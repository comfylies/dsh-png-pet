import assert from 'node:assert/strict'
import test from 'node:test'

import { encodeHostMessage, parseHelperMessage, parseHostMessage } from '../lib/protocol.js'

test('accepts a v3 ready message', () => {
  assert.deepEqual(
    parseHelperMessage('{"version":3,"kind":"ready"}'),
    { version: 3, kind: 'ready' },
  )
})

test('encodes a canonical composite active state presentation', () => {
  assert.equal(
    encodeHostMessage({ kind: 'state', state: 'active', activities: ['thinking', 'working'], label: '思考中/工作中', sequence: 42 }),
    '{"version":3,"kind":"state","state":"active","activities":["thinking","working"],"label":"思考中/工作中","sequence":42}\n',
  )
})

test('rejects a state message with a free-form label', () => {
  assert.throws(
    () => parseHostMessage('{"version":3,"kind":"state","state":"active","activities":["working"],"label":"C:\\\\secret","sequence":1}'),
    /label/,
  )
})

test('rejects non-canonical composite activities', () => {
  assert.throws(
    () => parseHostMessage('{"version":3,"kind":"state","state":"active","activities":["working","thinking"],"label":"思考中/工作中","sequence":1}'),
    /activities/,
  )
})

test('rejects activities for an exclusive state', () => {
  assert.throws(
    () => parseHostMessage('{"version":3,"kind":"state","state":"waiting","activities":["thinking"],"label":"等待你的操作","sequence":1}'),
    /activities/,
  )
})

test('rejects an old Helper handshake', () => {
  assert.throws(() => parseHelperMessage('{"version":2,"kind":"ready"}'), /version/)
})

test('rejects an unknown helper message kind', () => {
  assert.throws(
    () => parseHelperMessage('{"version":3,"kind":"secret"}'),
    /kind/,
  )
})

test('rejects a message with an unsupported protocol version', () => {
  assert.throws(
    () => parseHelperMessage('{"version":4,"kind":"ready"}'),
    /version/,
  )
})
