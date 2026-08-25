export const PROTOCOL_VERSION = 1 as const

export type HelperMessageKind = 'ready' | 'closed'
export type HostMessageKind = 'hello' | 'config' | 'state' | 'shutdown'

export type HelperMessage = {
  version: typeof PROTOCOL_VERSION
  kind: HelperMessageKind
}

export type HostMessage = {
  version: typeof PROTOCOL_VERSION
  kind: HostMessageKind
}

const helperKinds = new Set<HelperMessageKind>(['ready', 'closed'])

export function parseHelperMessage(line: string): HelperMessage {
  let value: unknown

  try {
    value = JSON.parse(line)
  } catch {
    throw new Error('helper message must be valid JSON')
  }

  if (value === null || typeof value !== 'object' || Array.isArray(value)) {
    throw new Error('helper message must be an object')
  }

  const message = value as Record<string, unknown>
  if (message.version !== PROTOCOL_VERSION) {
    throw new Error('helper message has an unsupported version')
  }

  if (typeof message.kind !== 'string' || !helperKinds.has(message.kind as HelperMessageKind)) {
    throw new Error('helper message has an unknown kind')
  }

  return { version: PROTOCOL_VERSION, kind: message.kind as HelperMessageKind }
}

export function encodeHostMessage(kind: HostMessageKind): string {
  const message: HostMessage = { version: PROTOCOL_VERSION, kind }
  return `${JSON.stringify(message)}\n`
}
