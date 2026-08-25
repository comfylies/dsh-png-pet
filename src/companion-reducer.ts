import type { CompanionState } from './protocol.js'

export type ReducibleState = Exclude<CompanionState, 'disconnected'>

export type SessionFact = {
  sessionId: string
  seq: number
  isSubagent: boolean
  kind: ReducibleState
}

export type Presentation = {
  state: ReducibleState
  sequence: number
  terminal: boolean
}

export type CompanionReducerOptions = {
  includeSubagents?: boolean
}

const priority: Record<ReducibleState, number> = {
  idle: 0,
  success: 1,
  thinking: 2,
  working: 3,
  error: 4,
  waiting: 5,
}

const reducibleStates = new Set<ReducibleState>([
  'idle',
  'thinking',
  'working',
  'waiting',
  'success',
  'error',
])

type SessionRecord = Pick<SessionFact, 'seq' | 'kind'>

export class CompanionReducer {
  private readonly sessions = new Map<string, SessionRecord>()
  private readonly retired = new Set<string>()

  public constructor(private readonly options: CompanionReducerOptions = {}) {}

  public apply(fact: unknown): Presentation {
    if (!isFact(fact) || this.retired.has(fact.sessionId)) return this.current()
    if (fact.isSubagent && !this.options.includeSubagents) return this.current()

    const previous = this.sessions.get(fact.sessionId)
    if (previous !== undefined && fact.seq <= previous.seq) return this.current()

    this.sessions.set(fact.sessionId, { seq: fact.seq, kind: fact.kind })
    return this.current()
  }

  public dispose(sessionId: string): Presentation {
    if (sessionId.length === 0) return this.current()

    this.sessions.delete(sessionId)
    this.retired.add(sessionId)
    return this.current()
  }

  public disposeTerminal(sequence: number): Presentation {
    for (const [sessionId, session] of this.sessions) {
      if (session.seq === sequence && (session.kind === 'success' || session.kind === 'error')) {
        this.sessions.delete(sessionId)
      }
    }
    return this.current()
  }

  public current(): Presentation {
    let winner: SessionRecord | undefined

    for (const session of this.sessions.values()) {
      if (
        winner === undefined
        || priority[session.kind] > priority[winner.kind]
        || (priority[session.kind] === priority[winner.kind] && session.seq > winner.seq)
      ) {
        winner = session
      }
    }

    if (winner === undefined) return { state: 'idle', sequence: 0, terminal: false }
    return {
      state: winner.kind,
      sequence: winner.seq,
      terminal: winner.kind === 'success' || winner.kind === 'error',
    }
  }
}

function isFact(value: unknown): value is SessionFact {
  if (value === null || typeof value !== 'object' || Array.isArray(value)) return false

  const fact = value as Record<string, unknown>
  return typeof fact.sessionId === 'string'
    && fact.sessionId.length > 0
    && Number.isSafeInteger(fact.seq)
    && (fact.seq as number) >= 0
    && typeof fact.isSubagent === 'boolean'
    && typeof fact.kind === 'string'
    && reducibleStates.has(fact.kind as ReducibleState)
}
