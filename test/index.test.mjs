import assert from 'node:assert/strict'
import test from 'node:test'

import { CompanionBridge } from '../lib/companion-bridge.js'
import { registerSessionObservers } from '../lib/index.js'

test('registers both DSH observers and contains malformed event input', () => {
  const listeners = new Map()
  const sent = []
  const context = {
    on(name, listener) {
      listeners.set(name, listener)
    },
  }
  const bridge = new CompanionBridge((message) => sent.push(message))

  registerSessionObservers(context, bridge)

  assert.equal(listeners.size, 2)
  assert.doesNotThrow(() => listeners.get('session/event')({ id: 'root', header: {} }, { type: 'unknown', seq: 1 }))
  listeners.get('session/event')({ id: 'root', header: {} }, { type: 'approval/asked', seq: 2 })
  listeners.get('session/disposed')({ id: 'root' })

  assert.deepEqual(sent, [
    { kind: 'state', state: 'waiting', label: '等待你的操作', sequence: 2 },
    { kind: 'state', state: 'idle', label: '', sequence: 0 },
  ])
})
