import { CompanionBridge, createSessionObservers } from './companion-bridge.js'
import { DialogueController } from './dialogue-controller.js'
import { dialogueSettingsSchema, type DialogueSettings } from './dialogue-settings.js'
import { settingsNamespace } from '@deepseek-ai/dsh-settings'
import { type DshDialogueContext, type DshDialogueSettingsScope, type DshSettingsProvider } from './dsh-dialogue-types.js'
import { HelperProcess, type HelperProcessMessage, type HelperProcessOptions } from './helper-process.js'
import { TargetController } from './target-controller.js'
import { RandomChatController } from './random-chat-controller.js'
import type { TargetApi } from './target-service.js'

export const name = 'dsh-png-pet'

/**
 * Cordis service dependencies declared by this plugin. DSH mounts bundles on a
 * restricted context: services used through `ctx.*` MUST be declared here or
 * property access throws "cannot get property ... without inject".
 */
export const inject = ['agents', 'apiProxy', 'attachments', 'sessionQuery', 'agentDefaultModel'] as const

export type SessionObserverContext = {
  on(name: 'session/event', listener: (session: unknown, event: unknown) => void): unknown
  on(name: 'session/disposed', listener: (session: unknown) => void): unknown
}

type PluginContext = SessionObserverContext & Omit<DshDialogueContext, 'settings'> & {
  apiProxy: TargetApi
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
  let targetController: TargetController | undefined
  let randomChatController: RandomChatController | undefined
  let settingsScope: DshDialogueSettingsScope | undefined
  let unwatchSettings = () => {}
  let pendingRandomChatTest = false
  let helperReady = false
  const helper = createHelper({
    onMessage: (message) => routeHelperMessage(message, controller, targetController, randomChatController),
  })
  const bridge = new CompanionBridge((message) => helper.send(message))

  const publishWhenReady = () => {
    if (!helperReady || !controller || !settingsScope) return
    helper.send({ kind: 'hello' })
    helper.send(helperConfig(settingsScope.get()))
    if (pendingRandomChatTest) {
      helper.send({ kind: 'random-chat-test' })
      pendingRandomChatTest = false
    }
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
    settingsScope = scope
    const dialogueCtx = createDialogueContext(ctx, scope)
    controller = new DialogueController(dialogueCtx, (message) => helper.send(message))
    targetController = new TargetController(
      ctx.apiProxy,
      scope,
      (message) => helper.send(message),
      (sessionId, workspaceId) => controller?.setTemporaryTarget(sessionId, workspaceId),
    )
    randomChatController = new RandomChatController(
      ctx.apiProxy,
      scope,
      controller,
      (message) => helper.send(message),
    )
    unwatchSettings = watchDialogueSettings(scope, controller, (settings, previous) => {
      const shouldTestRandomChat = settings.randomChatTestNonce !== previous.randomChatTestNonce
      if (!helperReady) {
        pendingRandomChatTest ||= shouldTestRandomChat
        return
      }
      if (helperConfigChanged(settings, previous)) helper.send(helperConfig(settings))
      if (shouldTestRandomChat) helper.send({ kind: 'random-chat-test' })
    })
    registerSessionObservers(ctx, bridge, controller)
    publishWhenReady()
  })
}

export function createDialogueContext(
  ctx: Pick<DshDialogueContext, 'agents' | 'attachments' | 'sessionQuery' | 'agentDefaultModel'>,
  settings: DshDialogueSettingsScope,
): DshDialogueContext {
  return {
    get agents() {
      return ctx.agents
    },
    get attachments() {
      return ctx.attachments
    },
    get sessionQuery() {
      return ctx.sessionQuery
    },
    get agentDefaultModel() {
      return ctx.agentDefaultModel
    },
    settings,
  }
}

export function routeHelperMessage(
  message: HelperProcessMessage,
  controller: Pick<DialogueController, 'acceptInput' | 'requestHistory' | 'stop' | 'helperClosed'> | undefined,
  targetController?: Pick<TargetController, 'open' | 'answer'>,
  randomChatController?: Pick<RandomChatController, 'open' | 'dialogueClosed'>,
): Promise<void> {
  if (message.kind === 'input' || message.kind === 'request-history' || message.kind === 'stop') {
    try {
      const handled = message.kind === 'input'
        ? controller?.acceptInput(message)
        : message.kind === 'stop'
          ? controller?.stop(message.requestId)
          : controller?.requestHistory(message.requestId)
      return Promise.resolve(handled).catch(() => {})
    } catch {
      return Promise.resolve()
    }
  }

  if (message.kind === 'target-open' || message.kind === 'target-answer') {
    try {
      const handled = message.kind === 'target-open'
        ? targetController?.open(message)
        : targetController?.answer(message)
      return Promise.resolve(handled).catch(() => {})
    } catch {
      return Promise.resolve()
    }
  }

  if (message.kind === 'random-chat-open') {
    try {
      return Promise.resolve(randomChatController?.open(message)).then(() => {})
    } catch {
      return Promise.resolve()
    }
  }

  if (message.kind === 'dialogue-closed') {
    try {
      randomChatController?.dialogueClosed()
    } catch {
      // A local window close must never destabilize the helper host.
    }
    return Promise.resolve()
  }

  controller?.helperClosed()
  return Promise.resolve()
}

export function watchDialogueSettings(
  settings: Pick<DshDialogueContext['settings'], 'watch'>,
  controller: Pick<DialogueController, 'settingsChanged'>,
  publishConfig?: (settings: DialogueSettings, previous: DialogueSettings) => void,
): () => void {
  return settings.watch((next, previous) => {
    controller.settingsChanged(next, previous)
    publishConfig?.(next, previous)
  })
}

function helperConfig(settings: DialogueSettings) {
  return {
    kind: 'config' as const,
    scale: settings.scale,
    reducedMotion: settings.reducedMotion,
    petPlacement: settings.petPlacement,
    dialoguePlacement: settings.dialoguePlacement,
    dialogueWidth: settings.dialogueWidth,
    dialogueHeight: settings.dialogueHeight,
    randomChatEnabled: settings.randomChatEnabled,
    randomChatBrowseOnOpen: settings.randomChatBrowseOnOpen,
    randomChatConfigured: settings.randomChatWorkspaceIds.length > 0,
    randomChatMinIntervalMinutes: settings.randomChatMinIntervalMinutes,
    randomChatMaxIntervalMinutes: settings.randomChatMaxIntervalMinutes,
    randomChatCustomPrompts: settings.randomChatCustomPrompts,
  }
}

function helperConfigChanged(next: DialogueSettings, previous: DialogueSettings): boolean {
  return JSON.stringify(helperConfig(next)) !== JSON.stringify(helperConfig(previous))
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
