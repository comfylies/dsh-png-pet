import { CompanionBridge, createSessionObservers } from './companion-bridge.js'
import open from 'open'
import { ApprovalController, type ApprovalOutcome, type DshApprovalRequest } from './approval-controller.js'
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
export const inject = ['agents', 'apiProxy', 'attachments', 'sessionQuery', 'agentDefaultModel', 'webServer'] as const

export type SessionObserverContext = {
  on(name: 'session/event', listener: (session: unknown, event: unknown) => void): unknown
  on(name: 'session/disposed', listener: (session: unknown) => void): unknown
  on(name: 'approval/request', listener: (request: DshApprovalRequest, next: () => Promise<ApprovalOutcome>) => Promise<ApprovalOutcome>, options?: boolean): unknown
}

type PluginContext = SessionObserverContext & Omit<DshDialogueContext, 'settings'> & {
  apiProxy: TargetApi
  webServer: { port: number }
  effect(factory: () => () => void): void
  inject(services: readonly ['settings'], callback: (ctx: { settings: DshSettingsProvider }) => void): void
}

type Helper = Pick<HelperProcess, 'start' | 'send' | 'stop'>
type HelperFactory = (options: Pick<HelperProcessOptions, 'onMessage' | 'onExit'>) => Helper

export const helperRestartDelaysMs = Object.freeze([1_000, 5_000, 15_000])
export const helperStableRunMs = 60_000

export type HelperLifecycleOptions = {
  /** Test seam only; production uses a short, bounded exponential-like backoff. */
  restartDelaysMs?: readonly number[]
  /** A Helper that remains healthy for this period receives a fresh retry budget. */
  stableRunMs?: number
}

export function apply(ctx: PluginContext): void {
  applyWithHelper(ctx, (options) => new HelperProcess(options))
}

export function applyWithHelper(ctx: PluginContext, createHelper: HelperFactory, lifecycleOptions: HelperLifecycleOptions = {}): void {
  startDialogueHost(ctx, createHelper, lifecycleOptions)
}

function startDialogueHost(ctx: PluginContext, createHelper: HelperFactory, lifecycleOptions: HelperLifecycleOptions): void {
  let controller: DialogueController | undefined
  let approvalController: ApprovalController | undefined
  let targetController: TargetController | undefined
  let randomChatController: RandomChatController | undefined
  let settingsScope: DshDialogueSettingsScope | undefined
  let unwatchSettings = () => {}
  let pendingRandomChatTest = false
  let helperReady = false
  let disposed = false
  let restartSuppressed = false
  let restartAttempts = 0
  let restartTimer: NodeJS.Timeout | undefined
  let stableRunTimer: NodeJS.Timeout | undefined
  let helper: Helper | undefined
  const restartDelays = lifecycleOptions.restartDelaysMs ?? helperRestartDelaysMs
  const stableRunMs = lifecycleOptions.stableRunMs ?? helperStableRunMs
  const bridge = new CompanionBridge((message) => helper?.send(message))
  const openHarness = () => {
    const port = ctx.webServer.port
    if (!Number.isSafeInteger(port) || port < 1 || port > 65_535) return
    void open(`http://127.0.0.1:${port}`).catch(() => {
      // A user-initiated browser handoff must never destabilize the Host or expose its URL in logs.
      console.error('dsh-png-pet could not open Harness')
    })
  }

  const clearRestartTimer = () => {
    if (restartTimer) clearTimeout(restartTimer)
    restartTimer = undefined
  }

  const clearStableRunTimer = () => {
    if (stableRunTimer) clearTimeout(stableRunTimer)
    stableRunTimer = undefined
  }

  const publishWhenReady = () => {
    if (!helperReady || !helper || !controller || !settingsScope) return
    helper.send({ kind: 'hello' })
    helper.send(helperConfig(settingsScope.get()))
    if (pendingRandomChatTest) {
      helper.send({ kind: 'random-chat-test' })
      pendingRandomChatTest = false
    }
    controller.publishConversationConfig()
    bridge.publishCurrent()
  }

  const startHelper = () => {
    if (disposed || restartSuppressed || helper) return

    let terminationHandled = false
    const instance = createHelper({
      onMessage: (message) => {
        if (message.kind === 'close-requested') {
          restartSuppressed = true
          clearRestartTimer()
          clearStableRunTimer()
        }
        void routeHelperMessage(message, controller, targetController, randomChatController, openHarness, approvalController)
      },
      onExit: () => handleTermination(),
    })
    helper = instance

    const handleTermination = () => {
      if (terminationHandled) return
      terminationHandled = true
      if (helper !== instance) return
      helper = undefined
      helperReady = false
      approvalController?.helperUnavailable()
      clearStableRunTimer()

      if (disposed || restartSuppressed) return
      const delay = restartDelays[restartAttempts]
      if (delay === undefined) {
        // Fixed diagnostic only: never include child output, paths, or session data.
        console.error('dsh-png-pet helper restart budget exhausted')
        return
      }
      restartAttempts++
      restartTimer = setTimeout(() => {
        restartTimer = undefined
        startHelper()
      }, delay)
    }

    void instance.start().then(() => {
      if (disposed || restartSuppressed || helper !== instance) {
        void instance.stop()
        return
      }
      helperReady = true
      publishWhenReady()
      stableRunTimer = setTimeout(() => {
        if (helper === instance && helperReady) restartAttempts = 0
      }, stableRunMs)
      stableRunTimer.unref?.()
    }).catch(() => {
      handleTermination()
    })
  }

  startHelper()

  ctx.effect(() => () => {
    disposed = true
    clearRestartTimer()
    clearStableRunTimer()
    controller?.dispose()
    unwatchSettings()
    void helper?.stop()
  })

  ctx.inject(['settings'], (settingsCtx) => {
    const scope = settingsCtx.settings.register(settingsNamespace('dsh-png-pet'), dialogueSettingsSchema)
    settingsScope = scope
    const dialogueCtx = createDialogueContext(ctx, scope)
    controller = new DialogueController(dialogueCtx, (message) => helper?.send(message))
    approvalController = new ApprovalController(
      (sessionId) => controller?.isSelectedSession(sessionId) ?? false,
      () => helperReady && helper !== undefined,
      (message) => helper?.send(message),
      () => settingsScope?.get().approvalSurface === 'pet',
    )
    registerApprovalAnswerer(ctx, approvalController)
    targetController = new TargetController(
      ctx.apiProxy,
      scope,
      (message) => helper?.send(message),
      (sessionId, workspaceId) => controller?.setTemporaryTarget(sessionId, workspaceId),
    )
    randomChatController = new RandomChatController(
      ctx.apiProxy,
      scope,
      controller,
      (message) => helper?.send(message),
    )
    unwatchSettings = watchDialogueSettings(scope, controller, (settings, previous) => {
      const shouldTestRandomChat = settings.randomChatTestNonce !== previous.randomChatTestNonce
      if (!helperReady) {
        pendingRandomChatTest ||= shouldTestRandomChat
        return
      }
      if (helperConfigChanged(settings, previous)) helper?.send(helperConfig(settings))
      if (shouldTestRandomChat) helper?.send({ kind: 'random-chat-test' })
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
  openHarness?: () => void,
  approvalController?: Pick<ApprovalController, 'answer'>,
): Promise<void> {
  if (message.kind === 'open-harness') {
    try {
      openHarness?.()
    } catch {
      // A user-initiated browser handoff must never disrupt dialogue routing.
    }
    return Promise.resolve()
  }
  if (message.kind === 'approval-answer') {
    try {
      approvalController?.answer(message)
    } catch {
      // A stale or malformed local decision must fail closed inside the controller.
    }
    return Promise.resolve()
  }
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

/**
 * Claims only the currently selected conversation's requests. All other
 * requests delegate to DSH's built-in Web answerer, preserving Web ownership
 * for sessions the pet is not displaying.
 */
export function registerApprovalAnswerer(
  ctx: SessionObserverContext,
  controller: Pick<ApprovalController, 'request'>,
): void {
  ctx.on('approval/request', (request, next) => controller.request(request, next), true)
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
