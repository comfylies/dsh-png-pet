import assert from 'node:assert/strict'
import test from 'node:test'

import { encodeHostMessage, parseHelperMessage, parseHostMessage } from '../lib/protocol.js'

test('accepts a v4 ready message', () => {
  assert.deepEqual(
    parseHelperMessage('{"version":4,"kind":"ready"}'),
    { version: 4, kind: 'ready' },
  )
})

test('encodes a v4 canonical composite active state presentation', () => {
  assert.equal(
    encodeHostMessage({ kind: 'state', state: 'active', activities: ['thinking', 'working'], label: '思考中/工作中', sequence: 42 }),
    '{"version":4,"kind":"state","state":"active","activities":["thinking","working"],"label":"思考中/工作中","sequence":42}\n',
  )
})

test('accepts a bounded helper input and encodes bounded dialogue messages', () => {
  assert.deepEqual(
    parseHelperMessage('{"version":4,"kind":"input","requestId":7,"text":"hello"}'),
    { version: 4, kind: 'input', requestId: 7, text: 'hello' },
  )
  assert.equal(
    encodeHostMessage({ kind: 'conversation-config', previewEnabled: true, previewMaxChars: 480 }),
    '{"version":4,"kind":"conversation-config","previewEnabled":true,"previewMaxChars":480}\n',
  )
  assert.equal(
    encodeHostMessage({ kind: 'input-status', requestId: 7, status: 'queued' }),
    '{"version":4,"kind":"input-status","requestId":7,"status":"queued"}\n',
  )
  assert.equal(
    encodeHostMessage({ kind: 'reply-preview', requestId: 7, text: 'ok', completed: false }),
    '{"version":4,"kind":"reply-preview","requestId":7,"text":"ok","completed":false}\n',
  )
  assert.equal(
    encodeHostMessage({ kind: 'clear-preview', requestId: 7, reason: 'next-input' }),
    '{"version":4,"kind":"clear-preview","requestId":7,"reason":"next-input"}\n',
  )
})

test('rejects invalid v4 dialogue payloads', () => {
  assert.throws(
    () => parseHelperMessage('{"version":4,"kind":"input","requestId":1,"text":"x","extra":true}'),
    /fields/,
  )
  assert.throws(
    () => parseHelperMessage(JSON.stringify({ version: 4, kind: 'input', requestId: 1, text: 'x'.repeat(2001) })),
    /text/,
  )
  assert.throws(
    () => parseHelperMessage('{"version":4,"kind":"input","requestId":0,"text":"hello"}'),
    /requestId/,
  )
  assert.throws(
    () => parseHelperMessage('{"version":4,"kind":"input","requestId":1,"text":" hello"}'),
    /text/,
  )
  assert.throws(
    () => encodeHostMessage({ kind: 'conversation-config', previewEnabled: true, previewMaxChars: 79 }),
    /previewMaxChars/,
  )
  assert.throws(
    () => encodeHostMessage({ kind: 'conversation-config', previewEnabled: true, previewMaxChars: 2001 }),
    /previewMaxChars/,
  )
  assert.throws(
    () => parseHostMessage('{"version":4,"kind":"conversation-config","previewEnabled":true,"previewMaxChars":2001}'),
    /previewMaxChars/,
  )
  assert.throws(
    () => encodeHostMessage({ kind: 'input-status', requestId: 1, status: 'unknown' }),
    /status/,
  )
  assert.throws(
    () => encodeHostMessage({ kind: 'reply-preview', requestId: 1, text: '', completed: false }),
    /text/,
  )
  assert.throws(
    () => encodeHostMessage({ kind: 'clear-preview', requestId: 1, reason: 'other' }),
    /reason/,
  )
})

test('rejects a required host field inherited from the prototype', () => {
  const message = Object.create({ previewMaxChars: 480 })
  message.kind = 'conversation-config'
  message.previewEnabled = true

  assert.throws(() => encodeHostMessage(message), /missing required fields/)
})

test('parseHostMessage rejects extra fields for every v4 dialogue message kind', () => {
  const messages = [
    { version: 4, kind: 'conversation-config', previewEnabled: true, previewMaxChars: 480 },
    { version: 4, kind: 'input-status', requestId: 7, status: 'queued' },
    { version: 4, kind: 'reply-preview', requestId: 7, text: 'ok', completed: false },
    { version: 4, kind: 'clear-preview', requestId: 7, reason: 'next-input' },
  ]

  for (const message of messages) {
    assert.throws(() => parseHostMessage(JSON.stringify({ ...message, extra: true })), /fields/)
  }
})

test('accepts v4 dialogue boundaries and rejects 2001-character text', () => {
  const maximumText = 'x'.repeat(2000)
  const ready = '{"version":4,"kind":"ready"}'

  assert.deepEqual(
    parseHelperMessage(JSON.stringify({ version: 4, kind: 'input', requestId: 1, text: maximumText })),
    { version: 4, kind: 'input', requestId: 1, text: maximumText },
  )
  assert.equal(encodeHostMessage({ kind: 'reply-preview', requestId: 1, text: maximumText, completed: false }).endsWith('\n'), true)
  assert.deepEqual(
    parseHelperMessage(`${ready}${' '.repeat(4096 - ready.length)}`),
    { version: 4, kind: 'ready' },
  )
  assert.equal(
    encodeHostMessage({ kind: 'conversation-config', previewEnabled: true, previewMaxChars: 80 }).includes('"previewMaxChars":80'),
    true,
  )
  assert.equal(
    encodeHostMessage({ kind: 'conversation-config', previewEnabled: true, previewMaxChars: 2000 }).includes('"previewMaxChars":2000'),
    true,
  )
  assert.throws(
    () => encodeHostMessage({ kind: 'reply-preview', requestId: 1, text: 'x'.repeat(2001), completed: false }),
    /text/,
  )
})

test('rejects a state message with a free-form label', () => {
  assert.throws(
    () => parseHostMessage('{"version":4,"kind":"state","state":"active","activities":["working"],"label":"C:\\\\secret","sequence":1}'),
    /label/,
  )
})

test('rejects non-canonical composite activities', () => {
  assert.throws(
    () => parseHostMessage('{"version":4,"kind":"state","state":"active","activities":["working","thinking"],"label":"思考中/工作中","sequence":1}'),
    /activities/,
  )
})

test('rejects activities for an exclusive state', () => {
  assert.throws(
    () => parseHostMessage('{"version":4,"kind":"state","state":"waiting","activities":["thinking"],"label":"等待你的操作","sequence":1}'),
    /activities/,
  )
})

test('rejects an old Helper handshake', () => {
  assert.throws(() => parseHelperMessage('{"version":3,"kind":"ready"}'), /version/)
})

test('rejects an unknown helper message kind', () => {
  assert.throws(
    () => parseHelperMessage('{"version":4,"kind":"secret"}'),
    /kind/,
  )
})

test('rejects a message with an unsupported protocol version', () => {
  assert.throws(
    () => parseHelperMessage('{"version":5,"kind":"ready"}'),
    /version/,
  )
})

test('rejects a line longer than 4096 characters', () => {
  assert.throws(() => parseHelperMessage(' '.repeat(4097)), /long/)
})
