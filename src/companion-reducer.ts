export type Activity = 'thinking' | 'working' | 'responding'
export type ReducibleState = 'thinking' | 'responding' | 'work-start' | 'work-finish' | 'waiting' | 'question' | 'success' | 'error' | 'idle'
export type PresentationState = 'active' | 'idle' | 'waiting' | 'question' | 'success' | 'error'

export type SessionFact = { sessionId: string; seq: number; isSubagent: boolean; kind: ReducibleState }
export type Presentation = { state: PresentationState; activities: readonly Activity[]; sequence: number; terminal: boolean }
export type CompanionReducerOptions = { includeSubagents?: boolean }

type ExclusiveState = 'idle' | 'waiting' | 'question' | 'success' | 'error'
type SessionRecord = { seq: number; thinking: boolean; responding: boolean; workingCount: number; exclusive?: ExclusiveState }

export class CompanionReducer {
  private readonly sessions = new Map<string, SessionRecord>()
  private readonly retired = new Set<string>()
  public constructor(private readonly options: CompanionReducerOptions = {}) {}

  public apply(fact: unknown): Presentation {
    if (!isFact(fact) || this.retired.has(fact.sessionId)) return this.current()
    if (fact.isSubagent && !this.options.includeSubagents) return this.current()
    const previous = this.sessions.get(fact.sessionId)
    if (previous !== undefined && fact.seq <= previous.seq) return this.current()
    if ((previous?.exclusive === 'success' || previous?.exclusive === 'error') && fact.kind !== 'success' && fact.kind !== 'error') return this.current()
    const session: SessionRecord = previous === undefined ? { seq: fact.seq, thinking: false, responding: false, workingCount: 0 } : { ...previous, seq: fact.seq }
    switch (fact.kind) {
      case 'thinking': session.thinking = true; session.exclusive = undefined; break
      case 'responding': session.thinking = false; session.responding = true; session.exclusive = undefined; break
      case 'work-start': session.responding = false; session.workingCount += 1; session.exclusive = undefined; break
      case 'work-finish':
        if (session.exclusive === 'question') {
          session.thinking = true
          session.exclusive = undefined
        } else {
          session.workingCount = Math.max(0, session.workingCount - 1)
        }
        break
      case 'waiting': session.responding = false; session.exclusive = 'waiting'; break
      case 'question': session.thinking = false; session.responding = false; session.workingCount = 0; session.exclusive = 'question'; break
      case 'success':
      case 'error': session.thinking = false; session.responding = false; session.workingCount = 0; session.exclusive = fact.kind; break
      case 'idle': session.thinking = false; session.responding = false; session.workingCount = 0; session.exclusive = 'idle'; break
    }
    this.sessions.set(fact.sessionId, session)
    return this.current()
  }

  public dispose(sessionId: string): Presentation {
    if (sessionId.length === 0) return this.current()
    this.sessions.delete(sessionId); this.retired.add(sessionId); return this.current()
  }

  public disposeTerminal(sequence: number): Presentation {
    for (const [sessionId, session] of this.sessions) {
      if (session.seq === sequence && (session.exclusive === 'success' || session.exclusive === 'error')) this.sessions.delete(sessionId)
    }
    return this.current()
  }

  public current(): Presentation {
    let winner: { presentation: Presentation; rank: number } | undefined
    for (const session of this.sessions.values()) {
      const presentation = present(session)
      const rank = presentation.state === 'active'
        ? (presentation.activities.includes('working') ? 4 : presentation.activities.includes('responding') ? 3 : 2)
        : ({ idle: 0, success: 1, error: 5, question: 6, waiting: 7 } as const)[presentation.state]
      if (winner === undefined || rank > winner.rank || (rank === winner.rank && presentation.sequence > winner.presentation.sequence)) winner = { presentation, rank }
    }
    return winner?.presentation ?? { state: 'idle', activities: [], sequence: 0, terminal: false }
  }
}

function present(session: SessionRecord): Presentation {
  if (session.exclusive !== undefined) return { state: session.exclusive, activities: [], sequence: session.seq, terminal: session.exclusive === 'success' || session.exclusive === 'error' }
  if (session.responding) return { state: 'active', activities: ['responding'], sequence: session.seq, terminal: false }
  const activities: Activity[] = []
  if (session.thinking) activities.push('thinking')
  if (session.workingCount > 0) activities.push('working')
  return { state: activities.length === 0 ? 'idle' : 'active', activities, sequence: session.seq, terminal: false }
}

function isFact(value: unknown): value is SessionFact {
  if (value === null || typeof value !== 'object' || Array.isArray(value)) return false
  const fact = value as Record<string, unknown>
  return typeof fact.sessionId === 'string' && fact.sessionId.length > 0 && Number.isSafeInteger(fact.seq) && (fact.seq as number) >= 0 && typeof fact.isSubagent === 'boolean' && typeof fact.kind === 'string' && ['thinking', 'responding', 'work-start', 'work-finish', 'waiting', 'question', 'success', 'error', 'idle'].includes(fact.kind)
}
