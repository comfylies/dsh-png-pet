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
    { kind: 'state', state: 'success', label: '已完成', activities: [], sequence: 1 },
    { kind: 'state', state: 'idle', label: '', activities: [], sequence: 0 },
  ])
})

test('does not let a stale terminal timer overwrite another session’s active activities', () => {
  const sent = []
  const { clock, timers } = createClock()
  const bridge = new CompanionBridge((message) => sent.push(message), { clock })

  bridge.apply({ sessionId: 'done', seq: 1, isSubagent: false, kind: 'success' })
  bridge.apply({ sessionId: 'active', seq: 2, isSubagent: false, kind: 'thinking' })
  bridge.apply({ sessionId: 'active', seq: 3, isSubagent: false, kind: 'work-start' })
  timers[0].callback()

  assert.deepEqual(sent.at(-1), { kind: 'state', state: 'active', label: '思考中/工作中', activities: ['thinking', 'working'], sequence: 3 })
})

test('publishes composite thinking and working activities with a fixed working label', () => {
  const sent = []
  const bridge = new CompanionBridge((message) => sent.push(message))

  bridge.apply({ sessionId: 's', seq: 1, isSubagent: false, kind: 'thinking' })
  bridge.apply({ sessionId: 's', seq: 2, isSubagent: false, kind: 'work-start' })

  assert.deepEqual(sent, [
    { kind: 'state', state: 'active', label: '思考中', activities: ['thinking'], sequence: 1 },
    { kind: 'state', state: 'active', label: '思考中/工作中', activities: ['thinking', 'working'], sequence: 2 },
  ])
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
    { kind: 'state', state: 'active', label: '工作中', activities: ['working'], sequence: 1 },
    { kind: 'state', state: 'idle', label: '', activities: [], sequence: 0 },
  ])
  assert.equal(JSON.stringify(sent).includes('ignored'), false)
})
