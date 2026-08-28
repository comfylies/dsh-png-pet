import assert from 'node:assert/strict'
import test from 'node:test'

import { extractDialogueHistory } from '../lib/dialogue-history.js'

test('extracts only user text and final assistant text from the event log', () => {
  const events = [
    { type: 'user/message', seq: 1, data: { content: [{ type: 'text', text: '你好' }], source: { kind: 'user' } } },
    { type: 'assistant/message', seq: 2, data: { message: { content: [
      { type: 'reasoning', text: '隐藏的思考过程' },
      { type: 'text', text: '你好！' },
    ] } } },
    { type: 'tool/call', seq: 3, data: { name: 'bash', arguments: '秘密参数' } },
  ]

  assert.deepEqual(extractDialogueHistory(events), [
    { role: 'user', blocks: [{ type: 'text', text: '你好' }] },
    { role: 'assistant', blocks: [{ type: 'text', text: '你好！' }] },
  ])
})

test('skips tool-sourced user messages and empty user text', () => {
  const events = [
    { type: 'user/message', seq: 1, data: { content: [{ type: 'text', text: '工具结果' }], source: { kind: 'tool', callId: 'c1' } } },
    { type: 'user/message', seq: 2, data: { content: [{ type: 'text', text: '真实用户' }], source: { kind: 'user' } } },
    { type: 'user/message', seq: 3, data: { content: [{ type: 'text', text: '' }], source: { kind: 'user' } } },
  ]

  assert.deepEqual(extractDialogueHistory(events), [
    { role: 'user', blocks: [{ type: 'text', text: '真实用户' }] },
  ])
})

test('keeps tool-only and empty assistant turns as placeholders', () => {
  const events = [
    { type: 'assistant/message', seq: 1, data: { message: { content: [{ type: 'tool-call', id: 'c1', name: 'bash', arguments: '{"secret":true}' }] } } },
    { type: 'assistant/message', seq: 2, data: { message: { content: [] } } },
  ]

  assert.deepEqual(extractDialogueHistory(events), [
    { role: 'assistant', blocks: [{ type: 'text', text: '调用了 bash' }] },
    { role: 'assistant', blocks: [{ type: 'text', text: '（无内容）' }] },
  ])
})

test('projects image blocks as placeholders without attachment bytes', () => {
  const events = [
    { type: 'user/message', seq: 1, data: { content: [
      { type: 'image', attachment: { attachmentId: 'a-1', mediaType: 'image/png', bytes: 100, width: 640, height: 480, name: 'photo.png' } },
      { type: 'text', text: '看图' },
    ], source: { kind: 'user' } } },
  ]

  assert.deepEqual(extractDialogueHistory(events), [
    { role: 'user', blocks: [
      { type: 'image', name: 'photo.png', width: 640, height: 480 },
      { type: 'text', text: '看图' },
    ] },
  ])
})

test('keeps only the latest 20 messages and crops each text to 2000 chars', () => {
  const events = []
  for (let i = 0; i < 25; i++) {
    events.push({ type: 'user/message', seq: i, data: { content: [{ type: 'text', text: `m${i}` }], source: { kind: 'user' } } })
  }
  events.push({ type: 'assistant/message', seq: 25, data: { message: { content: [{ type: 'text', text: 'a'.repeat(3000) + 'tail' }] } } })

  const history = extractDialogueHistory(events)

  assert.equal(history.length, 20)
  assert.equal(history[0].role, 'user')
  assert.equal(history[0].blocks[0].text, 'm6')
  assert.equal(history[19].role, 'assistant')
  assert.equal(history[19].blocks[0].text.length, 2000)
  assert.equal(history[19].blocks[0].text.endsWith('tail'), true)
})

test('ignores malformed events without throwing', () => {
  const events = [null, 'junk', { type: 42 }, { type: 'user/message', data: { content: 'not-array' } }]

  assert.deepEqual(extractDialogueHistory(events), [])
})
