import type { DialoguePlacement, PetPlacement } from './dialogue-settings.js'

export const PROTOCOL_VERSION = 12 as const

export const HISTORY_LIMIT = 20
export const HISTORY_MESSAGE_MAX_CHARS = 2000
export const HISTORY_BLOCK_LIMIT = 8
export const HISTORY_IMAGE_NAME_MAX_CHARS = 200
export const HISTORY_IMAGE_DIMENSION_MAX = 100000
export const REPLY_MAX_CHARS = 8000
export const INPUT_ATTACHMENT_LIMIT = 4
export const INPUT_IMAGE_BASE64_MAX_CHARS = 3_000_000
export const INPUT_FILE_PATH_MAX_CHARS = 2048
export const TARGET_WORKSPACE_LIMIT = 64
export const TARGET_SESSIONS_PER_WORKSPACE = 100
export const TARGET_UNGOURPED_LIMIT = 100
export const TARGET_ID_MAX_CHARS = 200
export const TARGET_TITLE_MAX_CHARS = 200
export const TARGET_PATH_MAX_CHARS = 2048

export type HistoryRole = 'user' | 'assistant'
export type HistoryTextBlock = { type: 'text', text: string }
export type HistoryImageBlock = { type: 'image', name: string, width: number, height: number }
export type HistoryBlock = HistoryTextBlock | HistoryImageBlock
export type HistoryMessage = { role: HistoryRole, blocks: readonly HistoryBlock[] }

export type ImageMediaType = 'image/png' | 'image/jpeg' | 'image/webp' | 'image/gif'

export type HelperAttachment =
  | { type: 'image', mediaType: ImageMediaType, base64: string, name?: string }
  | { type: 'file', path: string, name?: string }

export const displayLabels = {
  idle: '',
  waiting: '等待你的操作',
  question: '等你回答…',
  success: '已完成',
  error: '发生错误',
  disconnected: '未连接',
} as const

const activityLabels = {
  thinking: '思考中…',
  working: '工作中…',
  responding: '输出中…',
} as const

export type Activity = keyof typeof activityLabels
export type State = 'active' | keyof typeof displayLabels
export type CompanionState = State
export type HelperLifecycleMessageKind = 'ready' | 'closed'
export type RandomChatTopic = 'news' | 'weather' | 'discovery'
export type RandomChatError = 'not-configured' | 'unavailable'
export type HelperMessageKind = HelperLifecycleMessageKind | 'close-requested' | 'input' | 'stop' | 'request-history' | 'target-open' | 'target-answer' | 'random-chat-open' | 'dialogue-closed'
export type InputStatus = 'queued' | 'sent' | 'no-default-session' | 'session-unavailable' | 'rejected' | 'stopped' | 'interrupted' | 'failed'
export type ClearPreviewReason = 'disabled' | 'next-input' | 'cancelled' | 'closed' | 'session-unavailable'
export type HostMessageKind = 'hello' | 'config' | 'state' | 'shutdown' | 'conversation-config' | 'input-status' | 'reply-preview' | 'clear-preview' | 'reply' | 'conversation-history' | 'target-request' | 'random-chat-ready' | 'random-chat-error' | 'random-chat-test'

export type TargetWorkspace = {
  id: string
  title: string
  path: string
}

export type TargetSession = {
  id: string
  title: string
  blank: boolean
}

export type HelperLifecycleMessage = {
  version: typeof PROTOCOL_VERSION
  kind: HelperLifecycleMessageKind
}

/** A deliberate local exit. The Host keeps the pet closed for this DSH lifetime. */
export type HelperCloseRequestedMessage = {
  version: typeof PROTOCOL_VERSION
  kind: 'close-requested'
}

export type HelperInputMessage = {
  version: typeof PROTOCOL_VERSION
  kind: 'input'
  requestId: number
  text: string
  attachments?: readonly HelperAttachment[]
}

export type HelperStopMessage = {
  version: typeof PROTOCOL_VERSION
  kind: 'stop'
  requestId: number
}

export type HelperHistoryRequest = {
  version: typeof PROTOCOL_VERSION
  kind: 'request-history'
  requestId: number
}

export type HelperTargetOpenMessage = {
  version: typeof PROTOCOL_VERSION
  kind: 'target-open'
  requestId: number
}

export type HelperTargetAnswerMessage = {
  version: typeof PROTOCOL_VERSION
  kind: 'target-answer'
  requestId: number
  sessionId: string | null
  workspaceId: string | null
  newBlank: boolean
  /** Present only when creating a workspace: an existing directory to register. */
  path?: string
  /** True only when the answer requests a workspace create. */
  newWorkspace?: boolean
}

export type HelperRandomChatOpenMessage = {
  version: typeof PROTOCOL_VERSION
  kind: 'random-chat-open'
  invitationId: number
  topic: RandomChatTopic
}

export type HelperDialogueClosedMessage = {
  version: typeof PROTOCOL_VERSION
  kind: 'dialogue-closed'
}

export type HelperMessage = HelperLifecycleMessage | HelperCloseRequestedMessage | HelperInputMessage | HelperStopMessage | HelperHistoryRequest | HelperTargetOpenMessage | HelperTargetAnswerMessage | HelperRandomChatOpenMessage | HelperDialogueClosedMessage

export type HostMessage =
  | { version: typeof PROTOCOL_VERSION, kind: 'hello' | 'shutdown' }
  | { version: typeof PROTOCOL_VERSION, kind: 'config', scale: 0.75 | 1 | 1.25 | 1.5, reducedMotion: boolean, petPlacement: PetPlacement, dialoguePlacement: DialoguePlacement, dialogueWidth: number, dialogueHeight: number, randomChatEnabled: boolean, randomChatBrowseOnOpen: boolean, randomChatConfigured: boolean, randomChatMinIntervalMinutes: number, randomChatMaxIntervalMinutes: number, randomChatCustomPrompts: readonly string[] }
  | { version: typeof PROTOCOL_VERSION, kind: 'state', state: State, activities: readonly Activity[], label: string, sequence: number }
  | { version: typeof PROTOCOL_VERSION, kind: 'conversation-config', previewEnabled: boolean, previewMaxChars: number, defaultSessionId: string | null, defaultWorkspaceId: string | null }
  | { version: typeof PROTOCOL_VERSION, kind: 'input-status', requestId: number, status: InputStatus }
  | { version: typeof PROTOCOL_VERSION, kind: 'reply-preview', requestId: number, text: string, completed: boolean }
  | { version: typeof PROTOCOL_VERSION, kind: 'clear-preview', requestId: number, reason: ClearPreviewReason }
  | { version: typeof PROTOCOL_VERSION, kind: 'reply', requestId: number, text: string, completed: boolean }
  | { version: typeof PROTOCOL_VERSION, kind: 'conversation-history', requestId: number, available: boolean, messages: readonly HistoryMessage[] }
  | { version: typeof PROTOCOL_VERSION, kind: 'target-request', requestId: number, workspaces: readonly TargetWorkspace[], sessionsByWorkspace: Readonly<Record<string, readonly TargetSession[]>>, ungrouped: readonly TargetSession[], defaultWorkspaceId: string | null, defaultSessionId: string | null, error?: string }
  | { version: typeof PROTOCOL_VERSION, kind: 'random-chat-ready', invitationId: number }
  | { version: typeof PROTOCOL_VERSION, kind: 'random-chat-error', invitationId: number, reason: RandomChatError }
  | { version: typeof PROTOCOL_VERSION, kind: 'random-chat-test' }

export type HostOutboundMessage =
  | { kind: 'hello' | 'shutdown' }
  | { kind: 'config', scale: 0.75 | 1 | 1.25 | 1.5, reducedMotion: boolean, petPlacement: PetPlacement, dialoguePlacement: DialoguePlacement, dialogueWidth: number, dialogueHeight: number, randomChatEnabled: boolean, randomChatBrowseOnOpen: boolean, randomChatConfigured: boolean, randomChatMinIntervalMinutes: number, randomChatMaxIntervalMinutes: number, randomChatCustomPrompts: readonly string[] }
  | { kind: 'state', state: State, activities: readonly Activity[], label: string, sequence: number }
  | { kind: 'conversation-config', previewEnabled: boolean, previewMaxChars: number, defaultSessionId: string | null, defaultWorkspaceId: string | null }
  | { kind: 'input-status', requestId: number, status: InputStatus }
  | { kind: 'reply-preview', requestId: number, text: string, completed: boolean }
  | { kind: 'clear-preview', requestId: number, reason: ClearPreviewReason }
  | { kind: 'reply', requestId: number, text: string, completed: boolean }
  | { kind: 'conversation-history', requestId: number, available: boolean, messages: readonly HistoryMessage[] }
  | { kind: 'target-request', requestId: number, workspaces: readonly TargetWorkspace[], sessionsByWorkspace: Readonly<Record<string, readonly TargetSession[]>>, ungrouped: readonly TargetSession[], defaultWorkspaceId: string | null, defaultSessionId: string | null, error?: string }
  | { kind: 'random-chat-ready', invitationId: number }
  | { kind: 'random-chat-error', invitationId: number, reason: RandomChatError }
  | { kind: 'random-chat-test' }

const maxLineLength = 16_000_000
const maxTextLength = 2_000
const minPreviewMaxChars = 80
const helperLifecycleKinds = new Set<HelperLifecycleMessageKind>(['ready', 'closed'])
const hostKinds = new Set<HostMessageKind>(['hello', 'config', 'state', 'shutdown', 'conversation-config', 'input-status', 'reply-preview', 'clear-preview', 'reply', 'conversation-history', 'target-request', 'random-chat-ready', 'random-chat-error', 'random-chat-test'])
const scales = new Set([0.75, 1, 1.25, 1.5])
const petPlacements = new Set<PetPlacement>(['center', 'top-left', 'top-right', 'bottom-left', 'bottom-right'])
const dialoguePlacements = new Set<DialoguePlacement>(['near-pet', 'center', 'top-left', 'top-right', 'bottom-left', 'bottom-right'])
const compositeActivities: readonly Activity[] = ['thinking', 'working']
const inputStatuses = new Set<InputStatus>(['queued', 'sent', 'no-default-session', 'session-unavailable', 'rejected', 'stopped', 'interrupted', 'failed'])
const clearPreviewReasons = new Set<ClearPreviewReason>(['disabled', 'next-input', 'cancelled', 'closed', 'session-unavailable'])
const imageMediaTypes = new Set<ImageMediaType>(['image/png', 'image/jpeg', 'image/webp', 'image/gif'])
const randomChatTopics = new Set<RandomChatTopic>(['news', 'weather', 'discovery'])
const randomChatErrors = new Set<RandomChatError>(['not-configured', 'unavailable'])

export function labelForPresentation(state: State, activities: readonly Activity[]): string {
  if (state === 'active') {
    if (activities.length === 0) throw new Error('active presentation requires activities')
    return activities.length === 2
      ? '思考中/工作中'
      : activityLabels[activities[0]]
  }
  if (activities.length !== 0) throw new Error('exclusive presentation cannot have activities')
  return displayLabels[state]
}

export function parseHelperMessage(line: string): HelperMessage {
  const message = parseObject(line, 'helper message')
  assertProtocolVersion(message, 'helper message')

  if (typeof message.kind !== 'string' || !isHelperMessageKind(message.kind)) {
    throw new Error('helper message has an unknown kind')
  }
  if (message.kind === 'ready' || message.kind === 'closed' || message.kind === 'close-requested') {
    assertExactKeys(message, ['version', 'kind'], 'helper message')
    return { version: PROTOCOL_VERSION, kind: message.kind }
  }
  if (message.kind === 'request-history') {
    assertExactKeys(message, ['version', 'kind', 'requestId'], 'helper message')
    assertPositiveSafeInteger(message.requestId, 'helper message requestId')
    return { version: PROTOCOL_VERSION, kind: 'request-history', requestId: message.requestId }
  }
  if (message.kind === 'dialogue-closed') {
    assertExactKeys(message, ['version', 'kind'], 'helper message')
    return { version: PROTOCOL_VERSION, kind: 'dialogue-closed' }
  }
  if (message.kind === 'random-chat-open') {
    assertExactKeys(message, ['version', 'kind', 'invitationId', 'topic'], 'helper message')
    assertPositiveSafeInteger(message.invitationId, 'helper message invitationId')
    if (typeof message.topic !== 'string' || !randomChatTopics.has(message.topic as RandomChatTopic)) {
      throw new Error('helper message has an invalid random chat topic')
    }
    return { version: PROTOCOL_VERSION, kind: 'random-chat-open', invitationId: message.invitationId, topic: message.topic as RandomChatTopic }
  }
  if (message.kind === 'stop') {
    assertExactKeys(message, ['version', 'kind', 'requestId'], 'helper message')
    assertPositiveSafeInteger(message.requestId, 'helper message requestId')
    return { version: PROTOCOL_VERSION, kind: 'stop', requestId: message.requestId }
  }
  if (message.kind === 'target-open') {
    assertExactKeys(message, ['version', 'kind', 'requestId'], 'helper message')
    assertPositiveSafeInteger(message.requestId, 'helper message requestId')
    return { version: PROTOCOL_VERSION, kind: 'target-open', requestId: message.requestId }
  }
  if (message.kind === 'target-answer') {
    return parseTargetAnswer(message)
  }
  if (message.kind === 'input') {
    return parseInput(message)
  }

  throw new Error('helper message has an unknown kind')
}

export function parseHostMessage(line: string): HostMessage {
  const message = parseObject(line, 'host message')
  assertProtocolVersion(message, 'host message')

  if (typeof message.kind !== 'string' || !hostKinds.has(message.kind as HostMessageKind)) {
    throw new Error('host message has an unknown kind')
  }

  return addProtocolVersion(validateHostMessage(message))
}

export function encodeHostMessage(message: HostOutboundMessage | HostMessageKind): string {
  const outbound = typeof message === 'string'
    ? legacyOutboundMessage(message)
    : validateHostMessage(message)
  return `${JSON.stringify(addProtocolVersion(outbound))}\n`
}

function legacyOutboundMessage(kind: HostMessageKind): HostOutboundMessage {
  if (kind === 'hello' || kind === 'shutdown') return { kind }
  throw new Error(`host message ${kind} requires an explicit safe payload`)
}

function addProtocolVersion(message: HostOutboundMessage): HostMessage {
  return { version: PROTOCOL_VERSION, ...message } as HostMessage
}

function validateHostMessage(value: Record<string, unknown> | HostOutboundMessage): HostOutboundMessage {
  switch (value.kind) {
    case 'hello':
    case 'shutdown':
    case 'random-chat-test':
      assertExactKeys(value, ['version', 'kind'], 'host message', ['kind'])
      return { kind: value.kind }
    case 'config':
      assertExactKeys(value, ['version', 'kind', 'scale', 'reducedMotion', 'petPlacement', 'dialoguePlacement', 'dialogueWidth', 'dialogueHeight', 'randomChatEnabled', 'randomChatBrowseOnOpen', 'randomChatConfigured', 'randomChatMinIntervalMinutes', 'randomChatMaxIntervalMinutes', 'randomChatCustomPrompts'], 'host message', ['kind', 'scale', 'reducedMotion', 'petPlacement', 'dialoguePlacement', 'dialogueWidth', 'dialogueHeight', 'randomChatEnabled', 'randomChatBrowseOnOpen', 'randomChatConfigured', 'randomChatMinIntervalMinutes', 'randomChatMaxIntervalMinutes', 'randomChatCustomPrompts'])
      if (typeof value.scale !== 'number' || !scales.has(value.scale) || typeof value.reducedMotion !== 'boolean'
        || typeof value.petPlacement !== 'string' || !petPlacements.has(value.petPlacement as PetPlacement)
        || typeof value.dialoguePlacement !== 'string' || !dialoguePlacements.has(value.dialoguePlacement as DialoguePlacement)
        || !isDialogueWidth(value.dialogueWidth) || !isDialogueHeight(value.dialogueHeight)
        || typeof value.randomChatEnabled !== 'boolean' || typeof value.randomChatBrowseOnOpen !== 'boolean' || typeof value.randomChatConfigured !== 'boolean'
        || !isRandomChatIntervalMinutes(value.randomChatMinIntervalMinutes) || !isRandomChatIntervalMinutes(value.randomChatMaxIntervalMinutes)
        || value.randomChatMinIntervalMinutes > value.randomChatMaxIntervalMinutes || !isRandomChatCustomPrompts(value.randomChatCustomPrompts)) {
        throw new Error('host message has an invalid config')
      }
      return {
        kind: 'config',
        scale: value.scale as 0.75 | 1 | 1.25 | 1.5,
        reducedMotion: value.reducedMotion,
        petPlacement: value.petPlacement as PetPlacement,
        dialoguePlacement: value.dialoguePlacement as DialoguePlacement,
        dialogueWidth: value.dialogueWidth,
        dialogueHeight: value.dialogueHeight,
        randomChatEnabled: value.randomChatEnabled,
        randomChatBrowseOnOpen: value.randomChatBrowseOnOpen,
        randomChatConfigured: value.randomChatConfigured,
        randomChatMinIntervalMinutes: value.randomChatMinIntervalMinutes,
        randomChatMaxIntervalMinutes: value.randomChatMaxIntervalMinutes,
        randomChatCustomPrompts: [...value.randomChatCustomPrompts],
      }
    case 'state':
      assertExactKeys(value, ['version', 'kind', 'state', 'activities', 'label', 'sequence'], 'host message', ['kind', 'state', 'activities', 'label', 'sequence'])
      if (typeof value.state !== 'string' || (value.state !== 'active' && !Object.hasOwn(displayLabels, value.state))) {
        throw new Error('host message has an unknown state')
      }
      if (!isCanonicalActivities(value.state as State, value.activities)) {
        throw new Error('host message has invalid activities')
      }
      if (typeof value.label !== 'string' || value.label !== labelForPresentation(value.state as State, value.activities as readonly Activity[])) {
        throw new Error('host message has an invalid label')
      }
      const sequence = value.sequence
      if (typeof sequence !== 'number' || !Number.isSafeInteger(sequence) || sequence < 0) {
        throw new Error('host message has an invalid sequence')
      }
      return {
        kind: 'state',
        state: value.state as State,
        activities: [...value.activities as Activity[]],
        label: value.label,
        sequence,
      }
    case 'conversation-config':
      assertExactKeys(value, ['version', 'kind', 'previewEnabled', 'previewMaxChars', 'defaultSessionId', 'defaultWorkspaceId'], 'host message', ['kind', 'previewEnabled', 'previewMaxChars', 'defaultSessionId', 'defaultWorkspaceId'])
      if (typeof value.previewEnabled !== 'boolean') throw new Error('host message has an invalid previewEnabled')
      if (!isPreviewMaxChars(value.previewMaxChars)) throw new Error('host message has an invalid previewMaxChars')
      if (value.defaultSessionId !== null && (typeof value.defaultSessionId !== 'string' || value.defaultSessionId.length === 0)) {
        throw new Error('host message has an invalid defaultSessionId')
      }
      if (value.defaultWorkspaceId !== null && (typeof value.defaultWorkspaceId !== 'string' || value.defaultWorkspaceId.length === 0)) {
        throw new Error('host message has an invalid defaultWorkspaceId')
      }
      return { kind: 'conversation-config', previewEnabled: value.previewEnabled, previewMaxChars: value.previewMaxChars, defaultSessionId: value.defaultSessionId as string | null, defaultWorkspaceId: value.defaultWorkspaceId as string | null }
    case 'input-status':
      assertExactKeys(value, ['version', 'kind', 'requestId', 'status'], 'host message', ['kind', 'requestId', 'status'])
      assertPositiveSafeInteger(value.requestId, 'host message requestId')
      if (typeof value.status !== 'string' || !inputStatuses.has(value.status as InputStatus)) {
        throw new Error('host message has an invalid status')
      }
      return { kind: 'input-status', requestId: value.requestId, status: value.status as InputStatus }
    case 'reply-preview':
      assertExactKeys(value, ['version', 'kind', 'requestId', 'text', 'completed'], 'host message', ['kind', 'requestId', 'text', 'completed'])
      assertPositiveSafeInteger(value.requestId, 'host message requestId')
      assertStreamText(value.text, 'host message text')
      if (typeof value.completed !== 'boolean') throw new Error('host message has an invalid completed')
      return { kind: 'reply-preview', requestId: value.requestId, text: value.text, completed: value.completed }
    case 'clear-preview':
      assertExactKeys(value, ['version', 'kind', 'requestId', 'reason'], 'host message', ['kind', 'requestId', 'reason'])
      assertPositiveSafeInteger(value.requestId, 'host message requestId')
      if (typeof value.reason !== 'string' || !clearPreviewReasons.has(value.reason as ClearPreviewReason)) {
        throw new Error('host message has an invalid reason')
      }
      return { kind: 'clear-preview', requestId: value.requestId, reason: value.reason as ClearPreviewReason }
    case 'reply':
      assertExactKeys(value, ['version', 'kind', 'requestId', 'text', 'completed'], 'host message', ['kind', 'requestId', 'text', 'completed'])
      assertPositiveSafeInteger(value.requestId, 'host message requestId')
      if (typeof value.text !== 'string' || value.text.length === 0 || value.text.length > REPLY_MAX_CHARS) {
        throw new Error('host message has an invalid reply text')
      }
      if (typeof value.completed !== 'boolean') throw new Error('host message has an invalid completed')
      return { kind: 'reply', requestId: value.requestId, text: value.text, completed: value.completed }
    case 'conversation-history':
      assertExactKeys(value, ['version', 'kind', 'requestId', 'available', 'messages'], 'host message', ['kind', 'requestId', 'available', 'messages'])
      assertPositiveSafeInteger(value.requestId, 'host message requestId')
      if (typeof value.available !== 'boolean') throw new Error('host message has an invalid available')
      if (!isHistoryMessages(value.messages)) throw new Error('host message has invalid history messages')
      return { kind: 'conversation-history', requestId: value.requestId, available: value.available, messages: [...value.messages as HistoryMessage[]] }
    case 'target-request':
      assertExactKeys(value, ['version', 'kind', 'requestId', 'workspaces', 'sessionsByWorkspace', 'ungrouped', 'defaultWorkspaceId', 'defaultSessionId', 'error'], 'host message', ['kind', 'requestId', 'workspaces', 'sessionsByWorkspace', 'ungrouped', 'defaultWorkspaceId', 'defaultSessionId'])
      assertPositiveSafeInteger(value.requestId, 'host message requestId')
      if (value.error !== undefined && (typeof value.error !== 'string' || value.error.length === 0 || value.error.length > TARGET_TITLE_MAX_CHARS)) {
        throw new Error('host message has an invalid target error')
      }
      return {
        kind: 'target-request',
        requestId: value.requestId,
        workspaces: assertTargetWorkspaces(value.workspaces),
        sessionsByWorkspace: assertSessionsByWorkspace(value.sessionsByWorkspace),
        ungrouped: assertTargetSessions(value.ungrouped, 'host message ungrouped', TARGET_UNGOURPED_LIMIT),
        defaultWorkspaceId: assertNullableId(value.defaultWorkspaceId, 'host message defaultWorkspaceId'),
        defaultSessionId: assertNullableId(value.defaultSessionId, 'host message defaultSessionId'),
        ...(value.error === undefined ? {} : { error: value.error }),
      }
    case 'random-chat-ready':
      assertExactKeys(value, ['version', 'kind', 'invitationId'], 'host message', ['kind', 'invitationId'])
      assertPositiveSafeInteger(value.invitationId, 'host message invitationId')
      return { kind: 'random-chat-ready', invitationId: value.invitationId }
    case 'random-chat-error':
      assertExactKeys(value, ['version', 'kind', 'invitationId', 'reason'], 'host message', ['kind', 'invitationId', 'reason'])
      assertPositiveSafeInteger(value.invitationId, 'host message invitationId')
      if (typeof value.reason !== 'string' || !randomChatErrors.has(value.reason as RandomChatError)) {
        throw new Error('host message has an invalid random chat error')
      }
      return { kind: 'random-chat-error', invitationId: value.invitationId, reason: value.reason as RandomChatError }
    default:
      throw new Error('host message has an unknown kind')
  }
}

function isHelperMessageKind(value: string): value is HelperMessageKind {
  return value === 'close-requested' || value === 'input' || value === 'stop' || value === 'request-history' || value === 'target-open' || value === 'target-answer' || value === 'random-chat-open' || value === 'dialogue-closed' || helperLifecycleKinds.has(value as HelperLifecycleMessageKind)
}

function parseInput(message: Record<string, unknown>): HelperInputMessage {
  assertAllowedKeys(message, ['version', 'kind', 'requestId', 'text', 'attachments'], 'helper message')
  assertPositiveSafeInteger(message.requestId, 'helper message requestId')
  if (typeof message.text !== 'string' || message.text.length > maxTextLength || message.text !== message.text.trim()) {
    throw new Error('helper message text must be trimmed and at most 2000 characters')
  }
  const attachments = message.attachments === undefined ? undefined : assertHelperAttachments(message.attachments)
  if (message.text.length === 0 && (attachments === undefined || attachments.length === 0)) {
    throw new Error('helper message requires text or an attachment')
  }
  return { version: PROTOCOL_VERSION, kind: 'input', requestId: message.requestId, text: message.text, ...(attachments === undefined ? {} : { attachments }) }
}

function assertHelperAttachments(value: unknown): HelperAttachment[] {
  if (!Array.isArray(value) || value.length === 0 || value.length > INPUT_ATTACHMENT_LIMIT) {
    throw new Error('helper message has invalid attachments')
  }
  return value.map((entry, index) => {
    if (entry === null || typeof entry !== 'object' || Array.isArray(entry)) {
      throw new Error(`helper message attachment ${index} is invalid`)
    }
    const record = entry as Record<string, unknown>
    if (record.type === 'image') {
      if (typeof record.mediaType !== 'string' || !imageMediaTypes.has(record.mediaType as ImageMediaType)
        || typeof record.base64 !== 'string' || record.base64.length === 0 || record.base64.length > INPUT_IMAGE_BASE64_MAX_CHARS) {
        throw new Error(`helper message attachment ${index} is an invalid image`)
      }
      if (record.name !== undefined && (typeof record.name !== 'string' || record.name.length === 0 || record.name.length > TARGET_TITLE_MAX_CHARS)) {
        throw new Error(`helper message attachment ${index} has an invalid name`)
      }
      return { type: 'image', mediaType: record.mediaType as ImageMediaType, base64: record.base64, ...(record.name === undefined ? {} : { name: record.name }) }
    }
    if (record.type === 'file') {
      if (typeof record.path !== 'string' || record.path.length === 0 || record.path.length > INPUT_FILE_PATH_MAX_CHARS) {
        throw new Error(`helper message attachment ${index} is an invalid file`)
      }
      if (record.name !== undefined && (typeof record.name !== 'string' || record.name.length === 0 || record.name.length > TARGET_TITLE_MAX_CHARS)) {
        throw new Error(`helper message attachment ${index} has an invalid name`)
      }
      return { type: 'file', path: record.path, ...(record.name === undefined ? {} : { name: record.name }) }
    }
    throw new Error(`helper message attachment ${index} has an unknown type`)
  })
}

function parseTargetAnswer(message: Record<string, unknown>): HelperTargetAnswerMessage {
  assertAllowedKeys(message, ['version', 'kind', 'requestId', 'sessionId', 'workspaceId', 'newBlank', 'path', 'newWorkspace'], 'helper message')
  assertPositiveSafeInteger(message.requestId, 'helper message requestId')
  if (typeof message.newBlank !== 'boolean') throw new Error('helper message has an invalid newBlank')
  const sessionId = assertNullableId(message.sessionId, 'helper message sessionId')
  const workspaceId = assertNullableId(message.workspaceId, 'helper message workspaceId')
  const newWorkspace = message.newWorkspace === undefined ? false : message.newWorkspace
  if (typeof newWorkspace !== 'boolean') throw new Error('helper message has an invalid newWorkspace')

  if (newWorkspace) {
    if (message.path === undefined || typeof message.path !== 'string' || message.path.length === 0 || message.path.length > TARGET_PATH_MAX_CHARS) {
      throw new Error('helper message has an invalid path')
    }
    if (sessionId !== null || workspaceId !== null || message.newBlank) {
      throw new Error('helper message has an invalid workspace create answer')
    }
    return { version: PROTOCOL_VERSION, kind: 'target-answer', requestId: message.requestId, sessionId: null, workspaceId: null, newBlank: false, path: message.path, newWorkspace: true }
  }

  if (message.path !== undefined) throw new Error('helper message has an unexpected path')

  if (message.newBlank) {
    // "+ 新对话": the host mints the session; no id may be attached yet.
    if (sessionId !== null) throw new Error('helper message has an invalid new blank answer')
    return { version: PROTOCOL_VERSION, kind: 'target-answer', requestId: message.requestId, sessionId: null, workspaceId, newBlank: true }
  }

  if (sessionId === null) throw new Error('helper message has an empty target answer')
  return { version: PROTOCOL_VERSION, kind: 'target-answer', requestId: message.requestId, sessionId, workspaceId, newBlank: false }
}

function isHistoryMessages(value: unknown): value is readonly HistoryMessage[] {
  if (!Array.isArray(value) || value.length > HISTORY_LIMIT) return false
  return value.every((entry) => {
    if (entry === null || typeof entry !== 'object' || Array.isArray(entry)) return false
    const record = entry as Record<string, unknown>
    return (record.role === 'user' || record.role === 'assistant')
      && isHistoryBlocks(record.blocks)
  })
}

function isHistoryBlocks(value: unknown): value is readonly HistoryBlock[] {
  if (!Array.isArray(value) || value.length === 0 || value.length > HISTORY_BLOCK_LIMIT) return false
  return value.every((entry) => {
    if (entry === null || typeof entry !== 'object' || Array.isArray(entry)) return false
    const record = entry as Record<string, unknown>
    if (record.type === 'text') {
      return typeof record.text === 'string' && record.text.length > 0 && record.text.length <= HISTORY_MESSAGE_MAX_CHARS
    }
    if (record.type === 'image') {
      return typeof record.name === 'string' && record.name.length <= HISTORY_IMAGE_NAME_MAX_CHARS
        && typeof record.width === 'number' && Number.isSafeInteger(record.width) && record.width >= 1 && record.width <= HISTORY_IMAGE_DIMENSION_MAX
        && typeof record.height === 'number' && Number.isSafeInteger(record.height) && record.height >= 1 && record.height <= HISTORY_IMAGE_DIMENSION_MAX
    }
    return false
  })
}

function assertPositiveSafeInteger(value: unknown, subject: string): asserts value is number {
  if (typeof value !== 'number' || !Number.isSafeInteger(value) || value <= 0) {
    throw new Error(`${subject} must be a positive safe integer`)
  }
}

function assertStreamText(value: unknown, subject: string): asserts value is string {
  if (typeof value !== 'string' || value.length === 0 || value.length > REPLY_MAX_CHARS) {
    throw new Error(`${subject} must be between 1 and ${REPLY_MAX_CHARS} characters`)
  }
}

function isPreviewMaxChars(value: unknown): value is number {
  return typeof value === 'number'
    && Number.isSafeInteger(value)
    && value >= minPreviewMaxChars
    && value <= REPLY_MAX_CHARS
}

function isDialogueWidth(value: unknown): value is number {
  return typeof value === 'number' && Number.isSafeInteger(value) && value >= 220 && value <= 4000
}

function isDialogueHeight(value: unknown): value is number {
  return typeof value === 'number' && Number.isSafeInteger(value) && value >= 240 && value <= 3000
}

function isRandomChatIntervalMinutes(value: unknown): value is number {
  return typeof value === 'number' && Number.isSafeInteger(value) && value >= 5 && value <= 1440
}

function isRandomChatCustomPrompts(value: unknown): value is readonly string[] {
  return Array.isArray(value)
    && value.length <= 12
    && value.every((prompt) => typeof prompt === 'string' && prompt.length > 0 && prompt.length <= 120 && !prompt.includes('\n') && !prompt.includes('\r'))
    && new Set(value).size === value.length
}

function isCanonicalActivities(state: State, value: unknown): value is readonly Activity[] {
  if (!Array.isArray(value) || !value.every((activity): activity is Activity => typeof activity === 'string' && Object.hasOwn(activityLabels, activity))) {
    return false
  }

  if (state !== 'active') return value.length === 0
  if (value.length === 1) return true
  return value.length === compositeActivities.length
    && value.every((activity, index) => activity === compositeActivities[index])
}

function parseObject(line: string, subject: string): Record<string, unknown> {
  if (line.length > maxLineLength) throw new Error(`${subject} is too long`)

  let value: unknown
  try {
    value = JSON.parse(line)
  } catch {
    throw new Error(`${subject} must be valid JSON`)
  }

  if (value === null || typeof value !== 'object' || Array.isArray(value)) {
    throw new Error(`${subject} must be an object`)
  }
  return value as Record<string, unknown>
}

function assertProtocolVersion(message: Record<string, unknown>, subject: string): void {
  if (message.version !== PROTOCOL_VERSION) {
    throw new Error(`${subject} has an unsupported version`)
  }
}

function assertExactKeys(
  message: Record<string, unknown>,
  protocolKeys: readonly string[],
  subject: string,
  requiredKeys: readonly string[] = protocolKeys,
): void {
  const allowed = new Set(protocolKeys)
  if (Object.keys(message).some((key) => !allowed.has(key))) {
    throw new Error(`${subject} has unexpected fields`)
  }
  if (requiredKeys.some((key) => !Object.hasOwn(message, key))) {
    throw new Error(`${subject} is missing required fields`)
  }
}

function assertAllowedKeys(
  message: Record<string, unknown>,
  allowedKeys: readonly string[],
  subject: string,
): void {
  const allowed = new Set(allowedKeys)
  if (Object.keys(message).some((key) => !allowed.has(key))) {
    throw new Error(`${subject} has unexpected fields`)
  }
}

function assertNullableId(value: unknown, subject: string): string | null {
  if (value === null) return null
  if (typeof value !== 'string' || value.length === 0 || value.length > TARGET_ID_MAX_CHARS) {
    throw new Error(`${subject} must be a non-empty id or null`)
  }
  return value
}

function assertTargetWorkspaces(value: unknown): TargetWorkspace[] {
  if (!Array.isArray(value) || value.length > TARGET_WORKSPACE_LIMIT) {
    throw new Error('host message has invalid workspaces')
  }
  return value.map((entry, index) => {
    if (entry === null || typeof entry !== 'object' || Array.isArray(entry)) {
      throw new Error('host message has an invalid workspace')
    }
    const record = entry as Record<string, unknown>
    const id = assertNullableId(record.id, `host message workspace ${index} id`)
    const title = record.title
    const path = record.path
    if (id === null
      || typeof title !== 'string' || title.length === 0 || title.length > TARGET_TITLE_MAX_CHARS
      || typeof path !== 'string' || path.length === 0 || path.length > TARGET_PATH_MAX_CHARS) {
      throw new Error('host message has an invalid workspace')
    }
    return { id, title, path }
  })
}

function assertTargetSessions(value: unknown, subject: string, limit: number): TargetSession[] {
  if (!Array.isArray(value) || value.length > limit) {
    throw new Error(`${subject} has invalid sessions`)
  }
  return value.map((entry) => {
    if (entry === null || typeof entry !== 'object' || Array.isArray(entry)) {
      throw new Error(`${subject} has an invalid session`)
    }
    const record = entry as Record<string, unknown>
    const id = assertNullableId(record.id, `${subject} session id`)
    const title = record.title
    const blank = record.blank
    if (id === null
      || typeof title !== 'string' || title.length > TARGET_TITLE_MAX_CHARS
      || typeof blank !== 'boolean') {
      throw new Error(`${subject} has an invalid session`)
    }
    return { id, title, blank }
  })
}

function assertSessionsByWorkspace(value: unknown): Record<string, TargetSession[]> {
  if (value === null || typeof value !== 'object' || Array.isArray(value)) {
    throw new Error('host message has invalid sessionsByWorkspace')
  }
  const record = value as Record<string, unknown>
  const result: Record<string, TargetSession[]> = {}
  for (const key of Object.keys(record)) {
    if (key.length === 0 || key.length > TARGET_ID_MAX_CHARS) {
      throw new Error('host message has an invalid sessionsByWorkspace key')
    }
    result[key] = assertTargetSessions(record[key], `host message sessionsByWorkspace.${key}`, TARGET_SESSIONS_PER_WORKSPACE)
  }
  return result
}
