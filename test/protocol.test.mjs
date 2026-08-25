import assert from 'node:assert/strict'
import test from 'node:test'

import { encodeHostMessage, parseHelperMessage, parseHostMessage } from '../lib/protocol.js'

test('accepts a v2 ready message', () => {
  assert.deepEqual(
    parseHelperMessage('{"version":2,"kind":"ready"}'),
    { version: 2, kind: 'ready' },
  )
})

test('encodes only a fixed working state presentation', () => {
  assert.equal(
    encodeHostMessage({ kind: 'state', state: 'working', label: '工作中…', sequence: 42 }),
    '{"version":2,"kind":"state","state":"working","label":"工作中…","sequence":42}\n',
  )
})

test('rejects a state message with a free-form label', () => {
  assert.throws(
    () => parseHostMessage('{"version":2,"kind":"state","state":"working","label":"C:\\\\secret","sequence":1}'),
    /label/,
  )
})

test('rejects an old Helper handshake', () => {
  assert.throws(() => parseHelperMessage('{"version":1,"kind":"ready"}'), /version/)
})

test('rejects an unknown helper message kind', () => {
  assert.throws(
    () => parseHelperMessage('{"version":2,"kind":"secret"}'),
    /kind/,
  )
})

test('rejects a message with an unsupported protocol version', () => {
  assert.throws(
    () => parseHelperMessage('{"version":3,"kind":"ready"}'),
    /version/,
  )
})
