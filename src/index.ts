import { HelperProcess } from './helper-process.js'

export const name = 'dsh-png-pet'

type PluginContext = {
  effect(factory: () => () => void): void
}

export function apply(ctx: PluginContext): void {
  const helper = new HelperProcess()
  void helper.start().then(() => {
    helper.send('hello')
    helper.send('config')
    helper.send('state')
  }).catch(() => {
    console.error('dsh-png-pet helper startup failed')
  })

  ctx.effect(() => () => {
    void helper.stop()
  })
}
