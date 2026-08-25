import { CompanionBridge, createSessionObservers } from './companion-bridge.js'
import { HelperProcess } from './helper-process.js'

export const name = 'dsh-png-pet'

export type SessionObserverContext = {
  on(name: 'session/event', listener: (session: unknown, event: unknown) => void): unknown
  on(name: 'session/disposed', listener: (session: unknown) => void): unknown
}

type PluginContext = SessionObserverContext & {
  effect(factory: () => () => void): void
}

export function apply(ctx: PluginContext): void {
  const helper = new HelperProcess()
  const bridge = new CompanionBridge((message) => helper.send(message))
  registerSessionObservers(ctx, bridge)

  void helper.start().then(() => {
    helper.send({ kind: 'hello' })
    helper.send({ kind: 'config', scale: 1, reducedMotion: false })
    bridge.publishCurrent()
  }).catch(() => {
    console.error('dsh-png-pet helper startup failed')
  })

  ctx.effect(() => () => {
    void helper.stop()
  })
}

export function registerSessionObservers(ctx: SessionObserverContext, bridge: CompanionBridge): void {
  const observers = createSessionObservers(bridge)

  ctx.on('session/event', (session, event) => {
    try {
      observers.sessionEvent(session, event)
    } catch {
      console.error('dsh-png-pet session event ignored')
    }
  })
  ctx.on('session/disposed', (session) => {
    try {
      observers.sessionDisposed(session)
    } catch {
      console.error('dsh-png-pet session disposal ignored')
    }
  })
}
