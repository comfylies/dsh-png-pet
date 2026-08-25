import { CompanionBridge, createSessionObservers } from './companion-bridge.js'
import { DialogueController } from './dialogue-controller.js'
import { dialogueSettingsSchema } from './dialogue-settings.js'
import { settingsNamespace } from '@deepseek-ai/dsh-settings'
import { type DshDialogueContext, type DshDialogueSettingsScope, type DshSettingsProvider } from './dsh-dialogue-types.js'
import { HelperProcess, type HelperProcessMessage, type HelperProcessOptions } from './helper-process.js'

export const name = 'dsh-png-pet'

export type SessionObserverContext = {
  on(name: 'session/event', listener: (session: unknown, event: unknown) => void): unknown
  on(name: 'session/disposed', listener: (session: unknown) => void): unknown
}

type PluginContext = SessionObserverContext & Omit<DshDialogueContext, 'settings'> & {
  effect(factory: () => () => void): void
  inject(services: readonly ['settings'], callback: (ctx: { settings: DshSettingsProvider }) => void): void
}

type Helper = Pick<HelperProcess, 'start' | 'send' | 'stop'>
type HelperFactory = (options: Pick<HelperProcessOptions, 'onMessage'>) => Helper

export function apply(ctx: PluginContext, createHelper: HelperFactory = (options) => new HelperProcess(options)): void {
  ctx.inject(['settings'], (settingsCtx) => {
    const scope = settingsCtx.settings.register(settingsNamespace('dsh-png-pet'), dialogueSettingsSchema)
    startDialogueHost(ctx, scope, createHelper)
  })
}

function startDialogueHost(ctx: PluginContext, scope: DshDialogueSettingsScope, createHelper: HelperFactory): void {
  const dialogueCtx = createDialogueContext(ctx, scope)
  let controller: DialogueController | undefined
  const helper = createHelper({
    onMessage: (message) => routeHelperMessage(message, controller),
  })
  const bridge = new CompanionBridge((message) => helper.send(message))
  controller = new DialogueController(dialogueCtx, (message) => helper.send(message))
  const unwatchSettings = watchDialogueSettings(scope, controller)
  registerSessionObservers(ctx, bridge, controller)

  ctx.effect(() => () => {
    controller.dispose()
    unwatchSettings()
    void helper.stop()
  })

  void helper.start().then(() => {
    helper.send({ kind: 'hello' })
    helper.send({ kind: 'config', scale: 1, reducedMotion: false })
    controller.publishConversationConfig()
    bridge.publishCurrent()
  }).catch(() => {
    console.error('dsh-png-pet helper startup failed')
  })
}

export function createDialogueContext(
  ctx: Pick<DshDialogueContext, 'agents' | 'createUserMessage'>,
  settings: DshDialogueSettingsScope,
): DshDialogueContext {
  return {
    get agents() {
      return ctx.agents
    },
    createUserMessage(message) {
      return ctx.createUserMessage(message)
    },
    settings,
  }
}

export function routeHelperMessage(
  message: HelperProcessMessage,
  controller: Pick<DialogueController, 'acceptInput' | 'helperClosed'> | undefined,
): void {
  if (message.kind === 'input') {
    void controller?.acceptInput(message)
  } else {
    controller?.helperClosed()
  }
}

export function watchDialogueSettings(
  settings: Pick<DshDialogueContext['settings'], 'watch'>,
  controller: Pick<DialogueController, 'settingsChanged'>,
): () => void {
  return settings.watch((next, previous) => controller.settingsChanged(next, previous))
}

export function registerSessionObservers(
  ctx: SessionObserverContext,
  bridge: CompanionBridge,
  controller?: Pick<DialogueController, 'observeEvent' | 'sessionUnavailable'>,
): void {
  const observers = createSessionObservers(bridge)

  ctx.on('session/event', (session, event) => {
    try {
      const sessionId = readSessionId(session)
      if (sessionId !== undefined) controller?.observeEvent(sessionId, event)
      observers.sessionEvent(session, event)
    } catch {
      console.error('dsh-png-pet session event ignored')
    }
  })
  ctx.on('session/disposed', (session) => {
    try {
      const sessionId = readSessionId(session)
      if (sessionId !== undefined) controller?.sessionUnavailable(sessionId)
      observers.sessionDisposed(session)
    } catch {
      console.error('dsh-png-pet session disposal ignored')
    }
  })
}

function readSessionId(value: unknown): string | undefined {
  if (value === null || typeof value !== 'object' || Array.isArray(value)) return undefined
  const id = (value as { id?: unknown }).id
  return typeof id === 'string' && id.length > 0 ? id : undefined
}
