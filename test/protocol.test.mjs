import assert from 'node:assert/strict'
import test from 'node:test'

import { encodeHostMessage, parseHelperMessage, parseHostMessage } from '../lib/protocol.js'

test('accepts a v7 ready message', () => {
  assert.deepEqual(
    parseHelperMessage('{"version":7,"kind":"ready"}'),
    { version: 7, kind: 'ready' },
  )
})

test('accepts the v7 default-layout config and rejects incomplete layout values', () => {
  const config = {
    kind: 'config',
    scale: 1.25,
    reducedMotion: true,
    petPlacement: 'bottom-right',
    dialoguePlacement: 'near-pet',
    dialogueWidth: 320,
    dialogueHeight: 420,
  }
  const line = encodeHostMessage(config)

  assert.equal(line, '{"version":7,"kind":"config","scale":1.25,"reducedMotion":true,"petPlacement":"bottom-right","dialoguePlacement":"near-pet","dialogueWidth":320,"dialogueHeight":420}\n')
  assert.deepEqual(parseHostMessage(line), { version: 7, ...config })
  assert.throws(() => encodeHostMessage({ ...config, dialogueWidth: 219 }), /config/)
  assert.throws(() => encodeHostMessage({ ...config, petPlacement: 'left' }), /config/)
})

test('encodes a v7 canonical composite active state presentation', () => {
  assert.equal(
    encodeHostMessage({ kind: 'state', state: 'active', activities: ['thinking', 'working'], label: '思考中/工作中', sequence: 42 }),
    '{"version":7,"kind":"state","state":"active","activities":["thinking","working"],"label":"思考中/工作中","sequence":42}\n',
  )
})

test('encodes a v7 canonical outputting active state presentation', () => {
  assert.equal(
    encodeHostMessage({ kind: 'state', state: 'active', activities: ['responding'], label: '输出中…', sequence: 42 }),
    '{"version":7,"kind":"state","state":"active","activities":["responding"],"label":"输出中…","sequence":42}\n',
  )
})

test('encodes a v7 question state with its fixed label', () => {
  assert.equal(
    encodeHostMessage({ kind: 'state', state: 'question', activities: [], label: '等你回答…', sequence: 42 }),
    '{"version":7,"kind":"state","state":"question","activities":[],"label":"等你回答…","sequence":42}\n',
  )
})

test('accepts a bounded helper input and encodes bounded dialogue messages', () => {
  assert.deepEqual(
    parseHelperMessage('{"version":7,"kind":"input","requestId":7,"text":"hello"}'),
    { version: 7, kind: 'input', requestId: 7, text: 'hello' },
  )
  assert.equal(
    encodeHostMessage({ kind: 'conversation-config', previewEnabled: true, previewMaxChars: 480, defaultSessionId: null, defaultWorkspaceId: null }),
    '{"version":7,"kind":"conversation-config","previewEnabled":true,"previewMaxChars":480,"defaultSessionId":null,"defaultWorkspaceId":null}\n',
  )
  assert.equal(
    encodeHostMessage({ kind: 'input-status', requestId: 7, status: 'queued' }),
    '{"version":7,"kind":"input-status","requestId":7,"status":"queued"}\n',
  )
  assert.equal(
    encodeHostMessage({ kind: 'reply-preview', requestId: 7, text: 'ok', completed: false }),
    '{"version":7,"kind":"reply-preview","requestId":7,"text":"ok","completed":false}\n',
  )
  assert.equal(
    encodeHostMessage({ kind: 'clear-preview', requestId: 7, reason: 'next-input' }),
    '{"version":7,"kind":"clear-preview","requestId":7,"reason":"next-input"}\n',
  )
})

test('accepts an input with attachments and an empty text', () => {
  assert.deepEqual(
    parseHelperMessage('{"version":7,"kind":"input","requestId":7,"text":"","attachments":[{"type":"image","mediaType":"image/png","base64":"AAAA"},{"type":"file","path":"C:\\\\a.txt","name":"a.txt"}]}'),
    {
      version: 7,
      kind: 'input',
      requestId: 7,
      text: '',
      attachments: [
        { type: 'image', mediaType: 'image/png', base64: 'AAAA' },
        { type: 'file', path: 'C:\\a.txt', name: 'a.txt' },
      ],
    },
  )
  assert.deepEqual(
    parseHelperMessage('{"version":7,"kind":"input","requestId":8,"text":"看图","attachments":[{"type":"image","mediaType":"image/jpeg","base64":"BBBB","name":"pic.jpg"}]}'),
    {
      version: 7,
      kind: 'input',
      requestId: 8,
      text: '看图',
      attachments: [{ type: 'image', mediaType: 'image/jpeg', base64: 'BBBB', name: 'pic.jpg' }],
    },
  )
})

test('round-trips a stop helper message', () => {
  assert.deepEqual(
    parseHelperMessage('{"version":7,"kind":"stop","requestId":12}\n'),
    { version: 7, kind: 'stop', requestId: 12 },
  )
})

test('rejects invalid v7 dialogue payloads', () => {
  assert.throws(
    () => parseHelperMessage('{"version":7,"kind":"input","requestId":1,"text":"x","extra":true}'),
    /fields/,
  )
  assert.throws(
    () => parseHelperMessage(JSON.stringify({ version: 7, kind: 'input', requestId: 1, text: 'x'.repeat(2001) })),
    /text/,
  )
  assert.throws(
    () => parseHelperMessage('{"version":7,"kind":"input","requestId":0,"text":"hello"}'),
    /requestId/,
  )
  assert.throws(
    () => parseHelperMessage('{"version":7,"kind":"input","requestId":1,"text":" hello"}'),
    /text/,
  )
  assert.throws(
    () => parseHelperMessage('{"version":7,"kind":"input","requestId":1,"text":""}'),
    /requires text or an attachment/,
  )
  assert.throws(
    () => parseHelperMessage('{"version":7,"kind":"input","requestId":1,"text":"x","attachments":[]}'),
    /attachments/,
  )
  assert.throws(
    () => parseHelperMessage(`{"version":7,"kind":"input","requestId":1,"text":"x","attachments":[{"type":"image","mediaType":"image/gif","base64":"${'A'.repeat(3000001)}"}]}`),
    /image/,
  )
  assert.throws(
    () => parseHelperMessage('{"version":7,"kind":"input","requestId":1,"text":"x","attachments":[{"type":"image","mediaType":"image/tiff","base64":"AAAA"}]}'),
    /image/,
  )
  assert.throws(
    () => parseHelperMessage('{"version":7,"kind":"input","requestId":1,"text":"x","attachments":[{"type":"file","path":""}]}'),
    /file/,
  )
  assert.throws(
    () => parseHelperMessage('{"version":7,"kind":"stop","requestId":0}'),
    /requestId/,
  )
  assert.throws(
    () => encodeHostMessage({ kind: 'conversation-config', previewEnabled: true, previewMaxChars: 79, defaultSessionId: null, defaultWorkspaceId: null }),
    /previewMaxChars/,
  )
  assert.throws(
    () => encodeHostMessage({ kind: 'conversation-config', previewEnabled: true, previewMaxChars: 8001, defaultSessionId: null, defaultWorkspaceId: null }),
    /previewMaxChars/,
  )
  assert.throws(
    () => parseHostMessage('{"version":7,"kind":"conversation-config","previewEnabled":true,"previewMaxChars":8001,"defaultSessionId":null,"defaultWorkspaceId":null}'),
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

test('parseHostMessage rejects extra fields for every v7 dialogue message kind', () => {
  const messages = [
    { version: 7, kind: 'conversation-config', previewEnabled: true, previewMaxChars: 480, defaultSessionId: null, defaultWorkspaceId: null },
    { version: 7, kind: 'input-status', requestId: 7, status: 'queued' },
    { version: 7, kind: 'reply-preview', requestId: 7, text: 'ok', completed: false },
    { version: 7, kind: 'clear-preview', requestId: 7, reason: 'next-input' },
  ]

  for (const message of messages) {
    assert.throws(() => parseHostMessage(JSON.stringify({ ...message, extra: true })), /fields/)
  }
})

test('accepts v7 dialogue boundaries and rejects 2001-character text', () => {
  const maximumText = 'x'.repeat(2000)
  const ready = '{"version":7,"kind":"ready"}'

  assert.deepEqual(
    parseHelperMessage(JSON.stringify({ version: 7, kind: 'input', requestId: 1, text: maximumText })),
    { version: 7, kind: 'input', requestId: 1, text: maximumText },
  )
  assert.equal(encodeHostMessage({ kind: 'reply-preview', requestId: 1, text: maximumText, completed: false }).endsWith('\n'), true)
  assert.deepEqual(
    parseHelperMessage(`${ready}${' '.repeat(4096 - ready.length)}`),
    { version: 7, kind: 'ready' },
  )
  assert.equal(
    encodeHostMessage({ kind: 'conversation-config', previewEnabled: true, previewMaxChars: 80, defaultSessionId: null, defaultWorkspaceId: null }).includes('"previewMaxChars":80'),
    true,
  )
  assert.equal(
    encodeHostMessage({ kind: 'conversation-config', previewEnabled: true, previewMaxChars: 2000, defaultSessionId: null, defaultWorkspaceId: null }).includes('"previewMaxChars":2000'),
    true,
  )
  assert.throws(
    () => encodeHostMessage({ kind: 'reply-preview', requestId: 1, text: 'x'.repeat(8001), completed: false }),
    /text/,
  )
})

test('rejects a state message with a free-form label', () => {
  assert.throws(
    () => parseHostMessage('{"version":7,"kind":"state","state":"active","activities":["working"],"label":"C:\\\\secret","sequence":1}'),
    /label/,
  )
})

test('rejects non-canonical composite activities', () => {
  assert.throws(
    () => parseHostMessage('{"version":7,"kind":"state","state":"active","activities":["working","thinking"],"label":"思考中/工作中","sequence":1}'),
    /activities/,
  )
})

test('rejects a mixed outputting activity presentation', () => {
  assert.throws(
    () => parseHostMessage('{"version":7,"kind":"state","state":"active","activities":["thinking","responding"],"label":"输出中…","sequence":1}'),
    /activities/,
  )
})

test('rejects activities for an exclusive state', () => {
  assert.throws(
    () => parseHostMessage('{"version":7,"kind":"state","state":"waiting","activities":["thinking"],"label":"等待你的操作","sequence":1}'),
    /activities/,
  )
})

test('rejects a question state with a mismatched label', () => {
  assert.throws(
    () => parseHostMessage('{"version":7,"kind":"state","state":"question","activities":[],"label":"等待你的操作","sequence":1}'),
    /label/,
  )
})

test('rejects an old Helper handshake', () => {
  assert.throws(() => parseHelperMessage('{"version":3,"kind":"ready"}'), /version/)
})

test('rejects an unknown helper message kind', () => {
  assert.throws(
    () => parseHelperMessage('{"version":7,"kind":"secret"}'),
    /kind/,
  )
})

test('rejects a message with an unsupported protocol version', () => {
  assert.throws(
    () => parseHelperMessage('{"version":5,"kind":"ready"}'),
    /version/,
  )
})

test('rejects a line longer than the 16 million character limit', () => {
  assert.throws(() => parseHelperMessage(' '.repeat(16_000_001)), /long/)
})

test('accepts a large image attachment line', () => {
  const base64 = 'A'.repeat(3_000_000)
  const parsed = parseHelperMessage(JSON.stringify({ version: 7, kind: 'input', requestId: 9, text: '', attachments: [{ type: 'image', mediaType: 'image/png', base64 }] }))
  assert.equal(parsed.kind, 'input')
  assert.equal(parsed.attachments[0].base64.length, 3_000_000)
})

test('round-trips a request-history helper message', () => {
  const parsed = parseHelperMessage('{"version":7,"kind":"request-history","requestId":9}\n')
  assert.deepEqual(parsed, { version: 7, kind: 'request-history', requestId: 9 })
})

test('round-trips a reply host message with the extended limit', () => {
  const text = 'a'.repeat(8000)
  const line = encodeHostMessage({ kind: 'reply', requestId: 3, text, completed: true })
  const parsed = parseHostMessage(line)
  assert.deepEqual(parsed, { version: 7, kind: 'reply', requestId: 3, text, completed: true })
})

test('rejects an over-limit reply text', () => {
  assert.throws(() => encodeHostMessage({ kind: 'reply', requestId: 3, text: 'a'.repeat(8001), completed: true }))
})

test('round-trips a conversation-history message with block entries', () => {
  const messages = [
    { role: 'user', blocks: [{ type: 'text', text: 'hi' }] },
    { role: 'assistant', blocks: [{ type: 'text', text: 'hello' }, { type: 'image', name: 'chart.png', width: 640, height: 480 }] },
  ]
  const parsed = parseHostMessage(encodeHostMessage({ kind: 'conversation-history', requestId: 4, available: true, messages }))
  assert.deepEqual(parsed, { version: 7, kind: 'conversation-history', requestId: 4, available: true, messages })
})

test('rejects history entries beyond the limit or with invalid blocks', () => {
  const tooMany = Array.from({ length: 21 }, (_, i) => ({ role: 'user', blocks: [{ type: 'text', text: `m${i}` }] }))
  assert.throws(() => encodeHostMessage({ kind: 'conversation-history', requestId: 4, available: true, messages: tooMany }))
  assert.throws(() => encodeHostMessage({ kind: 'conversation-history', requestId: 4, available: true, messages: [{ role: 'system', blocks: [{ type: 'text', text: 'x' }] }] }))
  assert.throws(() => encodeHostMessage({ kind: 'conversation-history', requestId: 4, available: true, messages: [{ role: 'user', blocks: [] }] }))
  assert.throws(() => encodeHostMessage({ kind: 'conversation-history', requestId: 4, available: true, messages: [{ role: 'user', blocks: [{ type: 'text', text: '' }] }] }))
  assert.throws(() => encodeHostMessage({ kind: 'conversation-history', requestId: 4, available: true, messages: [{ role: 'user', blocks: [{ type: 'image', name: 'a', width: 0, height: 1 }] }] }))
  assert.throws(() => encodeHostMessage({ kind: 'conversation-history', requestId: 4, available: true, messages: [{ role: 'user', blocks: [{ type: 'unknown' }] }] }))
})

test('requires defaultSessionId and defaultWorkspaceId on conversation-config', () => {
  assert.throws(() => encodeHostMessage({ kind: 'conversation-config', previewEnabled: true, previewMaxChars: 480 }))
  const parsed = parseHostMessage(encodeHostMessage({ kind: 'conversation-config', previewEnabled: true, previewMaxChars: 480, defaultSessionId: 's-1', defaultWorkspaceId: 'w-1' }))
  assert.equal(parsed.defaultSessionId, 's-1')
  assert.equal(parsed.defaultWorkspaceId, 'w-1')
})

test('accepts the terminal input statuses', () => {
  for (const status of ['stopped', 'interrupted', 'failed']) {
    const line = encodeHostMessage({ kind: 'input-status', requestId: 3, status })
    assert.deepEqual(parseHostMessage(line), { version: 7, kind: 'input-status', requestId: 3, status })
  }
})

test('round-trips target-open and every target-answer variant', () => {
  assert.deepEqual(
    parseHelperMessage('{"version":7,"kind":"target-open","requestId":9}'),
    { version: 7, kind: 'target-open', requestId: 9 },
  )
  assert.deepEqual(
    parseHelperMessage('{"version":7,"kind":"target-answer","requestId":9,"sessionId":"s-1","workspaceId":"w-1","newBlank":false}'),
    { version: 7, kind: 'target-answer', requestId: 9, sessionId: 's-1', workspaceId: 'w-1', newBlank: false },
  )
  assert.deepEqual(
    parseHelperMessage('{"version":7,"kind":"target-answer","requestId":9,"sessionId":null,"workspaceId":"w-1","newBlank":true}'),
    { version: 7, kind: 'target-answer', requestId: 9, sessionId: null, workspaceId: 'w-1', newBlank: true },
  )
  assert.deepEqual(
    parseHelperMessage('{"version":7,"kind":"target-answer","requestId":9,"sessionId":null,"workspaceId":null,"newBlank":false,"path":"C:\\\\dir","newWorkspace":true}'),
    { version: 7, kind: 'target-answer', requestId: 9, sessionId: null, workspaceId: null, newBlank: false, path: 'C:\\dir', newWorkspace: true },
  )
})

test('rejects malformed target messages', () => {
  assert.throws(() => parseHelperMessage('{"version":7,"kind":"target-answer","requestId":9,"sessionId":"s-1","workspaceId":null,"newBlank":true}'), /new blank/)
  assert.throws(() => parseHelperMessage('{"version":7,"kind":"target-answer","requestId":9,"sessionId":null,"workspaceId":null,"newBlank":false}'), /empty target/)
  assert.throws(() => parseHelperMessage('{"version":7,"kind":"target-answer","requestId":9,"sessionId":null,"workspaceId":null,"newBlank":false,"path":"C:\\\\dir","newWorkspace":true,"extra":1}'), /fields/)
  assert.throws(() => parseHelperMessage('{"version":7,"kind":"target-answer","requestId":9,"sessionId":null,"workspaceId":"w-1","newBlank":false,"newWorkspace":true,"path":"C:\\\\dir"}'), /invalid workspace create/)
  assert.throws(() => parseHelperMessage('{"version":7,"kind":"target-answer","requestId":9,"sessionId":"s-1","workspaceId":null,"newBlank":false,"path":"C:\\\\dir"}'), /unexpected path/)
})

test('round-trips a target-request with grouped and ungrouped sessions', () => {
  const line = encodeHostMessage({
    kind: 'target-request',
    requestId: 7,
    workspaces: [{ id: 'w-1', title: 'pet-helper', path: 'C:\\pet-helper' }],
    sessionsByWorkspace: { 'w-1': [{ id: 's-9', title: '修复窗口', blank: false }] },
    ungrouped: [{ id: 's-2', title: '', blank: true }],
    defaultWorkspaceId: 'w-1',
    defaultSessionId: 's-9',
  })
  const parsed = parseHostMessage(line)
  assert.deepEqual(parsed, {
    version: 7,
    kind: 'target-request',
    requestId: 7,
    workspaces: [{ id: 'w-1', title: 'pet-helper', path: 'C:\\pet-helper' }],
    sessionsByWorkspace: { 'w-1': [{ id: 's-9', title: '修复窗口', blank: false }] },
    ungrouped: [{ id: 's-2', title: '', blank: true }],
    defaultWorkspaceId: 'w-1',
    defaultSessionId: 's-9',
  })
})

test('rejects an oversized or invalid target-request', () => {
  const tooMany = Array.from({ length: 65 }, (_, i) => ({ id: `w-${i}`, title: 't', path: 'C:\\x' }))
  assert.throws(() => encodeHostMessage({
    kind: 'target-request',
    requestId: 7,
    workspaces: tooMany,
    sessionsByWorkspace: {},
    ungrouped: [],
    defaultWorkspaceId: null,
    defaultSessionId: null,
  }), /workspaces/)
  assert.throws(() => encodeHostMessage({
    kind: 'target-request',
    requestId: 7,
    workspaces: [{ id: '', title: 't', path: 'C:\\x' }],
    sessionsByWorkspace: {},
    ungrouped: [],
    defaultWorkspaceId: null,
    defaultSessionId: null,
  }), /workspace/)
  assert.throws(() => encodeHostMessage({
    kind: 'target-request',
    requestId: 7,
    workspaces: [],
    sessionsByWorkspace: { 'w-1': [{ id: 's-1', title: 't', blank: 'yes' }] },
    ungrouped: [],
    defaultWorkspaceId: null,
    defaultSessionId: null,
  }), /session/)
})
