import assert from 'node:assert/strict'
import test from 'node:test'

import { adaptSessionEvent } from '../lib/dsh-event-adapter.js'

const topLevelSession = { id: 'root', header: {} }

test('maps whitelisted DSH event types to safe facts', () => {
  const cases = [
    ['turn/start', 'thinking'],
    ['step/start', 'thinking'],
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

test('maps only user-visible assistant text to responding', () => {
  const cases = [
    [{ type: 'assistant/chunk', seq: 1, data: { chunk: { type: 'reasoning-delta', text: 'private reasoning' } } }, 'thinking'],
    [{ type: 'assistant/chunk', seq: 2, data: { chunk: { type: 'tool-call-delta', text: 'private tool data' } } }, 'thinking'],
    [{ type: 'assistant/chunk', seq: 3, data: { chunk: { type: 'text-delta', text: 'visible reply' } } }, 'responding'],
    [{ type: 'assistant/message', seq: 4, data: { message: { content: [{ type: 'reasoning', text: 'private reasoning' }] } } }, 'thinking'],
    [{ type: 'assistant/message', seq: 5, data: { message: { content: [{ type: 'reasoning', text: 'private reasoning' }, { type: 'text', text: 'visible reply' }] } } }, 'responding'],
  ]

  for (const [event, kind] of cases) {
    const fact = adaptSessionEvent(topLevelSession, event)
    assert.equal(fact.kind, kind)
    assert.equal(JSON.stringify(fact).includes('private'), false)
    assert.equal(JSON.stringify(fact).includes('visible'), false)
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

test('maps only the ask-user tool call to a question without retaining its payload', () => {
  const question = adaptSessionEvent(topLevelSession, {
    type: 'tool/call',
    seq: 10,
    data: { name: 'ask_user_question', arguments: { question: 'secret question', options: ['secret option'] } },
  })
  const regularTool = adaptSessionEvent(topLevelSession, {
    type: 'tool/call', seq: 11, data: { name: 'bash', arguments: { command: 'secret command' } },
  })

  assert.deepEqual(question, { sessionId: 'root', seq: 10, isSubagent: false, kind: 'question' })
  assert.equal(regularTool.kind, 'work-start')
  assert.equal(JSON.stringify(question).includes('secret'), false)
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
