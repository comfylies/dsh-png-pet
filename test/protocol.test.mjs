import assert from 'node:assert/strict'
import test from 'node:test'

import { encodeHostMessage, parseHelperMessage, parseHostMessage } from '../lib/protocol.js'

test('accepts a v5 ready message', () => {
  assert.deepEqual(
    parseHelperMessage('{"version":5,"kind":"ready"}'),
    { version: 5, kind: 'ready' },
  )
})

test('encodes a v5 canonical composite active state presentation', () => {
  assert.equal(
    encodeHostMessage({ kind: 'state', state: 'active', activities: ['thinking', 'working'], label: '思考中/工作中', sequence: 42 }),
    '{"version":5,"kind":"state","state":"active","activities":["thinking","working"],"label":"思考中/工作中","sequence":42}\n',
  )
})

test('encodes a v5 canonical outputting active state presentation', () => {
  assert.equal(
    encodeHostMessage({ kind: 'state', state: 'active', activities: ['responding'], label: '输出中…', sequence: 42 }),
    '{"version":5,"kind":"state","state":"active","activities":["responding"],"label":"输出中…","sequence":42}\n',
  )
})

test('encodes a v5 question state with its fixed label', () => {
  assert.equal(
    encodeHostMessage({ kind: 'state', state: 'question', activities: [], label: '等你回答…', sequence: 42 }),
    '{"version":5,"kind":"state","state":"question","activities":[],"label":"等你回答…","sequence":42}\n',
  )
})

test('accepts a bounded helper input and encodes bounded dialogue messages', () => {
  assert.deepEqual(
    parseHelperMessage('{"version":5,"kind":"input","requestId":7,"text":"hello"}'),
    { version: 5, kind: 'input', requestId: 7, text: 'hello' },
  )
  assert.equal(
    encodeHostMessage({ kind: 'conversation-config', previewEnabled: true, previewMaxChars: 480, defaultSessionId: null }),
    '{"version":5,"kind":"conversation-config","previewEnabled":true,"previewMaxChars":480,"defaultSessionId":null}\n',
  )
  assert.equal(
    encodeHostMessage({ kind: 'input-status', requestId: 7, status: 'queued' }),
    '{"version":5,"kind":"input-status","requestId":7,"status":"queued"}\n',
  )
  assert.equal(
    encodeHostMessage({ kind: 'reply-preview', requestId: 7, text: 'ok', completed: false }),
    '{"version":5,"kind":"reply-preview","requestId":7,"text":"ok","completed":false}\n',
  )
  assert.equal(
    encodeHostMessage({ kind: 'clear-preview', requestId: 7, reason: 'next-input' }),
    '{"version":5,"kind":"clear-preview","requestId":7,"reason":"next-input"}\n',
  )
})

test('rejects invalid v5 dialogue payloads', () => {
  assert.throws(
    () => parseHelperMessage('{"version":5,"kind":"input","requestId":1,"text":"x","extra":true}'),
    /fields/,
  )
  assert.throws(
    () => parseHelperMessage(JSON.stringify({ version: 5, kind: 'input', requestId: 1, text: 'x'.repeat(2001) })),
    /text/,
  )
  assert.throws(
    () => parseHelperMessage('{"version":5,"kind":"input","requestId":0,"text":"hello"}'),
    /requestId/,
  )
  assert.throws(
    () => parseHelperMessage('{"version":5,"kind":"input","requestId":1,"text":" hello"}'),
    /text/,
  )
  assert.throws(
    () => encodeHostMessage({ kind: 'conversation-config', previewEnabled: true, previewMaxChars: 79, defaultSessionId: null }),
    /previewMaxChars/,
  )
  assert.throws(
    () => encodeHostMessage({ kind: 'conversation-config', previewEnabled: true, previewMaxChars: 2001, defaultSessionId: null }),
    /previewMaxChars/,
  )
  assert.throws(
    () => parseHostMessage('{"version":5,"kind":"conversation-config","previewEnabled":true,"previewMaxChars":2001,"defaultSessionId":null}'),
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

test('parseHostMessage rejects extra fields for every v5 dialogue message kind', () => {
  const messages = [
    { version: 5, kind: 'conversation-config', previewEnabled: true, previewMaxChars: 480, defaultSessionId: null },
    { version: 5, kind: 'input-status', requestId: 7, status: 'queued' },
    { version: 5, kind: 'reply-preview', requestId: 7, text: 'ok', completed: false },
    { version: 5, kind: 'clear-preview', requestId: 7, reason: 'next-input' },
  ]

  for (const message of messages) {
    assert.throws(() => parseHostMessage(JSON.stringify({ ...message, extra: true })), /fields/)
  }
})

test('accepts v5 dialogue boundaries and rejects 2001-character text', () => {
  const maximumText = 'x'.repeat(2000)
  const ready = '{"version":5,"kind":"ready"}'

  assert.deepEqual(
    parseHelperMessage(JSON.stringify({ version: 5, kind: 'input', requestId: 1, text: maximumText })),
    { version: 5, kind: 'input', requestId: 1, text: maximumText },
  )
  assert.equal(encodeHostMessage({ kind: 'reply-preview', requestId: 1, text: maximumText, completed: false }).endsWith('\n'), true)
  assert.deepEqual(
    parseHelperMessage(`${ready}${' '.repeat(4096 - ready.length)}`),
    { version: 5, kind: 'ready' },
  )
  assert.equal(
    encodeHostMessage({ kind: 'conversation-config', previewEnabled: true, previewMaxChars: 80, defaultSessionId: null }).includes('"previewMaxChars":80'),
    true,
  )
  assert.equal(
    encodeHostMessage({ kind: 'conversation-config', previewEnabled: true, previewMaxChars: 2000, defaultSessionId: null }).includes('"previewMaxChars":2000'),
    true,
  )
  assert.throws(
    () => encodeHostMessage({ kind: 'reply-preview', requestId: 1, text: 'x'.repeat(2001), completed: false }),
    /text/,
  )
})

test('rejects a state message with a free-form label', () => {
  assert.throws(
    () => parseHostMessage('{"version":5,"kind":"state","state":"active","activities":["working"],"label":"C:\\\\secret","sequence":1}'),
    /label/,
  )
})

test('rejects non-canonical composite activities', () => {
  assert.throws(
    () => parseHostMessage('{"version":5,"kind":"state","state":"active","activities":["working","thinking"],"label":"思考中/工作中","sequence":1}'),
    /activities/,
  )
})

test('rejects a mixed outputting activity presentation', () => {
  assert.throws(
    () => parseHostMessage('{"version":5,"kind":"state","state":"active","activities":["thinking","responding"],"label":"输出中…","sequence":1}'),
    /activities/,
  )
})

test('rejects activities for an exclusive state', () => {
  assert.throws(
    () => parseHostMessage('{"version":5,"kind":"state","state":"waiting","activities":["thinking"],"label":"等待你的操作","sequence":1}'),
    /activities/,
  )
})

test('rejects a question state with a mismatched label', () => {
  assert.throws(
    () => parseHostMessage('{"version":5,"kind":"state","state":"question","activities":[],"label":"等待你的操作","sequence":1}'),
    /label/,
  )
})

test('rejects an old Helper handshake', () => {
  assert.throws(() => parseHelperMessage('{"version":3,"kind":"ready"}'), /version/)
})

test('rejects an unknown helper message kind', () => {
  assert.throws(
    () => parseHelperMessage('{"version":5,"kind":"secret"}'),
    /kind/,
  )
})

test('rejects a message with an unsupported protocol version', () => {
  assert.throws(
    () => parseHelperMessage('{"version":6,"kind":"ready"}'),
    /version/,
  )
})

test('rejects a line longer than 65536 characters', () => {
  assert.throws(() => parseHelperMessage(' '.repeat(65537)), /long/)
})

test('round-trips a request-history helper message', () => {
  const parsed = parseHelperMessage('{"version":5,"kind":"request-history","requestId":9}\n')
  assert.deepEqual(parsed, { version: 5, kind: 'request-history', requestId: 9 })
})

test('round-trips a reply host message with the extended limit', () => {
  const text = 'a'.repeat(8000)
  const line = encodeHostMessage({ kind: 'reply', requestId: 3, text, completed: true })
  const parsed = parseHostMessage(line)
  assert.deepEqual(parsed, { version: 5, kind: 'reply', requestId: 3, text, completed: true })
})

test('rejects an over-limit reply text', () => {
  assert.throws(() => encodeHostMessage({ kind: 'reply', requestId: 3, text: 'a'.repeat(8001), completed: true }))
})

test('round-trips a conversation-history message with bounded entries', () => {
  const messages = [{ role: 'user', text: 'hi' }, { role: 'assistant', text: 'hello' }]
  const parsed = parseHostMessage(encodeHostMessage({ kind: 'conversation-history', requestId: 4, available: true, messages }))
  assert.deepEqual(parsed, { version: 5, kind: 'conversation-history', requestId: 4, available: true, messages })
})

test('rejects history entries beyond the limit or with unknown roles', () => {
  const tooMany = Array.from({ length: 21 }, (_, i) => ({ role: 'user', text: `m${i}` }))
  assert.throws(() => encodeHostMessage({ kind: 'conversation-history', requestId: 4, available: true, messages: tooMany }))
  assert.throws(() => encodeHostMessage({ kind: 'conversation-history', requestId: 4, available: true, messages: [{ role: 'system', text: 'x' }] }))
  assert.throws(() => encodeHostMessage({ kind: 'conversation-history', requestId: 4, available: true, messages: [{ role: 'user', text: '' }] }))
})

test('requires defaultSessionId on conversation-config', () => {
  assert.throws(() => encodeHostMessage({ kind: 'conversation-config', previewEnabled: true, previewMaxChars: 480 }))
  const parsed = parseHostMessage(encodeHostMessage({ kind: 'conversation-config', previewEnabled: true, previewMaxChars: 480, defaultSessionId: 's-1' }))
  assert.equal(parsed.defaultSessionId, 's-1')
})
