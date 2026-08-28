import type { ReducibleState, SessionFact } from './companion-reducer.js'

const questionToolNames = new Set(['ask_user_question'])

const eventKinds: Readonly<Record<string, ReducibleState>> = {
  'turn/start': 'thinking',
  'step/start': 'thinking',
  'step/end': 'thinking',
  'tool/call': 'work-start',
  'tool/code-dispatch-start': 'work-start',
  'tool/result': 'work-finish',
  'tool/code-dispatch': 'work-finish',
  'approval/asked': 'waiting',
  'approval/decided': 'thinking',
}

export function adaptSessionEvent(session: unknown, event: unknown): SessionFact | undefined {
  const sessionId = readNonEmptyString(session, 'id')
  const seq = readNonNegativeSafeInteger(event, 'seq')
  const type = readString(event, 'type')
  if (sessionId === undefined || seq === undefined || type === undefined) return undefined

  const kind = type === 'turn/end'
    ? mapTurnEnd(event)
    : type === 'assistant/chunk' || type === 'assistant/message'
      ? mapAssistantActivity(type, event)
    : type === 'tool/call' && questionToolNames.has(readString(readRecord(event, 'data'), 'name') ?? '')
      ? 'question'
      : eventKinds[type]
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

function mapAssistantActivity(type: 'assistant/chunk' | 'assistant/message', event: unknown): ReducibleState {
  const data = readRecord(event, 'data')
  if (type === 'assistant/chunk') {
    return readString(readRecord(data, 'chunk'), 'type') === 'text-delta' ? 'responding' : 'thinking'
  }

  const content = readArray(readRecord(data, 'message'), 'content')
  return content?.some((block) => readString(block, 'type') === 'text') ? 'responding' : 'thinking'
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

function readArray(value: unknown, key: string): readonly unknown[] | undefined {
  if (value === null || typeof value !== 'object' || Array.isArray(value)) return undefined
  const candidate = (value as Record<string, unknown>)[key]
  return Array.isArray(candidate) ? candidate : undefined
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
