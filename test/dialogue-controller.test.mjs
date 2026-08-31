import assert from 'node:assert/strict'
import test from 'node:test'

import { DialogueController } from '../lib/dialogue-controller.js'

function createDsh({
  settings = { defaultSessionId: 's-1', defaultWorkspaceId: 'w-1', previewEnabled: true, previewMaxChars: 80 },
  agent,
  agents,
  resumedAgent,
  resumeFails = false,
  resume,
  attachments,
  sessionQuery,
  agentDefaultModel,
} = {}) {
  const writes = []
  const settingsListeners = new Set()
  const resumeCalls = []
  let currentSettings = { defaultWorkspaceId: null, ...settings }
  const context = {
    settings: {
      get: () => currentSettings,
      update: (next) => {
        const previous = currentSettings
        currentSettings = { defaultWorkspaceId: null, ...currentSettings, ...next }
        writes.push(next)
        for (const listener of settingsListeners) listener(next, previous)
      },
      watch: (listener) => {
        settingsListeners.add(listener)
        return () => settingsListeners.delete(listener)
      },
    },
    agents: {
      get: (id) => agents?.[id] ?? (id === 's-1' ? agent : undefined),
      resume: async (options) => {
        if (agents === undefined) assert.equal(options.resumeSessionId, 's-1')
        resumeCalls.push(options)
        if (resume !== undefined) return resume(options)
        if (resumeFails) throw new Error('unavailable')
        return resumedAgent === undefined ? undefined : { agent: resumedAgent }
      },
    },
    ...(attachments === undefined ? {} : { attachments }),
    ...(sessionQuery === undefined ? {} : { sessionQuery }),
    ...(agentDefaultModel === undefined ? {} : { agentDefaultModel }),
  }
  return {
    context,
    writes,
    resumeCalls,
    updateSettings(next) {
      const previous = currentSettings
      currentSettings = next
      for (const listener of settingsListeners) listener(next, previous)
    },
  }
}

test('resumes an unavailable live session with the deployment default model so prompt assembly succeeds', async () => {
  const sent = []
  const followups = []
  const dsh = createDsh({
    agentDefaultModel: { currentSelection: () => ({ provider: 'deepseek-official', model: 'deepseek-v4-flash' }) },
    resumedAgent: { status: 'idle', followup: (message) => followups.push(message) },
  })
  const controller = new DialogueController(dsh.context, (message) => sent.push(message))

  await controller.acceptInput({ requestId: 6, text: 'input omitted' })

  assert.equal(followups.length, 1)
  assert.deepEqual(dsh.resumeCalls, [{
    resumeSessionId: 's-1',
    agentOptions: { provider: 'deepseek-official', model: 'deepseek-v4-flash' },
  }])
  assert.deepEqual(sent, [{ kind: 'input-status', requestId: 6, status: 'sent' }])
})

test('resumes without agentOptions when no default model service is available', async () => {
  const sent = []
  const followups = []
  const dsh = createDsh({ resumedAgent: { status: 'idle', followup: (message) => followups.push(message) } })
  const controller = new DialogueController(dsh.context, (message) => sent.push(message))

  await controller.acceptInput({ requestId: 6, text: 'input omitted' })

  assert.equal(followups.length, 1)
  assert.deepEqual(dsh.resumeCalls, [{ resumeSessionId: 's-1' }])
})

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

test('rejects input without a configured session and reports an unavailable session', async () => {
  const withoutDefault = createDsh({ settings: { defaultSessionId: null, previewEnabled: false, previewMaxChars: 80 } })
  const unavailable = createDsh({ resumeFails: true })
  const noDefaultSent = []
  const unavailableSent = []

  await new DialogueController(withoutDefault.context, (message) => noDefaultSent.push(message)).acceptInput({ requestId: 7, text: 'input omitted' })
  await new DialogueController(unavailable.context, (message) => unavailableSent.push(message)).acceptInput({ requestId: 8, text: 'input omitted' })

  assert.deepEqual(noDefaultSent, [{ kind: 'input-status', requestId: 7, status: 'no-default-session' }])
  assert.deepEqual(unavailableSent, [{ kind: 'input-status', requestId: 8, status: 'session-unavailable' }])
})

test('keeps a persisted default session ahead of a right-click temporary target', async () => {
  const sent = []
  const defaultFollowups = []
  const temporaryFollowups = []
  const dsh = createDsh({
    settings: { defaultSessionId: 's-default', defaultWorkspaceId: 'w-default', previewEnabled: true, previewMaxChars: 80 },
    agents: {
      's-default': { status: 'idle', followup: (message) => defaultFollowups.push(message) },
      's-temporary': { status: 'idle', followup: (message) => temporaryFollowups.push(message) },
    },
  })
  const controller = new DialogueController(dsh.context, (message) => sent.push(message))

  controller.setTemporaryTarget('s-temporary', 'w-temporary')
  await controller.acceptInput({ requestId: 70, text: 'input omitted' })

  assert.equal(defaultFollowups.length, 1)
  assert.equal(temporaryFollowups.length, 0)
  assert.deepEqual(dsh.writes, [])
  assert.deepEqual(sent.filter((message) => message.kind === 'conversation-config').at(-1), {
    kind: 'conversation-config', previewEnabled: true, previewMaxChars: 80, defaultSessionId: 's-default', defaultWorkspaceId: 'w-default',
  })
})

test('uses a right-click temporary target only when no persisted default exists', async () => {
  const sent = []
  const temporaryFollowups = []
  const dsh = createDsh({
    settings: { defaultSessionId: null, defaultWorkspaceId: null, previewEnabled: true, previewMaxChars: 80 },
    agents: { 's-temporary': { status: 'idle', followup: (message) => temporaryFollowups.push(message) } },
  })
  const controller = new DialogueController(dsh.context, (message) => sent.push(message))

  controller.setTemporaryTarget('s-temporary', 'w-temporary')
  await controller.acceptInput({ requestId: 71, text: 'input omitted' })

  assert.equal(temporaryFollowups.length, 1)
  assert.deepEqual(dsh.writes, [])
  assert.deepEqual(sent.filter((message) => message.kind === 'conversation-config').at(-1), {
    kind: 'conversation-config', previewEnabled: true, previewMaxChars: 80, defaultSessionId: 's-temporary', defaultWorkspaceId: 'w-temporary',
  })
})

test('keeps the selected session after a resume failure so the user can choose another one', async () => {
  const sent = []
  const dsh = createDsh({ resumeFails: true })
  const controller = new DialogueController(dsh.context, (message) => sent.push(message))

  await controller.acceptInput({ requestId: 9, text: 'input omitted' })

  assert.deepEqual(dsh.writes, [])
  assert.deepEqual(sent, [{ kind: 'input-status', requestId: 9, status: 'session-unavailable' }])
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

test('publishes the final reply text after a turn ends', async () => {
  const sent = []
  const followups = []
  const events = [
    { type: 'user/message', data: { content: [{ type: 'text', text: 'hi' }], source: { kind: 'user' } } },
    { type: 'assistant/message', data: { turn: 4, message: { content: [{ type: 'reasoning', text: 'hidden' }, { type: 'text', text: '答复' }] } } },
  ]
  const dsh = createDsh({ agent: { status: 'idle', followup: (message) => followups.push(message), session: { events } } })
  const controller = new DialogueController(dsh.context, (message) => sent.push(message))

  await controller.acceptInput({ requestId: 3, text: 'hi' })
  controller.observeEvent('s-1', { type: 'user/message', data: { id: followups[0].id } })
  controller.observeEvent('s-1', { type: 'turn/start', data: { turn: 4 } })
  controller.observeEvent('s-1', { type: 'turn/end', data: { turn: 4 } })

  assert.deepEqual(sent.filter((message) => message.kind === 'reply'), [
    { kind: 'reply', requestId: 3, text: '答复', completed: true },
  ])
})

test('publishes the final reply even when the turn was never associated with an input', async () => {
  const sent = []
  const events = [
    { type: 'user/message', data: { content: [{ type: 'text', text: 'hi' }], source: { kind: 'user' } } },
    { type: 'assistant/message', data: { turn: 4, message: { content: [{ type: 'text', text: '答复' }] } } },
  ]
  const dsh = createDsh({ agent: { status: 'idle', followup: () => {}, session: { events } } })
  const controller = new DialogueController(dsh.context, (message) => sent.push(message))

  await controller.acceptInput({ requestId: 3, text: 'hi' })
  controller.observeEvent('s-1', { type: 'turn/end', data: { turn: 4 } })

  assert.deepEqual(sent.filter((message) => message.kind === 'reply'), [
    { kind: 'reply', requestId: 3, text: '答复', completed: true },
  ])
})

test('answers a history request with the extracted dialogue', async () => {
  const sent = []
  const events = [
    { type: 'user/message', data: { content: [{ type: 'text', text: 'hi' }], source: { kind: 'user' } } },
    { type: 'assistant/message', data: { message: { content: [{ type: 'text', text: 'hello' }] } } },
  ]
  const dsh = createDsh({ agent: { status: 'idle', followup: () => {}, session: { events } } })
  const controller = new DialogueController(dsh.context, (message) => sent.push(message))

  await controller.acceptInput({ requestId: 5, text: 'hi' })
  await controller.requestHistory(8)

  assert.deepEqual(sent.filter((message) => message.kind === 'conversation-history'), [
    {
      kind: 'conversation-history',
      requestId: 8,
      available: true,
      messages: [
        { role: 'user', blocks: [{ type: 'text', text: 'hi' }] },
        { role: 'assistant', blocks: [{ type: 'text', text: 'hello' }] },
      ],
    },
  ])
})

test('loads history through the session query service for a not-live session', async () => {
  const sent = []
  const events = [
    { type: 'user/message', data: { content: [{ type: 'text', text: '存档' }], source: { kind: 'user' } } },
  ]
  const dsh = createDsh({
    sessionQuery: { readSession: async (id) => ({ session: {}, events }) },
  })
  const controller = new DialogueController(dsh.context, (message) => sent.push(message))

  await controller.requestHistory(8)

  assert.deepEqual(sent.filter((message) => message.kind === 'conversation-history'), [
    {
      kind: 'conversation-history',
      requestId: 8,
      available: true,
      messages: [{ role: 'user', blocks: [{ type: 'text', text: '存档' }] }],
    },
  ])
})

test('reports an unavailable session in a history answer', async () => {
  const sent = []
  const dsh = createDsh({ resumeFails: true })
  const controller = new DialogueController(dsh.context, (message) => sent.push(message))

  await controller.requestHistory(8)

  assert.deepEqual(sent.filter((message) => message.kind === 'conversation-history'), [
    { kind: 'conversation-history', requestId: 8, available: false, messages: [] },
  ])
})

test('uploads images through the attachments service and echoes file paths as text', async () => {
  const sent = []
  const followups = []
  const saved = []
  const attachments = {
    saveImage: async (input) => {
      saved.push(input)
      return { attachmentId: `a-${saved.length}`, mediaType: input.mediaType, bytes: input.data.length, width: 100, height: 80 }
    },
  }
  const dsh = createDsh({
    attachments,
    agent: { status: 'idle', followup: (message) => followups.push(message) },
  })
  const controller = new DialogueController(dsh.context, (message) => sent.push(message))

  await controller.acceptInput({
    requestId: 21,
    text: '看看这个',
    attachments: [
      { type: 'image', mediaType: 'image/png', base64: Buffer.from([1, 2, 3]).toString('base64'), name: 'shot.png' },
      { type: 'file', path: 'C:\\docs\\notes.txt', name: 'notes.txt' },
    ],
  })

  assert.equal(followups.length, 1)
  const content = followups[0].content
  assert.equal(content[0].type, 'image')
  assert.equal(content[0].attachment.attachmentId, 'a-1')
  assert.equal(content[1].type, 'text')
  assert.match(content[1].text, /\[文件 notes\.txt\]/)
  assert.match(content[1].text, /C:\\docs\\notes\.txt/)
  assert.deepEqual(saved.map(({ mediaType, name }) => ({ mediaType, name })), [
    { mediaType: 'image/png', name: 'shot.png' },
  ])
  assert.deepEqual(sent.filter((message) => message.kind === 'input-status'), [
    { kind: 'input-status', requestId: 21, status: 'sent' },
  ])
})

test('rejects input whose image upload fails', async () => {
  const sent = []
  const followups = []
  const dsh = createDsh({
    attachments: { saveImage: async () => { throw new Error('admission rejected') } },
    agent: { status: 'idle', followup: (message) => followups.push(message) },
  })
  const controller = new DialogueController(dsh.context, (message) => sent.push(message))

  await controller.acceptInput({
    requestId: 22,
    text: '',
    attachments: [{ type: 'image', mediaType: 'image/jpeg', base64: 'AAAA' }],
  })

  assert.equal(followups.length, 0)
  assert.deepEqual(sent.filter((message) => message.kind === 'input-status'), [
    { kind: 'input-status', requestId: 22, status: 'rejected' },
  ])
})

test('accepts an image-only input with empty text', async () => {
  const sent = []
  const followups = []
  const dsh = createDsh({
    attachments: { saveImage: async () => ({ attachmentId: 'a-1', mediaType: 'image/png', bytes: 3, width: 10, height: 10 }) },
    agent: { status: 'idle', followup: (message) => followups.push(message) },
  })
  const controller = new DialogueController(dsh.context, (message) => sent.push(message))

  await controller.acceptInput({
    requestId: 23,
    text: '',
    attachments: [{ type: 'image', mediaType: 'image/png', base64: 'AAAA' }],
  })

  assert.equal(followups.length, 1)
  assert.deepEqual(followups[0].content.map((block) => block.type), ['image'])
})

test('stops a running agent turn with a user cancel', async () => {
  const sent = []
  const cancels = []
  const dsh = createDsh({ agent: { status: 'running', followup: () => {}, cancel: (cause) => cancels.push(cause) } })
  const controller = new DialogueController(dsh.context, (message) => sent.push(message))

  await controller.acceptInput({ requestId: 30, text: 'input omitted' })
  controller.stop(30)

  assert.deepEqual(cancels, [{ kind: 'user' }])
  assert.equal(sent.filter((message) => message.kind === 'input-status').length, 1)
})

test('finalizes a queued stop locally when the agent is idle', async () => {
  const sent = []
  const dsh = createDsh({ agent: { status: 'idle', followup: () => {} } })
  const controller = new DialogueController(dsh.context, (message) => sent.push(message))

  await controller.acceptInput({ requestId: 31, text: 'input omitted' })
  controller.stop(31)

  assert.deepEqual(sent.filter((message) => message.kind === 'input-status').map((message) => message.status), ['sent', 'stopped'])
})

test('maps aborted and interrupted turn endings to terminal statuses without a stale reply', async () => {
  const events = [
    { type: 'user/message', data: { content: [{ type: 'text', text: 'hi' }], source: { kind: 'user' } } },
    { type: 'assistant/message', data: { message: { content: [{ type: 'text', text: '之前完整回复' }] } } },
  ]
  const aborted = createDsh({ agent: { status: 'idle', followup: () => {}, session: { events } } })
  const interrupted = createDsh({ agent: { status: 'idle', followup: () => {}, session: { events } } })
  const abortedSent = []
  const interruptedSent = []
  const abortedController = new DialogueController(aborted.context, (message) => abortedSent.push(message))
  const interruptedController = new DialogueController(interrupted.context, (message) => interruptedSent.push(message))

  await abortedController.acceptInput({ requestId: 40, text: 'hi' })
  abortedController.observeEvent('s-1', { type: 'turn/end', data: { turn: 4, reason: { kind: 'aborted', reason: { kind: 'user' } } } })

  await interruptedController.acceptInput({ requestId: 41, text: 'hi' })
  interruptedController.observeEvent('s-1', { type: 'turn/end', data: { turn: 4, reason: { kind: 'interrupted' } } })

  assert.deepEqual(abortedSent.filter((message) => message.kind === 'input-status').map((message) => message.status), ['sent', 'stopped'])
  assert.deepEqual(interruptedSent.filter((message) => message.kind === 'input-status').map((message) => message.status), ['sent', 'interrupted'])
  assert.equal(abortedSent.filter((message) => message.kind === 'reply').length, 0)
  assert.equal(interruptedSent.filter((message) => message.kind === 'reply').length, 0)
})

test('maps failed turn endings to a failed status without publishing a stale reply', async () => {
  const events = [
    { type: 'user/message', data: { content: [{ type: 'text', text: 'hi' }], source: { kind: 'user' } } },
    { type: 'assistant/message', data: { message: { content: [{ type: 'text', text: '之前完整回复' }] } } },
  ]
  const dsh = createDsh({ agent: { status: 'idle', followup: () => {}, session: { events } } })
  const sent = []
  const controller = new DialogueController(dsh.context, (message) => sent.push(message))

  await controller.acceptInput({ requestId: 42, text: 'hi' })
  controller.observeEvent('s-1', { type: 'turn/end', data: { turn: 4, reason: { kind: 'error', error: { message: 'boom' } } } })

  assert.deepEqual(sent.filter((message) => message.kind === 'input-status').map((message) => message.status), ['sent', 'failed'])
  assert.equal(sent.filter((message) => message.kind === 'reply').length, 0)
})

test('publishes a tool-only turn as a placeholder reply', async () => {
  const sent = []
  const followups = []
  const events = [
    { type: 'user/message', data: { content: [{ type: 'text', text: 'hi' }], source: { kind: 'user' } } },
    { type: 'assistant/message', data: { turn: 4, message: { content: [{ type: 'tool-call', id: 'c1', name: 'bash', arguments: '{}' }] } } },
  ]
  const dsh = createDsh({ agent: { status: 'idle', followup: (message) => followups.push(message), session: { events } } })
  const controller = new DialogueController(dsh.context, (message) => sent.push(message))

  await controller.acceptInput({ requestId: 3, text: 'hi' })
  controller.observeEvent('s-1', { type: 'turn/end', data: { turn: 4 } })

  assert.deepEqual(sent.filter((message) => message.kind === 'reply'), [
    { kind: 'reply', requestId: 3, text: '调用了 bash', completed: true },
  ])
})

test('binds the final reply to the ended turn instead of a stale assistant message', async () => {
  const sent = []
  const followups = []
  const events = [
    { type: 'assistant/message', data: { turn: 2, message: { content: [{ type: 'text', text: '旧回复' }] } } },
    { type: 'user/message', data: { content: [{ type: 'text', text: 'hi' }], source: { kind: 'user' } } },
    { type: 'assistant/message', data: { turn: 4, message: { content: [{ type: 'text', text: '新回复' }] } } },
  ]
  const dsh = createDsh({ agent: { status: 'idle', followup: (message) => followups.push(message), session: { events } } })
  const controller = new DialogueController(dsh.context, (message) => sent.push(message))

  await controller.acceptInput({ requestId: 3, text: 'hi' })
  controller.observeEvent('s-1', { type: 'turn/end', data: { turn: 4 } })

  assert.deepEqual(sent.filter((message) => message.kind === 'reply'), [
    { kind: 'reply', requestId: 3, text: '新回复', completed: true },
  ])
})

test('publishes the partial preview before a stopped turn ending', async () => {
  const sent = []
  const followups = []
  const dsh = createDsh({ agent: { status: 'idle', followup: (message) => followups.push(message) } })
  const controller = new DialogueController(dsh.context, (message) => sent.push(message))

  await controller.acceptInput({ requestId: 43, text: 'hi' })
  controller.observeEvent('s-1', { type: 'user/message', data: { id: followups[0].id } })
  controller.observeEvent('s-1', { type: 'turn/start', data: { turn: 5 } })
  controller.observeEvent('s-1', { type: 'assistant/chunk', data: { turn: 5, chunk: { type: 'text-delta', text: '部分' } } })
  controller.observeEvent('s-1', { type: 'turn/end', data: { turn: 5, reason: { kind: 'aborted', reason: { kind: 'user' } } } })

  const previews = sent.filter((message) => message.kind === 'reply-preview')
  assert.deepEqual(previews.map(({ text, completed }) => ({ text, completed })), [
    { text: '部分', completed: false },
    { text: '部分', completed: true },
  ])
  assert.deepEqual(sent.filter((message) => message.kind === 'input-status').map((message) => message.status), ['sent', 'stopped'])
})

test('publishes the default session id with the conversation config', () => {
  const sent = []
  const dsh = createDsh()
  const controller = new DialogueController(dsh.context, (message) => sent.push(message))

  controller.publishConversationConfig()

  assert.deepEqual(sent.filter((message) => message.kind === 'conversation-config'), [
    { kind: 'conversation-config', previewEnabled: true, previewMaxChars: 80, defaultSessionId: 's-1', defaultWorkspaceId: 'w-1' },
  ])
})
