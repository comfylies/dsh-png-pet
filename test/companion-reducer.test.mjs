import assert from 'node:assert/strict'
import test from 'node:test'

import { CompanionReducer } from '../lib/companion-reducer.js'

test('maps every supported state to a presentation', () => {
  const cases = [
    ['idle', 'idle', [], false],
    ['thinking', 'active', ['thinking'], false],
    ['work-start', 'active', ['working'], false],
    ['responding', 'active', ['responding'], false],
    ['waiting', 'waiting', [], false],
    ['question', 'question', [], false],
    ['success', 'success', [], true],
    ['error', 'error', [], true],
  ]

  for (const [kind, state, activities, terminal] of cases) {
    const reducer = new CompanionReducer()
    assert.deepEqual(
      reducer.apply({ sessionId: kind, seq: 1, isSubagent: false, kind }),
      { state, activities, sequence: 1, terminal },
    )
  }
})

test('question is an exclusive user-blocked state and resumes thinking after its tool result', () => {
  const reducer = new CompanionReducer()
  reducer.apply({ sessionId: 'root', seq: 1, isSubagent: false, kind: 'work-start' })

  assert.deepEqual(
    reducer.apply({ sessionId: 'root', seq: 2, isSubagent: false, kind: 'question' }),
    { state: 'question', activities: [], sequence: 2, terminal: false },
  )
  assert.deepEqual(
    reducer.apply({ sessionId: 'root', seq: 3, isSubagent: false, kind: 'work-finish' }),
    { state: 'active', activities: ['thinking'], sequence: 3, terminal: false },
  )
})

test('question outranks background activity but not an approval request', () => {
  const reducer = new CompanionReducer()
  reducer.apply({ sessionId: 'background', seq: 8, isSubagent: false, kind: 'work-start' })
  assert.deepEqual(
    reducer.apply({ sessionId: 'root', seq: 4, isSubagent: false, kind: 'question' }),
    { state: 'question', activities: [], sequence: 4, terminal: false },
  )
  assert.deepEqual(
    reducer.apply({ sessionId: 'approval', seq: 1, isSubagent: false, kind: 'waiting' }),
    { state: 'waiting', activities: [], sequence: 1, terminal: false },
  )
})

test('cancelled and failed question turns do not leave a stale question state', () => {
  const reducer = new CompanionReducer()
  reducer.apply({ sessionId: 'cancelled', seq: 1, isSubagent: false, kind: 'question' })

  assert.deepEqual(
    reducer.apply({ sessionId: 'cancelled', seq: 2, isSubagent: false, kind: 'idle' }),
    { state: 'idle', activities: [], sequence: 2, terminal: false },
  )

  reducer.apply({ sessionId: 'failed', seq: 1, isSubagent: false, kind: 'question' })
  assert.deepEqual(
    reducer.apply({ sessionId: 'failed', seq: 2, isSubagent: false, kind: 'error' }),
    { state: 'error', activities: [], sequence: 2, terminal: true },
  )
})

test('keeps outputting visible until the turn ends after a text event', () => {
  const reducer = new CompanionReducer()
  reducer.apply({ sessionId: 'root', seq: 1, isSubagent: false, kind: 'thinking' })
  reducer.apply({ sessionId: 'root', seq: 2, isSubagent: false, kind: 'responding' })

  assert.deepEqual(
    reducer.apply({ sessionId: 'root', seq: 3, isSubagent: false, kind: 'thinking' }),
    { state: 'active', activities: ['responding'], sequence: 3, terminal: false },
  )
})

test('a new tool call interrupts outputting until the next text event', () => {
  const reducer = new CompanionReducer()
  reducer.apply({ sessionId: 'root', seq: 1, isSubagent: false, kind: 'responding' })

  assert.deepEqual(
    reducer.apply({ sessionId: 'root', seq: 2, isSubagent: false, kind: 'work-start' }),
    { state: 'active', activities: ['working'], sequence: 2, terminal: false },
  )
})

test('keeps thinking visible while a tool is running', () => {
  const reducer = new CompanionReducer()
  reducer.apply({ sessionId: 'root', seq: 1, isSubagent: false, kind: 'thinking' })

  assert.deepEqual(
    reducer.apply({ sessionId: 'root', seq: 2, isSubagent: false, kind: 'work-start' }),
    { state: 'active', activities: ['thinking', 'working'], sequence: 2, terminal: false },
  )
})

test('removes only the completed tool activity and clamps unmatched finishes', () => {
  const reducer = new CompanionReducer()
  reducer.apply({ sessionId: 'root', seq: 1, isSubagent: false, kind: 'thinking' })
  reducer.apply({ sessionId: 'root', seq: 2, isSubagent: false, kind: 'work-start' })
  reducer.apply({ sessionId: 'root', seq: 3, isSubagent: false, kind: 'work-start' })
  reducer.apply({ sessionId: 'root', seq: 4, isSubagent: false, kind: 'work-finish' })
  assert.deepEqual(reducer.current(), { state: 'active', activities: ['thinking', 'working'], sequence: 4, terminal: false })

  assert.deepEqual(
    reducer.apply({ sessionId: 'root', seq: 5, isSubagent: false, kind: 'work-finish' }),
    { state: 'active', activities: ['thinking'], sequence: 5, terminal: false },
  )
  assert.deepEqual(
    reducer.apply({ sessionId: 'root', seq: 6, isSubagent: false, kind: 'work-finish' }),
    { state: 'active', activities: ['thinking'], sequence: 6, terminal: false },
  )
})

test('waiting is exclusive and takes priority over composite activity', () => {
  const reducer = new CompanionReducer()
  reducer.apply({ sessionId: 'root', seq: 2, isSubagent: false, kind: 'work-start' })

  assert.deepEqual(
    reducer.apply({ sessionId: 'waiting', seq: 1, isSubagent: false, kind: 'waiting' }),
    { state: 'waiting', activities: [], sequence: 1, terminal: false },
  )
})

test('working activity outranks thinking and equal priorities use latest sequence', () => {
  const reducer = new CompanionReducer()
  reducer.apply({ sessionId: 'first', seq: 4, isSubagent: false, kind: 'thinking' })

  assert.deepEqual(
    reducer.apply({ sessionId: 'second', seq: 3, isSubagent: false, kind: 'work-start' }),
    { state: 'active', activities: ['working'], sequence: 3, terminal: false },
  )
})

test('an unmatched finish on an initial session remains idle', () => {
  const reducer = new CompanionReducer()

  assert.deepEqual(
    reducer.apply({ sessionId: 'root', seq: 7, isSubagent: false, kind: 'work-finish' }),
    { state: 'idle', activities: [], sequence: 7, terminal: false },
  )
})

test('same-priority top-level candidates use the latest sequence', () => {
  const reducer = new CompanionReducer()
  reducer.apply({ sessionId: 'first', seq: 3, isSubagent: false, kind: 'thinking' })

  assert.deepEqual(
    reducer.apply({ sessionId: 'second', seq: 4, isSubagent: false, kind: 'thinking' }),
    { state: 'active', activities: ['thinking'], sequence: 4, terminal: false },
  )
})

test('terminal facts clear activities and disposeTerminal removes only terminal records', () => {
  const reducer = new CompanionReducer({ includeSubagents: true })
  reducer.apply({ sessionId: 'root', seq: 1, isSubagent: false, kind: 'thinking' })
  reducer.apply({ sessionId: 'root', seq: 2, isSubagent: false, kind: 'work-start' })
  assert.deepEqual(
    reducer.apply({ sessionId: 'root', seq: 3, isSubagent: false, kind: 'success' }),
    { state: 'success', activities: [], sequence: 3, terminal: true },
  )
  reducer.apply({ sessionId: 'other', seq: 3, isSubagent: false, kind: 'thinking' })
  reducer.disposeTerminal(3)
  assert.deepEqual(reducer.current(), { state: 'active', activities: ['thinking'], sequence: 3, terminal: false })
})

test('terminal state cannot regress from later activity facts', () => {
  const reducer = new CompanionReducer()
  reducer.apply({ sessionId: 'root', seq: 1, isSubagent: false, kind: 'success' })

  assert.deepEqual(
    reducer.apply({ sessionId: 'root', seq: 2, isSubagent: false, kind: 'thinking' }),
    { state: 'success', activities: [], sequence: 1, terminal: true },
  )
  assert.deepEqual(
    reducer.apply({ sessionId: 'root', seq: 3, isSubagent: false, kind: 'work-start' }),
    { state: 'success', activities: [], sequence: 1, terminal: true },
  )
})

test('ignores duplicate facts and subagents by default', () => {
  const reducer = new CompanionReducer()
  reducer.apply({ sessionId: 'root', seq: 4, isSubagent: false, kind: 'thinking' })
  reducer.apply({ sessionId: 'root', seq: 4, isSubagent: false, kind: 'error' })

  assert.deepEqual(
    reducer.apply({ sessionId: 'child', seq: 9, isSubagent: true, kind: 'waiting' }),
    { state: 'active', activities: ['thinking'], sequence: 4, terminal: false },
  )
})

test('includes subagents only when enabled', () => {
  const reducer = new CompanionReducer({ includeSubagents: true })

  assert.deepEqual(
    reducer.apply({ sessionId: 'child', seq: 9, isSubagent: true, kind: 'waiting' }),
    { state: 'waiting', activities: [], sequence: 9, terminal: false },
  )
})

test('removes a disposed session without accepting a later fact', () => {
  const reducer = new CompanionReducer({ includeSubagents: true })
  reducer.apply({ sessionId: 's', seq: 1, isSubagent: false, kind: 'work-start' })
  reducer.dispose('s')

  assert.deepEqual(
    reducer.apply({ sessionId: 's', seq: 2, isSubagent: false, kind: 'error' }),
    { state: 'idle', activities: [], sequence: 0, terminal: false },
  )
})

test('ignores malformed facts', () => {
  const reducer = new CompanionReducer()

  assert.deepEqual(
    reducer.apply({ sessionId: '', seq: -1, isSubagent: 'no', kind: 'unknown' }),
    { state: 'idle', activities: [], sequence: 0, terminal: false },
  )
})
