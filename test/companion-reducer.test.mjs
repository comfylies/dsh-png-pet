import assert from 'node:assert/strict'
import test from 'node:test'

import { CompanionReducer } from '../lib/companion-reducer.js'

test('maps every supported state to a presentation', () => {
  const cases = [
    ['idle', false],
    ['thinking', false],
    ['working', false],
    ['waiting', false],
    ['success', true],
    ['error', true],
  ]

  for (const [kind, terminal] of cases) {
    const reducer = new CompanionReducer()
    assert.deepEqual(
      reducer.apply({ sessionId: kind, seq: 1, isSubagent: false, kind }),
      { state: kind, sequence: 1, terminal },
    )
  }
})

test('waiting takes priority over a working top-level session', () => {
  const reducer = new CompanionReducer()
  reducer.apply({ sessionId: 'first', seq: 1, isSubagent: false, kind: 'working' })

  assert.deepEqual(
    reducer.apply({ sessionId: 'second', seq: 1, isSubagent: false, kind: 'waiting' }),
    { state: 'waiting', sequence: 1, terminal: false },
  )
})

test('breaks an equal-priority tie with the latest sequence', () => {
  const reducer = new CompanionReducer()
  reducer.apply({ sessionId: 'first', seq: 3, isSubagent: false, kind: 'thinking' })

  assert.deepEqual(
    reducer.apply({ sessionId: 'second', seq: 4, isSubagent: false, kind: 'thinking' }),
    { state: 'thinking', sequence: 4, terminal: false },
  )
})

test('ignores duplicate facts and subagents by default', () => {
  const reducer = new CompanionReducer()
  reducer.apply({ sessionId: 'root', seq: 4, isSubagent: false, kind: 'thinking' })
  reducer.apply({ sessionId: 'root', seq: 4, isSubagent: false, kind: 'error' })

  assert.deepEqual(
    reducer.apply({ sessionId: 'child', seq: 9, isSubagent: true, kind: 'waiting' }),
    { state: 'thinking', sequence: 4, terminal: false },
  )
})

test('includes subagents only when enabled', () => {
  const reducer = new CompanionReducer({ includeSubagents: true })

  assert.deepEqual(
    reducer.apply({ sessionId: 'child', seq: 9, isSubagent: true, kind: 'waiting' }),
    { state: 'waiting', sequence: 9, terminal: false },
  )
})

test('removes a disposed session without accepting a later fact', () => {
  const reducer = new CompanionReducer({ includeSubagents: true })
  reducer.apply({ sessionId: 's', seq: 1, isSubagent: false, kind: 'working' })
  reducer.dispose('s')

  assert.deepEqual(
    reducer.apply({ sessionId: 's', seq: 2, isSubagent: false, kind: 'error' }),
    { state: 'idle', sequence: 0, terminal: false },
  )
})

test('ignores malformed facts', () => {
  const reducer = new CompanionReducer()

  assert.deepEqual(
    reducer.apply({ sessionId: '', seq: -1, isSubagent: 'no', kind: 'unknown' }),
    { state: 'idle', sequence: 0, terminal: false },
  )
})
