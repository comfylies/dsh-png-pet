import assert from 'node:assert/strict'
import test from 'node:test'

import { parseHelperMessage } from '../lib/protocol.js'

test('accepts a v1 ready message', () => {
  assert.deepEqual(
    parseHelperMessage('{"version":1,"kind":"ready"}'),
    { version: 1, kind: 'ready' },
  )
})

test('rejects an unknown helper message kind', () => {
  assert.throws(
    () => parseHelperMessage('{"version":1,"kind":"secret"}'),
    /kind/,
  )
})

test('rejects a message with an unsupported protocol version', () => {
  assert.throws(
    () => parseHelperMessage('{"version":2,"kind":"ready"}'),
    /version/,
  )
})
