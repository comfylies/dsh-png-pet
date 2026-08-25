import assert from 'node:assert/strict'
import test from 'node:test'

import { CompanionBridge, createSessionObservers } from '../lib/companion-bridge.js'

function createClock() {
  const timers = []
  return {
    timers,
    clock: {
      setTimeout(callback) {
        const timer = { callback, cleared: false }
        timers.push(timer)
        return timer
      },
      clearTimeout(timer) {
        timer.cleared = true
      },
    },
  }
}

test('sends a fixed success state then returns to idle after its terminal timer', () => {
  const sent = []
  const { clock, timers } = createClock()
  const bridge = new CompanionBridge((message) => sent.push(message), { clock })

  bridge.apply({ sessionId: 's', seq: 1, isSubagent: false, kind: 'success' })
  timers[0].callback()

  assert.deepEqual(sent, [
    { kind: 'state', state: 'success', label: '已完成', sequence: 1 },
    { kind: 'state', state: 'idle', label: '', sequence: 0 },
  ])
})

test('does not let a stale terminal timer overwrite a newer working state', () => {
  const sent = []
  const { clock, timers } = createClock()
  const bridge = new CompanionBridge((message) => sent.push(message), { clock })

  bridge.apply({ sessionId: 's', seq: 1, isSubagent: false, kind: 'success' })
  bridge.apply({ sessionId: 's', seq: 2, isSubagent: false, kind: 'working' })
  timers[0].callback()

  assert.deepEqual(sent.at(-1), { kind: 'state', state: 'working', label: '工作中…', sequence: 2 })
})

test('adapts events through observers without forwarding their data', () => {
  const sent = []
  const bridge = new CompanionBridge((message) => sent.push(message))
  const observers = createSessionObservers(bridge)

  assert.doesNotThrow(() => observers.sessionEvent(
    { id: 'root', header: {} },
    { type: 'tool/call', seq: 1, data: { arguments: { ignored: true } } },
  ))
  observers.sessionDisposed({ id: 'root' })

  assert.deepEqual(sent, [
    { kind: 'state', state: 'working', label: '工作中…', sequence: 1 },
    { kind: 'state', state: 'idle', label: '', sequence: 0 },
  ])
  assert.equal(JSON.stringify(sent).includes('ignored'), false)
})
