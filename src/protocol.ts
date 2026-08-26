export const PROTOCOL_VERSION = 5 as const

export const HISTORY_LIMIT = 20
export const HISTORY_MESSAGE_MAX_CHARS = 2000
export const REPLY_MAX_CHARS = 8000

export type HistoryRole = 'user' | 'assistant'
export type HistoryMessage = { role: HistoryRole, text: string }

export const displayLabels = {
  idle: '',
  waiting: '等待你的操作',
  success: '已完成',
  error: '发生错误',
  disconnected: '未连接',
} as const

const activityLabels = {
  thinking: '思考中…',
  working: '工作中…',
} as const

export type Activity = keyof typeof activityLabels
export type State = 'active' | keyof typeof displayLabels
export type CompanionState = State
export type HelperLifecycleMessageKind = 'ready' | 'closed'
export type HelperMessageKind = HelperLifecycleMessageKind | 'input' | 'request-history'
export type InputStatus = 'queued' | 'sent' | 'no-default-session' | 'session-unavailable' | 'rejected'
export type ClearPreviewReason = 'disabled' | 'next-input' | 'cancelled' | 'closed' | 'session-unavailable'
export type HostMessageKind = 'hello' | 'config' | 'state' | 'shutdown' | 'conversation-config' | 'input-status' | 'reply-preview' | 'clear-preview' | 'reply' | 'conversation-history'

export type HelperLifecycleMessage = {
  version: typeof PROTOCOL_VERSION
  kind: HelperLifecycleMessageKind
}

export type HelperInputMessage = {
  version: typeof PROTOCOL_VERSION
  kind: 'input'
  requestId: number
  text: string
}

export type HelperHistoryRequest = {
  version: typeof PROTOCOL_VERSION
  kind: 'request-history'
  requestId: number
}

export type HelperMessage = HelperLifecycleMessage | HelperInputMessage | HelperHistoryRequest

export type HostMessage =
  | { version: typeof PROTOCOL_VERSION, kind: 'hello' | 'shutdown' }
  | { version: typeof PROTOCOL_VERSION, kind: 'config', scale: 0.75 | 1 | 1.25 | 1.5, reducedMotion: boolean }
  | { version: typeof PROTOCOL_VERSION, kind: 'state', state: State, activities: readonly Activity[], label: string, sequence: number }
  | { version: typeof PROTOCOL_VERSION, kind: 'conversation-config', previewEnabled: boolean, previewMaxChars: number, defaultSessionId: string | null }
  | { version: typeof PROTOCOL_VERSION, kind: 'input-status', requestId: number, status: InputStatus }
  | { version: typeof PROTOCOL_VERSION, kind: 'reply-preview', requestId: number, text: string, completed: boolean }
  | { version: typeof PROTOCOL_VERSION, kind: 'clear-preview', requestId: number, reason: ClearPreviewReason }
  | { version: typeof PROTOCOL_VERSION, kind: 'reply', requestId: number, text: string, completed: boolean }
  | { version: typeof PROTOCOL_VERSION, kind: 'conversation-history', requestId: number, available: boolean, messages: readonly HistoryMessage[] }

export type HostOutboundMessage =
  | { kind: 'hello' | 'shutdown' }
  | { kind: 'config', scale: 0.75 | 1 | 1.25 | 1.5, reducedMotion: boolean }
  | { kind: 'state', state: State, activities: readonly Activity[], label: string, sequence: number }
  | { kind: 'conversation-config', previewEnabled: boolean, previewMaxChars: number, defaultSessionId: string | null }
  | { kind: 'input-status', requestId: number, status: InputStatus }
  | { kind: 'reply-preview', requestId: number, text: string, completed: boolean }
  | { kind: 'clear-preview', requestId: number, reason: ClearPreviewReason }
  | { kind: 'reply', requestId: number, text: string, completed: boolean }
  | { kind: 'conversation-history', requestId: number, available: boolean, messages: readonly HistoryMessage[] }

const maxLineLength = 65_536
const maxTextLength = 2_000
const minPreviewMaxChars = 80
const helperLifecycleKinds = new Set<HelperLifecycleMessageKind>(['ready', 'closed'])
const hostKinds = new Set<HostMessageKind>(['hello', 'config', 'state', 'shutdown', 'conversation-config', 'input-status', 'reply-preview', 'clear-preview', 'reply', 'conversation-history'])
const scales = new Set([0.75, 1, 1.25, 1.5])
const canonicalActivities: readonly Activity[] = ['thinking', 'working']
const inputStatuses = new Set<InputStatus>(['queued', 'sent', 'no-default-session', 'session-unavailable', 'rejected'])
const clearPreviewReasons = new Set<ClearPreviewReason>(['disabled', 'next-input', 'cancelled', 'closed', 'session-unavailable'])

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
  if (message.kind === 'ready' || message.kind === 'closed') {
    assertExactKeys(message, ['version', 'kind'], 'helper message')
    return { version: PROTOCOL_VERSION, kind: message.kind }
  }
  if (message.kind === 'request-history') {
    assertExactKeys(message, ['version', 'kind', 'requestId'], 'helper message')
    assertPositiveSafeInteger(message.requestId, 'helper message requestId')
    return { version: PROTOCOL_VERSION, kind: 'request-history', requestId: message.requestId }
  }

  assertExactKeys(message, ['version', 'kind', 'requestId', 'text'], 'helper message')
  assertPositiveSafeInteger(message.requestId, 'helper message requestId')
  assertInputText(message.text, 'helper message text')
  return { version: PROTOCOL_VERSION, kind: 'input', requestId: message.requestId, text: message.text }
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
      assertExactKeys(value, ['version', 'kind'], 'host message', ['kind'])
      return { kind: value.kind }
    case 'config':
      assertExactKeys(value, ['version', 'kind', 'scale', 'reducedMotion'], 'host message', ['kind', 'scale', 'reducedMotion'])
      if (typeof value.scale !== 'number' || !scales.has(value.scale) || typeof value.reducedMotion !== 'boolean') {
        throw new Error('host message has an invalid config')
      }
      return { kind: 'config', scale: value.scale as 0.75 | 1 | 1.25 | 1.5, reducedMotion: value.reducedMotion }
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
      assertExactKeys(value, ['version', 'kind', 'previewEnabled', 'previewMaxChars', 'defaultSessionId'], 'host message', ['kind', 'previewEnabled', 'previewMaxChars', 'defaultSessionId'])
      if (typeof value.previewEnabled !== 'boolean') throw new Error('host message has an invalid previewEnabled')
      if (!isPreviewMaxChars(value.previewMaxChars)) throw new Error('host message has an invalid previewMaxChars')
      if (value.defaultSessionId !== null && (typeof value.defaultSessionId !== 'string' || value.defaultSessionId.length === 0)) {
        throw new Error('host message has an invalid defaultSessionId')
      }
      return { kind: 'conversation-config', previewEnabled: value.previewEnabled, previewMaxChars: value.previewMaxChars, defaultSessionId: value.defaultSessionId as string | null }
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
      assertPreviewText(value.text, 'host message text')
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
    default:
      throw new Error('host message has an unknown kind')
  }
}

function isHelperMessageKind(value: string): value is HelperMessageKind {
  return value === 'input' || value === 'request-history' || helperLifecycleKinds.has(value as HelperLifecycleMessageKind)
}

function isHistoryMessages(value: unknown): value is readonly HistoryMessage[] {
  if (!Array.isArray(value) || value.length > HISTORY_LIMIT) return false
  return value.every((entry) => {
    if (entry === null || typeof entry !== 'object' || Array.isArray(entry)) return false
    const record = entry as Record<string, unknown>
    return (record.role === 'user' || record.role === 'assistant')
      && typeof record.text === 'string'
      && record.text.length > 0
      && record.text.length <= HISTORY_MESSAGE_MAX_CHARS
  })
}

function assertPositiveSafeInteger(value: unknown, subject: string): asserts value is number {
  if (typeof value !== 'number' || !Number.isSafeInteger(value) || value <= 0) {
    throw new Error(`${subject} must be a positive safe integer`)
  }
}

function assertInputText(value: unknown, subject: string): asserts value is string {
  if (typeof value !== 'string' || value.length === 0 || value.length > maxTextLength || value !== value.trim()) {
    throw new Error(`${subject} must be trimmed and between 1 and ${maxTextLength} characters`)
  }
}

function assertPreviewText(value: unknown, subject: string): asserts value is string {
  if (typeof value !== 'string' || value.length === 0 || value.length > maxTextLength) {
    throw new Error(`${subject} must be between 1 and ${maxTextLength} characters`)
  }
}

function isPreviewMaxChars(value: unknown): value is number {
  return typeof value === 'number'
    && Number.isSafeInteger(value)
    && value >= minPreviewMaxChars
    && value <= maxTextLength
}

function isCanonicalActivities(state: State, value: unknown): value is readonly Activity[] {
  if (!Array.isArray(value) || !value.every((activity): activity is Activity => typeof activity === 'string' && Object.hasOwn(activityLabels, activity))) {
    return false
  }

  if (state !== 'active') return value.length === 0

  const expected = canonicalActivities.filter((activity) => value.includes(activity))
  return value.length === expected.length && value.every((activity, index) => activity === expected[index])
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
