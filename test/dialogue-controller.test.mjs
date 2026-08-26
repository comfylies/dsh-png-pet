import assert from 'node:assert/strict'
import test from 'node:test'

import { DialogueController } from '../lib/dialogue-controller.js'

function createDsh({
  settings = { defaultSessionId: 's-1', previewEnabled: true, previewMaxChars: 80 },
  agent,
  resumedAgent,
  resumeFails = false,
  resume,
} = {}) {
  const writes = []
  const settingsListeners = new Set()
  let currentSettings = settings
  const context = {
    settings: {
      get: () => currentSettings,
      update: (next) => {
        const previous = currentSettings
        currentSettings = { ...currentSettings, ...next }
        writes.push(next)
        for (const listener of settingsListeners) listener(next, previous)
      },
      watch: (listener) => {
        settingsListeners.add(listener)
        return () => settingsListeners.delete(listener)
      },
    },
    agents: {
      get: (id) => id === 's-1' ? agent : undefined,
      resume: async ({ resumeSessionId }) => {
        assert.equal(resumeSessionId, 's-1')
        if (resume !== undefined) return resume({ resumeSessionId })
        if (resumeFails) throw new Error('unavailable')
        return resumedAgent === undefined ? undefined : { agent: resumedAgent }
      },
    },
  }
  return {
    context,
    writes,
    updateSettings(next) {
      const previous = currentSettings
      currentSettings = next
      for (const listener of settingsListeners) listener(next, previous)
    },
  }
}

test('maps its generated user message id to the next turn and forwards only text deltas', async () => {
  const sent = []
  const followups = []
  const dsh = createDsh({ agent: { status: 'idle', followup: (message) => followups.push(message) } })
  const controller = new DialogueController(dsh.context, (message) => sent.push(message))

  await controller.acceptInput({ requestId: 3, text: 'input omitted' })
  controller.observeEvent('s-1', { type: 'user/message', data: { id: followups[0].id } })
  controller.observeEvent('s-1', { type: 'turn/start', data: { turn: 12 } })
  controller.observeEvent('s-1', { type: 'assistant/chunk', data: { turn: 12, chunk: { type: 'text-delta', text: 'answer' } } })
  controller.observeEvent('s-1', { type: 'turn/end', data: { turn: 12 } })

  assert.equal(followups.length, 1)
  assert.equal(followups[0].content[0].type, 'text')
  assert.equal(followups[0].content[0].text.length, 13)
  assert.deepEqual(followups[0].source, { kind: 'user' })
  assert.deepEqual(sent.filter((message) => message.kind === 'input-status'), [
    { kind: 'input-status', requestId: 3, status: 'sent' },
  ])
  assert.deepEqual(
    sent.filter((message) => message.kind === 'reply-preview').map((message) => ({
      kind: message.kind,
      requestId: message.requestId,
      textLength: message.text.length,
      completed: message.completed,
    })),
    [
      { kind: 'reply-preview', requestId: 3, textLength: 6, completed: false },
      { kind: 'reply-preview', requestId: 3, textLength: 6, completed: true },
    ],
  )
})

test('drops reasoning, tool deltas and unmatched turns', () => {
  const sent = []
  const controller = new DialogueController(createDsh().context, (message) => sent.push(message))

  controller.observeEvent('s-1', { type: 'assistant/chunk', data: { turn: 9, chunk: { type: 'reasoning-delta', text: 'hidden' } } })
  controller.observeEvent('s-1', { type: 'assistant/chunk', data: { turn: 9, chunk: { type: 'tool-call-delta', text: 'hidden' } } })
  controller.observeEvent('s-1', { type: 'assistant/chunk', data: { turn: 9, chunk: { type: 'text-delta', text: 'hidden' } } })

  assert.equal(sent.length, 0)
})

test('uses a live agent first and marks running work as queued', async () => {
  const sent = []
  const followups = []
  const dsh = createDsh({ agent: { status: 'running', followup: (message) => followups.push(message) } })
  const controller = new DialogueController(dsh.context, (message) => sent.push(message))

  await controller.acceptInput({ requestId: 5, text: 'input omitted' })

  assert.equal(followups.length, 1)
  assert.deepEqual(sent, [{ kind: 'input-status', requestId: 5, status: 'queued' }])
})

test('resumes an unavailable live session before following up', async () => {
  const sent = []
  const followups = []
  const dsh = createDsh({ resumedAgent: { status: 'idle', followup: (message) => followups.push(message) } })
  const controller = new DialogueController(dsh.context, (message) => sent.push(message))

  await controller.acceptInput({ requestId: 6, text: 'input omitted' })

  assert.equal(followups.length, 1)
  assert.deepEqual(sent, [{ kind: 'input-status', requestId: 6, status: 'sent' }])
})

test('unwraps a DSH resumed-agent handle before following up', async () => {
  const sent = []
  const followups = []
  const resumedAgent = { status: 'idle', followup: (message) => followups.push(message) }
  const dsh = createDsh({ resume: async () => ({ agent: resumedAgent }) })
  const controller = new DialogueController(dsh.context, (message) => sent.push(message))

  await controller.acceptInput({ requestId: 7, text: 'input omitted' })

  assert.equal(followups.length, 1)
  assert.deepEqual(sent, [{ kind: 'input-status', requestId: 7, status: 'sent' }])
})

test('constructs pet follow-up messages without a context message factory', async () => {
  const sent = []
  const followups = []
  const controller = new DialogueController({
    settings: {
      get: () => ({ defaultSessionId: 's-1', previewEnabled: false, previewMaxChars: 80 }),
      update: () => {},
      watch: () => () => {},
    },
    agents: { get: () => ({ status: 'idle', followup: (message) => followups.push(message) }), resume: async () => undefined },
  }, (message) => sent.push(message))

  await controller.acceptInput({ requestId: 8, text: 'input omitted' })

  assert.equal(followups.length, 1)
  assert.equal(followups[0].role, 'user')
  assert.equal(followups[0].content[0].type, 'text')
  assert.equal(followups[0].content[0].text.length, 13)
  assert.deepEqual(followups[0].source, { kind: 'user' })
  assert.deepEqual(sent, [{ kind: 'input-status', requestId: 8, status: 'sent' }])
})

test('contains a missing settings snapshot as a fixed rejected input status', async () => {
  const sent = []
  const controller = new DialogueController({
    settings: { get: () => undefined, update: () => {}, watch: () => () => {} },
    agents: { get: () => undefined, resume: async () => undefined },
  }, (message) => sent.push(message))

  await controller.acceptInput({ requestId: 1, text: 'input omitted' })

  assert.deepEqual(sent, [{ kind: 'input-status', requestId: 1, status: 'rejected' }])
})

test('rejects input without a configured session and clears an unavailable session', async () => {
  const withoutDefault = createDsh({ settings: { defaultSessionId: null, previewEnabled: false, previewMaxChars: 80 } })
  const unavailable = createDsh({ resumeFails: true })
  const noDefaultSent = []
  const unavailableSent = []

  await new DialogueController(withoutDefault.context, (message) => noDefaultSent.push(message)).acceptInput({ requestId: 7, text: 'input omitted' })
  await new DialogueController(unavailable.context, (message) => unavailableSent.push(message)).acceptInput({ requestId: 8, text: 'input omitted' })

  assert.deepEqual(noDefaultSent, [{ kind: 'input-status', requestId: 7, status: 'no-default-session' }])
  assert.deepEqual(unavailable.writes, [{ defaultSessionId: null }])
  assert.deepEqual(unavailableSent, [{ kind: 'input-status', requestId: 8, status: 'session-unavailable' }])
})

test('clears previews on disabled settings, next input, session loss, helper close and disposal', async () => {
  const sent = []
  const dsh = createDsh({ agent: { status: 'idle', followup: () => {} } })
  const controller = new DialogueController(dsh.context, (message) => sent.push(message))

  await controller.acceptInput({ requestId: 10, text: 'input omitted' })
  await controller.acceptInput({ requestId: 11, text: 'input omitted' })
  controller.disablePreview()
  await controller.acceptInput({ requestId: 12, text: 'input omitted' })
  controller.sessionUnavailable('s-1')
  dsh.updateSettings({ defaultSessionId: 's-1', previewEnabled: true, previewMaxChars: 80 })
  await controller.acceptInput({ requestId: 13, text: 'input omitted' })
  controller.helperClosed()
  dsh.updateSettings({ defaultSessionId: 's-1', previewEnabled: true, previewMaxChars: 80 })
  await controller.acceptInput({ requestId: 14, text: 'input omitted' })
  controller.dispose()

  assert.deepEqual(
    sent.filter((message) => message.kind === 'clear-preview').map((message) => message.reason),
    ['next-input', 'disabled', 'session-unavailable', 'closed', 'cancelled'],
  )
})

test('keeps only the configured latest preview characters', async () => {
  const sent = []
  const followups = []
  const dsh = createDsh({ settings: { defaultSessionId: 's-1', previewEnabled: true, previewMaxChars: 80 }, agent: { status: 'idle', followup: (message) => followups.push(message) } })
  const controller = new DialogueController(dsh.context, (message) => sent.push(message))

  await controller.acceptInput({ requestId: 14, text: 'input omitted' })
  controller.observeEvent('s-1', { type: 'user/message', data: { id: followups[0].id } })
  controller.observeEvent('s-1', { type: 'turn/start', data: { turn: 14 } })
  controller.observeEvent('s-1', { type: 'assistant/chunk', data: { turn: 14, chunk: { type: 'text-delta', text: 'a'.repeat(80) } } })
  controller.observeEvent('s-1', { type: 'assistant/chunk', data: { turn: 14, chunk: { type: 'text-delta', text: 'b' } } })

  const preview = sent.at(-1)
  assert.equal(preview.kind, 'reply-preview')
  assert.equal(preview.text.length, 80)
  assert.equal(preview.text.endsWith('b'), true)
})

test('does not forward an already-associated turn after its session becomes unavailable', async () => {
  const sent = []
  const followups = []
  const dsh = createDsh({ agent: { status: 'idle', followup: (message) => followups.push(message) } })
  const controller = new DialogueController(dsh.context, (message) => sent.push(message))

  await controller.acceptInput({ requestId: 15, text: 'input omitted' })
  controller.observeEvent('s-1', { type: 'user/message', data: { id: followups[0].id } })
  controller.observeEvent('s-1', { type: 'turn/start', data: { turn: 15 } })
  controller.sessionUnavailable('s-1')
  controller.observeEvent('s-1', { type: 'assistant/chunk', data: { turn: 15, chunk: { type: 'text-delta', text: 'ignored' } } })

  assert.deepEqual(sent.filter((message) => message.kind === 'clear-preview').map((message) => message.reason), ['session-unavailable'])
  assert.equal(sent.filter((message) => message.kind === 'reply-preview').length, 0)
})

test('publishes settings changes immediately and crops or clears active previews', async () => {
  const sent = []
  const followups = []
  const dsh = createDsh({
    settings: { defaultSessionId: 's-1', previewEnabled: true, previewMaxChars: 100 },
    agent: { status: 'idle', followup: (message) => followups.push(message) },
  })
  const controller = new DialogueController(dsh.context, (message) => sent.push(message))

  await controller.acceptInput({ requestId: 16, text: 'input omitted' })
  controller.observeEvent('s-1', { type: 'user/message', data: { id: followups[0].id } })
  controller.observeEvent('s-1', { type: 'turn/start', data: { turn: 16 } })
  controller.observeEvent('s-1', { type: 'assistant/chunk', data: { turn: 16, chunk: { type: 'text-delta', text: 'a'.repeat(100) } } })
  dsh.updateSettings({ defaultSessionId: 's-1', previewEnabled: true, previewMaxChars: 80 })
  controller.settingsChanged()
  dsh.updateSettings({ defaultSessionId: 's-2', previewEnabled: true, previewMaxChars: 80 })
  controller.settingsChanged()

  const conversationConfigs = sent.filter((message) => message.kind === 'conversation-config')
  assert.deepEqual(conversationConfigs.map((message) => message.previewMaxChars), [80, 80])
  const previews = sent.filter((message) => message.kind === 'reply-preview')
  assert.equal(previews.at(-1).text.length, 80)
  assert.deepEqual(sent.filter((message) => message.kind === 'clear-preview').map((message) => message.reason), ['cancelled'])
})

test('disabling preview through settings clears active preview immediately', async () => {
  const sent = []
  const dsh = createDsh({ agent: { status: 'idle', followup: () => {} } })
  const controller = new DialogueController(dsh.context, (message) => sent.push(message))

  await controller.acceptInput({ requestId: 17, text: 'input omitted' })
  dsh.updateSettings({ defaultSessionId: 's-1', previewEnabled: false, previewMaxChars: 80 })
  controller.settingsChanged()

  assert.deepEqual(sent.filter((message) => message.kind === 'conversation-config').map((message) => message.previewEnabled), [false])
  assert.deepEqual(sent.filter((message) => message.kind === 'clear-preview').map((message) => message.reason), ['disabled'])
})

test('only follows up the newest input when earlier session resumes complete late', async () => {
  const sent = []
  const followups = []
  const resolvers = []
  const dsh = createDsh({
    resume: () => new Promise((resolve) => resolvers.push(resolve)),
  })
  const controller = new DialogueController(dsh.context, (message) => sent.push(message))

  const first = controller.acceptInput({ requestId: 18, text: 'input omitted' })
  const second = controller.acceptInput({ requestId: 19, text: 'input omitted' })
  const agent = { status: 'idle', followup: (message) => followups.push(message) }
  resolvers[0]({ agent })
  await first
  resolvers[1]({ agent })
  await second

  assert.equal(followups.length, 1)
  assert.deepEqual(sent.filter((message) => message.kind === 'input-status'), [
    { kind: 'input-status', requestId: 19, status: 'sent' },
  ])
})

test('does not follow up a resume that completes after its default session changes', async () => {
  const sent = []
  const followups = []
  let resolveResume
  const dsh = createDsh({
    resume: () => new Promise((resolve) => { resolveResume = resolve }),
  })
  const controller = new DialogueController(dsh.context, (message) => sent.push(message))

  const input = controller.acceptInput({ requestId: 20, text: 'input omitted' })
  dsh.updateSettings({ defaultSessionId: 's-2', previewEnabled: true, previewMaxChars: 80 })
  controller.settingsChanged()
  resolveResume({ agent: { status: 'idle', followup: (message) => followups.push(message) } })
  await input

  assert.equal(followups.length, 0)
  assert.equal(sent.filter((message) => message.kind === 'input-status').length, 0)
})
