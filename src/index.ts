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

export function apply(ctx: PluginContext): void {
  applyWithHelper(ctx, (options) => new HelperProcess(options))
}

export function applyWithHelper(ctx: PluginContext, createHelper: HelperFactory): void {
  startDialogueHost(ctx, createHelper)
}

function startDialogueHost(ctx: PluginContext, createHelper: HelperFactory): void {
  let controller: DialogueController | undefined
  let unwatchSettings = () => {}
  let helperReady = false
  const helper = createHelper({
    onMessage: (message) => routeHelperMessage(message, controller),
  })
  const bridge = new CompanionBridge((message) => helper.send(message))

  const publishWhenReady = () => {
    if (!helperReady || !controller) return
    helper.send({ kind: 'hello' })
    helper.send({ kind: 'config', scale: 1, reducedMotion: false })
    controller.publishConversationConfig()
    bridge.publishCurrent()
  }

  void helper.start().then(() => {
    helperReady = true
    publishWhenReady()
  }).catch(() => {
    console.error('dsh-png-pet helper startup failed')
  })

  ctx.effect(() => () => {
    controller?.dispose()
    unwatchSettings()
    void helper.stop()
  })

  ctx.inject(['settings'], (settingsCtx) => {
    const scope = settingsCtx.settings.register(settingsNamespace('dsh-png-pet'), dialogueSettingsSchema)
    const dialogueCtx = createDialogueContext(ctx, scope)
    controller = new DialogueController(dialogueCtx, (message) => helper.send(message))
    unwatchSettings = watchDialogueSettings(scope, controller)
    registerSessionObservers(ctx, bridge, controller)
    publishWhenReady()
  })
}

export function createDialogueContext(
  ctx: Pick<DshDialogueContext, 'agents'>,
  settings: DshDialogueSettingsScope,
): DshDialogueContext {
  return {
    get agents() {
      return ctx.agents
    },
    settings,
  }
}

export function routeHelperMessage(
  message: HelperProcessMessage,
  controller: Pick<DialogueController, 'acceptInput' | 'helperClosed'> | undefined,
): Promise<void> {
  if (message.kind === 'input') {
    try {
      return Promise.resolve(controller?.acceptInput(message)).catch(() => {})
    } catch {
      return Promise.resolve()
    }
  }

  controller?.helperClosed()
  return Promise.resolve()
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
