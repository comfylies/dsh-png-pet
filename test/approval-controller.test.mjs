import assert from 'node:assert/strict'
import test from 'node:test'

import { ApprovalController } from '../lib/approval-controller.js'

function request(sessionId = 's-1', signal) {
  return { agent: { session: { id: sessionId } }, ...(signal === undefined ? {} : { signal }) }
}

test('projects a selected-session approval without exposing its DSH payload and resolves once', async () => {
  const sent = []
  const controller = new ApprovalController((id) => id === 's-1', () => true, (message) => sent.push(message))
  const result = controller.request(request(), async () => 'unavailable')

  assert.deepEqual(sent, [{ kind: 'approval-request', requestId: 1 }])
  controller.answer({ version: 16, kind: 'approval-answer', requestId: 1, outcome: 'allowed-once' })
  controller.answer({ version: 16, kind: 'approval-answer', requestId: 1, outcome: 'rejected' })

  assert.equal(await result, 'allowed-once')
  assert.deepEqual(sent, [
    { kind: 'approval-request', requestId: 1 },
    { kind: 'approval-resolved', requestId: 1, outcome: 'allowed-once' },
  ])
})

test('delegates other sessions to the existing Web approval answerer', async () => {
  const sent = []
  const controller = new ApprovalController((id) => id === 's-1', () => true, (message) => sent.push(message))
  let delegated = 0

  assert.equal(await controller.request(request('s-2'), async () => { delegated++; return 'rejected' }), 'rejected')
  assert.equal(delegated, 1)
  assert.deepEqual(sent, [])
})

test('delegates to Web when the Web approval surface is selected', async () => {
  const sent = []
  const controller = new ApprovalController(() => true, () => true, (message) => sent.push(message), () => false)

  assert.equal(await controller.request(request(), async () => 'rejected'), 'rejected')
  assert.deepEqual(sent, [])
})

test('delegates a second concurrent request so one desktop card cannot orphan another', async () => {
  const sent = []
  const controller = new ApprovalController(() => true, () => true, (message) => sent.push(message))
  const first = controller.request(request('s-1'), async () => 'unavailable')

  assert.equal(await controller.request(request('s-2'), async () => 'rejected'), 'rejected')
  assert.equal(sent.length, 1)

  controller.answer({ version: 16, kind: 'approval-answer', requestId: 1, outcome: 'allowed-once' })
  assert.equal(await first, 'allowed-once')
})

test('fails closed when the Helper is absent or stops while an approval is pending', async () => {
  const sent = []
  let available = false
  const controller = new ApprovalController(() => true, () => available, (message) => sent.push(message))
  assert.equal(await controller.request(request(), async () => 'rejected'), 'unavailable')

  available = true
  const result = controller.request(request(), async () => 'rejected')
  controller.helperUnavailable()
  assert.equal(await result, 'unavailable')
  assert.deepEqual(sent, [
    { kind: 'approval-request', requestId: 1 },
    { kind: 'approval-resolved', requestId: 1, outcome: 'unavailable' },
  ])
})

test('withdraws an aborted approval and ignores a late Helper answer', async () => {
  const sent = []
  const abort = new AbortController()
  const controller = new ApprovalController(() => true, () => true, (message) => sent.push(message))
  const result = controller.request(request('s-1', abort.signal), async () => 'rejected')
  abort.abort()
  controller.answer({ version: 16, kind: 'approval-answer', requestId: 1, outcome: 'allowed-once' })

  assert.equal(await result, 'cancelled')
  assert.deepEqual(sent, [
    { kind: 'approval-request', requestId: 1 },
    { kind: 'approval-resolved', requestId: 1, outcome: 'cancelled' },
  ])
})
