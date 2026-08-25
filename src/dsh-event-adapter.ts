import type { ReducibleState, SessionFact } from './companion-reducer.js'

const eventKinds: Readonly<Record<string, ReducibleState>> = {
  'turn/start': 'thinking',
  'step/start': 'thinking',
  'assistant/chunk': 'thinking',
  'assistant/message': 'thinking',
  'step/end': 'thinking',
  'tool/call': 'working',
  'tool/code-dispatch-start': 'working',
  'tool/result': 'thinking',
  'tool/code-dispatch': 'thinking',
  'approval/asked': 'waiting',
  'approval/decided': 'thinking',
}

export function adaptSessionEvent(session: unknown, event: unknown): SessionFact | undefined {
  const sessionId = readNonEmptyString(session, 'id')
  const seq = readNonNegativeSafeInteger(event, 'seq')
  const type = readString(event, 'type')
  if (sessionId === undefined || seq === undefined || type === undefined) return undefined

  const kind = type === 'turn/end' ? mapTurnEnd(event) : eventKinds[type]
  if (kind === undefined) return undefined

  return {
    sessionId,
    seq,
    isSubagent: (readNonNegativeSafeInteger(readRecord(session, 'header'), 'delegationDepth') ?? 0) > 0,
    kind,
  }
}

export function adaptSessionDisposed(session: unknown): string | undefined {
  return readNonEmptyString(session, 'id')
}

function mapTurnEnd(event: unknown): ReducibleState | undefined {
  const kind = readString(readRecord(readRecord(event, 'data'), 'reason'), 'kind')
  if (kind === undefined) return undefined
  if (kind === 'completed') return 'success'
  if (kind === 'error' || kind === 'max-tokens') return 'error'
  return 'idle'
}

function readRecord(value: unknown, key: string): Record<string, unknown> | undefined {
  if (value === null || typeof value !== 'object' || Array.isArray(value)) return undefined

  const candidate = (value as Record<string, unknown>)[key]
  if (candidate === null || typeof candidate !== 'object' || Array.isArray(candidate)) return undefined
  return candidate as Record<string, unknown>
}

function readString(value: unknown, key: string): string | undefined {
  if (value === null || typeof value !== 'object' || Array.isArray(value)) return undefined
  const candidate = (value as Record<string, unknown>)[key]
  return typeof candidate === 'string' ? candidate : undefined
}

function readNonEmptyString(value: unknown, key: string): string | undefined {
  const candidate = readString(value, key)
  return candidate !== undefined && candidate.length > 0 ? candidate : undefined
}

function readNonNegativeSafeInteger(value: unknown, key: string): number | undefined {
  if (value === null || typeof value !== 'object' || Array.isArray(value)) return undefined
  const candidate = (value as Record<string, unknown>)[key]
  return typeof candidate === 'number' && Number.isSafeInteger(candidate) && candidate >= 0
    ? candidate
    : undefined
}
