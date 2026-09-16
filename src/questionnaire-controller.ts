import type { HostOutboundMessage, HelperQuestionAnswerMessage } from './protocol.js'
import { answersMatch, boundedQuestionText, type QuestionAnswer, type QuestionView, validateQuestionAnswers, validateQuestions } from './questionnaire-protocol.js'

export type DshQuestionRequest = { agent?: { session?: { id?: unknown } }, questions: unknown, signal?: AbortSignal }
export type DshQuestionAnswer = { answers: readonly QuestionAnswer[] }
type Outcome = 'answered' | 'cancelled' | 'unavailable'
type Pending = {
  requestId: number, sessionId: string, mirror: boolean,
  questions: QuestionView[], nativeIds: readonly string[],
  resolve?: (answer: DshQuestionAnswer) => void, reject?: (reason: unknown) => void,
  signal?: AbortSignal, onAbort?: () => void, timer?: NodeJS.Timeout,
}

/** One transient desktop presentation. Web mirrors never acquire decision authority. */
export class QuestionnaireController {
  private pending?: Pending
  private nextRequestId = 0
  public constructor(
    private readonly isSelectedSession: (id: string) => boolean,
    private readonly isHelperAvailable: () => boolean,
    private readonly send: (message: HostOutboundMessage) => void,
    private readonly mode: () => 'web' | 'pet' | 'both' = () => 'pet',
    private readonly timeoutMs = 600_000,
  ) {}

  public request(request: DshQuestionRequest, next: () => Promise<DshQuestionAnswer>): Promise<DshQuestionAnswer> {
    if (request.signal?.aborted) return Promise.reject(questionError('ASK_ABORTED'))
    const sessionId = request.agent?.session?.id
    const mode = this.mode()
    if (mode === 'web' || !boundedQuestionText(sessionId, 200) || !this.isSelectedSession(sessionId)
      || this.pending || !this.isHelperAvailable()) return next()
    const projected = projectQuestions(request.questions)
    if (!projected) return next()
    const requestId = ++this.nextRequestId
    const pending: Pending = { requestId, sessionId, mirror: mode === 'both', ...projected, signal: request.signal }
    this.pending = pending

    if (pending.mirror) {
      this.start(pending)
      // Always retain the built-in Web waterfall and its original error semantics.
      try {
        return Promise.resolve(next()).then(answer => {
          this.finish(requestId, 'answered')
          return answer
        }, error => {
          this.finish(requestId, 'cancelled')
          throw error
        })
      } catch (error) {
        this.finish(requestId, 'cancelled')
        return Promise.reject(error)
      }
    }
    return new Promise((resolve, reject) => {
      pending.resolve = resolve
      pending.reject = reject
      this.start(pending)
    })
  }

  private start(pending: Pending): void {
    pending.onAbort = () => this.finish(pending.requestId, 'cancelled', undefined, 'ASK_ABORTED')
    pending.signal?.addEventListener('abort', pending.onAbort, { once: true })
    if (pending.signal?.aborted) { pending.onAbort(); return }
    pending.timer = setTimeout(() => this.finish(pending.requestId, 'cancelled'), this.timeoutMs)
    pending.timer.unref()
    if (!this.publish({ kind: 'question-request', requestId: pending.requestId, answerable: !pending.mirror, questions: pending.questions })) {
      this.finish(pending.requestId, 'unavailable')
    }
  }

  public answer(message: HelperQuestionAnswerMessage): void {
    const pending = this.pending
    if (!pending || pending.mirror || pending.requestId !== message.requestId) return
    if (!this.isSelectedSession(pending.sessionId)) { this.selectionChanged(); return }
    let answers: QuestionAnswer[]
    try { answers = validateQuestionAnswers(message.answers) } catch { return }
    if (!answersMatch(pending.questions, answers)) return
    this.finish(message.requestId, 'answered', {
      answers: pending.questions.map((q, i) => ({ ...answers.find(a => a.id === q.id)!, id: pending.nativeIds[i] })),
    })
  }

  public cancel(requestId: number): void { this.finish(requestId, 'cancelled') }
  public selectionChanged(): void {
    if (this.pending && !this.isSelectedSession(this.pending.sessionId)) this.finish(this.pending.requestId, 'cancelled')
  }
  public sessionUnavailable(sessionId: string): void {
    if (this.pending?.sessionId === sessionId) this.finish(this.pending.requestId, 'unavailable')
  }
  public helperUnavailable(): void { if (this.pending) this.finish(this.pending.requestId, 'unavailable') }
  public dialogueClosed(): void { if (this.pending) this.finish(this.pending.requestId, 'cancelled') }

  private finish(requestId: number, outcome: Outcome, answer?: DshQuestionAnswer, code = 'ASK_CANCELLED'): void {
    const pending = this.pending
    if (!pending || pending.requestId !== requestId) return
    this.pending = undefined
    if (pending.timer) clearTimeout(pending.timer)
    if (pending.onAbort) pending.signal?.removeEventListener('abort', pending.onAbort)
    this.publish({ kind: 'question-resolved', requestId, outcome })
    if (pending.mirror) return
    if (answer) pending.resolve?.(answer)
    else pending.reject?.(questionError(outcome === 'unavailable' ? 'NO_PROVIDER' : code))
  }
  private publish(message: HostOutboundMessage): boolean {
    try { this.send(message); return true } catch { return false }
  }
}

function questionError(code: string): Error {
  return Object.assign(new Error('User question ended without an answer'), { name: 'UserQuestionError', code })
}
function isRecord(value: unknown): value is Record<string, unknown> {
  return value !== null && typeof value === 'object' && !Array.isArray(value)
}
function projectQuestions(value: unknown): { questions: QuestionView[], nativeIds: string[] } | undefined {
  if (!Array.isArray(value) || value.length < 1 || value.length > 8) return
  const nativeIds: string[] = []
  const questions: QuestionView[] = []
  for (const q of value) {
    if (!isRecord(q) || !boundedQuestionText(q.id, 100) || nativeIds.includes(q.id) || !boundedQuestionText(q.question, 2000)
      || q.detail !== undefined || q.intent !== undefined || (q.multiSelect !== undefined && typeof q.multiSelect !== 'boolean')
      || (q.options !== undefined && !Array.isArray(q.options))) return
    const options: string[] = []
    for (const option of (q.options ?? []) as unknown[]) {
      if (!isRecord(option) || !boundedQuestionText(option.label, 200) || option.description !== undefined) return
      options.push(option.label)
    }
    nativeIds.push(q.id)
    questions.push({ id: `q${questions.length + 1}`, question: q.question, options, multiSelect: q.multiSelect === true })
  }
  try { return { questions: validateQuestions(questions), nativeIds } } catch { return }
}
