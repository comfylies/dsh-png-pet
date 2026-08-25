import { CompanionReducer, type CompanionReducerOptions, type Presentation, type SessionFact } from './companion-reducer.js'
import { adaptSessionDisposed, adaptSessionEvent } from './dsh-event-adapter.js'
import { labelForPresentation, type HostOutboundMessage } from './protocol.js'

type BridgeClock = {
  setTimeout(callback: () => void, delayMs: number): unknown
  clearTimeout(timer: unknown): void
}

export type CompanionBridgeOptions = CompanionReducerOptions & {
  clock?: BridgeClock
}

export type SessionObservers = {
  sessionEvent(session: unknown, event: unknown): void
  sessionDisposed(session: unknown): void
}

const defaultClock: BridgeClock = {
  setTimeout,
  clearTimeout,
}

export class CompanionBridge {
  private readonly reducer: CompanionReducer
  private readonly clock: BridgeClock
  private timer?: unknown
  private lastPresentation?: Presentation

  public constructor(
    private readonly send: (message: HostOutboundMessage) => void,
    options: CompanionBridgeOptions = {},
  ) {
    this.reducer = new CompanionReducer(options)
    this.clock = options.clock ?? defaultClock
  }

  public apply(fact: SessionFact): void {
    this.publish(this.reducer.apply(fact))
  }

  public dispose(sessionId: string): void {
    this.publish(this.reducer.dispose(sessionId))
  }

  public publishCurrent(): void {
    this.publish(this.reducer.current(), true)
  }

  private publish(presentation: Presentation, force = false): void {
    if (this.lastPresentation?.terminal && !samePresentation(presentation, this.lastPresentation)) {
      this.reducer.disposeTerminal(this.lastPresentation.sequence)
      presentation = this.reducer.current()
    }
    if (!force && samePresentation(presentation, this.lastPresentation)) return

    this.clearTerminalTimer()
    this.lastPresentation = presentation
    this.send({
      kind: 'state',
      state: presentation.state,
      activities: [...presentation.activities],
      label: labelForPresentation(presentation.state, presentation.activities),
      sequence: presentation.sequence,
    })
    if (presentation.terminal) this.scheduleIdle(presentation.sequence)
  }

  private scheduleIdle(sequence: number): void {
    this.timer = this.clock.setTimeout(() => {
      const current = this.reducer.current()
      if (!current.terminal || current.sequence !== sequence) return
      this.reducer.disposeTerminal(sequence)
      this.publish(this.reducer.current())
    }, 2_500)
  }

  private clearTerminalTimer(): void {
    if (this.timer === undefined) return
    this.clock.clearTimeout(this.timer)
    this.timer = undefined
  }
}

export function createSessionObservers(bridge: CompanionBridge): SessionObservers {
  return {
    sessionEvent(session, event) {
      const fact = adaptSessionEvent(session, event)
      if (fact !== undefined) bridge.apply(fact)
    },
    sessionDisposed(session) {
      const sessionId = adaptSessionDisposed(session)
      if (sessionId !== undefined) bridge.dispose(sessionId)
    },
  }
}

function samePresentation(first: Presentation, second: Presentation | undefined): boolean {
  return second !== undefined
    && first.state === second.state
    && first.sequence === second.sequence
    && first.terminal === second.terminal
    && first.activities.length === second.activities.length
    && first.activities.every((activity, index) => activity === second.activities[index])
}
