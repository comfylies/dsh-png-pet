export const PROTOCOL_VERSION = 2 as const

export const displayLabels = {
  idle: '',
  thinking: '思考中…',
  working: '工作中…',
  waiting: '等待你的操作',
  success: '已完成',
  error: '发生错误',
  disconnected: '未连接',
} as const

export type CompanionState = keyof typeof displayLabels
export type HelperMessageKind = 'ready' | 'closed'
export type HostMessageKind = 'hello' | 'config' | 'state' | 'shutdown'

export type HelperMessage = {
  version: typeof PROTOCOL_VERSION
  kind: HelperMessageKind
}

export type HostMessage =
  | { version: typeof PROTOCOL_VERSION, kind: 'hello' | 'shutdown' }
  | { version: typeof PROTOCOL_VERSION, kind: 'config', scale: 0.75 | 1 | 1.25 | 1.5, reducedMotion: boolean }
  | { version: typeof PROTOCOL_VERSION, kind: 'state', state: CompanionState, label: string, sequence: number }

export type HostOutboundMessage =
  | { kind: 'hello' | 'shutdown' }
  | { kind: 'config', scale: 0.75 | 1 | 1.25 | 1.5, reducedMotion: boolean }
  | { kind: 'state', state: CompanionState, label: string, sequence: number }

const maxLineLength = 512
const helperKinds = new Set<HelperMessageKind>(['ready', 'closed'])
const hostKinds = new Set<HostMessageKind>(['hello', 'config', 'state', 'shutdown'])
const scales = new Set([0.75, 1, 1.25, 1.5])

export function parseHelperMessage(line: string): HelperMessage {
  const message = parseObject(line, 'helper message')
  assertProtocolVersion(message, 'helper message')

  if (typeof message.kind !== 'string' || !helperKinds.has(message.kind as HelperMessageKind)) {
    throw new Error('helper message has an unknown kind')
  }
  assertExactKeys(message, ['version', 'kind'], 'helper message')

  return { version: PROTOCOL_VERSION, kind: message.kind as HelperMessageKind }
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
      assertExactKeys(value, ['version', 'kind', 'state', 'label', 'sequence'], 'host message', ['kind', 'state', 'label', 'sequence'])
      if (typeof value.state !== 'string' || !Object.hasOwn(displayLabels, value.state)) {
        throw new Error('host message has an unknown state')
      }
      if (typeof value.label !== 'string' || value.label !== displayLabels[value.state as CompanionState]) {
        throw new Error('host message has an invalid label')
      }
      const sequence = value.sequence
      if (typeof sequence !== 'number' || !Number.isSafeInteger(sequence) || sequence < 0) {
        throw new Error('host message has an invalid sequence')
      }
      return {
        kind: 'state',
        state: value.state as CompanionState,
        label: value.label,
        sequence,
      }
    default:
      throw new Error('host message has an unknown kind')
  }
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
  if (requiredKeys.some((key) => !(key in message))) {
    throw new Error(`${subject} is missing required fields`)
  }
}
