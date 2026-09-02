import type { HelperApprovalAnswerMessage, HostOutboundMessage } from './protocol.js'

export type ApprovalOutcome = 'allowed-once' | 'rejected' | 'cancelled' | 'unavailable'

/** The public approval/request shape deliberately excludes all tool arguments. */
export type DshApprovalRequest = {
  agent: { session?: { id?: unknown } }
  signal?: AbortSignal
}

type PendingApproval = {
  resolve: (outcome: ApprovalOutcome) => void
  onAbort?: () => void
  signal?: AbortSignal
}

/**
 * Owns the Helper side of an approval exactly while it is answerable in the
 * selected DSH session. It never derives an allow decision: the Helper can
 * submit only the two closed, one-shot choices and every lost channel fails
 * closed.
 */
export class ApprovalController {
  private readonly pending = new Map<number, PendingApproval>()
  private nextRequestId = 0

  public constructor(
    private readonly isSelectedSession: (sessionId: string) => boolean,
    private readonly isHelperAvailable: () => boolean,
    private readonly send: (message: HostOutboundMessage) => void,
    private readonly isPetApprovalEnabled: () => boolean = () => true,
  ) {}

  public request(request: DshApprovalRequest, next: () => Promise<ApprovalOutcome>): Promise<ApprovalOutcome> {
    const sessionId = readSessionId(request)
    if (this.pending.size !== 0 || !this.isPetApprovalEnabled() || sessionId === undefined || !this.isSelectedSession(sessionId)) return next()
    if (!this.isHelperAvailable()) return Promise.resolve('unavailable')
    if (request.signal?.aborted) return Promise.resolve('cancelled')

    const requestId = this.nextRequestId + 1
    this.nextRequestId = requestId
    return new Promise<ApprovalOutcome>((resolve) => {
      const pending: PendingApproval = { resolve, signal: request.signal }
      if (request.signal !== undefined) {
        pending.onAbort = () => this.settle(requestId, 'cancelled')
        request.signal.addEventListener('abort', pending.onAbort, { once: true })
      }
      this.pending.set(requestId, pending)
      // No tool arguments, paths, reasons, or DSH IDs cross the Helper boundary.
      this.send({ kind: 'approval-request', requestId })
    })
  }

  public answer(message: HelperApprovalAnswerMessage): void {
    this.settle(message.requestId, message.outcome)
  }

  /** A closed/crashed Helper is an unavailable approval channel, never a grant. */
  public helperUnavailable(): void {
    for (const requestId of [...this.pending.keys()]) this.settle(requestId, 'unavailable')
  }

  private settle(requestId: number, outcome: ApprovalOutcome): void {
    const pending = this.pending.get(requestId)
    if (pending === undefined) return
    this.pending.delete(requestId)
    if (pending.signal !== undefined && pending.onAbort !== undefined) {
      pending.signal.removeEventListener('abort', pending.onAbort)
    }
    this.send({ kind: 'approval-resolved', requestId, outcome })
    pending.resolve(outcome)
  }
}

function readSessionId(request: DshApprovalRequest): string | undefined {
  const id = request.agent.session?.id
  return typeof id === 'string' && id.length > 0 && id.length <= 200 ? id : undefined
}
