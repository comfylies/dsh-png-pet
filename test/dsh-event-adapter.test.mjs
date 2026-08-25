import assert from 'node:assert/strict'
import test from 'node:test'

import { adaptSessionEvent } from '../lib/dsh-event-adapter.js'

const topLevelSession = { id: 'root', header: {} }

test('maps whitelisted DSH event types to safe facts', () => {
  const cases = [
    ['turn/start', 'thinking'],
    ['step/start', 'thinking'],
    ['assistant/chunk', 'thinking'],
    ['assistant/message', 'thinking'],
    ['step/end', 'thinking'],
    ['tool/call', 'work-start'],
    ['tool/code-dispatch-start', 'work-start'],
    ['tool/result', 'work-finish'],
    ['tool/code-dispatch', 'work-finish'],
    ['approval/asked', 'waiting'],
    ['approval/decided', 'thinking'],
  ]

  for (const [type, kind] of cases) {
    assert.deepEqual(
      adaptSessionEvent(topLevelSession, { type, seq: 7, data: { ignored: true } }),
      { sessionId: 'root', seq: 7, isSubagent: false, kind },
    )
  }
})

test('does not retain tool payload data in tool facts', () => {
  for (const type of ['tool/call', 'tool/code-dispatch-start', 'tool/result', 'tool/code-dispatch']) {
    const fact = adaptSessionEvent(topLevelSession, {
      type,
      seq: 9,
      data: { arguments: { apiKey: 'secret', path: 'C:\\private' }, result: 'sensitive output' },
    })
    assert.equal(JSON.stringify(fact).includes('secret'), false)
    assert.equal(JSON.stringify(fact).includes('private'), false)
    assert.equal(JSON.stringify(fact).includes('sensitive output'), false)
  }
})

test('maps completed and failed turn endings without retaining error data', () => {
  const completed = adaptSessionEvent(topLevelSession, {
    type: 'turn/end', seq: 3, data: { reason: { kind: 'completed', ignored: true } },
  })
  const failed = adaptSessionEvent(topLevelSession, {
    type: 'turn/end', seq: 4, data: { reason: { kind: 'error', ignored: true } },
  })

  assert.equal(completed.kind, 'success')
  assert.equal(failed.kind, 'error')
  assert.equal(JSON.stringify(failed).includes('ignored'), false)
})

test('maps max-token endings to error and non-actionable endings to idle', () => {
  for (const [reason, kind] of [
    ['max-tokens', 'error'],
    ['aborted', 'idle'],
    ['blocked', 'idle'],
    ['interrupted', 'idle'],
    ['future-reason', 'idle'],
  ]) {
    assert.equal(
      adaptSessionEvent(topLevelSession, { type: 'turn/end', seq: 8, data: { reason: { kind: reason } } }).kind,
      kind,
    )
  }
})

test('marks delegated sessions without exposing header data', () => {
  assert.deepEqual(
    adaptSessionEvent({ id: 'child', header: { delegationDepth: 1, ignored: true } }, { type: 'turn/start', seq: 1 }),
    { sessionId: 'child', seq: 1, isSubagent: true, kind: 'thinking' },
  )
})

test('ignores malformed and unknown events without throwing', () => {
  assert.equal(adaptSessionEvent({ id: '', header: {} }, { type: 'turn/start', seq: 1 }), undefined)
  assert.equal(adaptSessionEvent(topLevelSession, { type: 'unknown', seq: 1 }), undefined)
  assert.equal(adaptSessionEvent(topLevelSession, { type: 'turn/end', seq: 1, data: {} }), undefined)
  assert.equal(adaptSessionEvent(topLevelSession, { type: 'turn/start', seq: -1 }), undefined)
})
