import assert from 'node:assert/strict'
import test from 'node:test'

import { CompanionBridge } from '../lib/companion-bridge.js'
import { apply, applyWithHelper, createDialogueContext, inject, registerSessionObservers, routeHelperMessage, watchDialogueSettings } from '../lib/index.js'

test('keeps the production plugin entrypoint to one context argument', () => {
  assert.equal(apply.length, 1)
})

test('declares the services DSH exposes on the restricted plugin context', () => {
  assert.deepEqual(inject, ['agents', 'apiProxy', 'attachments', 'sessionQuery', 'agentDefaultModel'])
})

test('keeps prototype-backed DSH services reachable through the dialogue context adapter', () => {
  const agents = { get: () => undefined, resume: async () => undefined }
  const source = Object.create({
    agents,
  })
  const accessed = []
  const context = new Proxy(source, {
    get(target, key, receiver) {
      accessed.push(key)
      return Reflect.get(target, key, receiver)
    },
  })
  const settings = { get: () => ({ defaultSessionId: null, previewEnabled: false, previewMaxChars: 480 }) }

  const dialogue = createDialogueContext(context, settings)

  assert.equal(dialogue.agents, agents)
  assert.equal(accessed.includes('agents'), true)
})

function createHostContext(calls, register) {
  const listeners = new Map()
  return {
    agents: { get: () => undefined, resume: async () => undefined },
    apiProxy: {
      workspace: {
        list: async () => ({ result: { ok: true, value: { items: [], archivedSessionIds: [] } } }),
        create: async () => ({ result: { ok: true, value: { workspace: { workspaceId: 'w-1', path: 'C:\\x', title: 'x', sessionIds: [] }, created: true } } }),
      },
      sessions: {
        list: async () => ({ result: { ok: true, value: { items: [] } } }),
        create: async () => ({ result: { ok: true, value: { sessionId: 's-1' } } }),
      },
    },
    on(name, listener) { listeners.set(name, listener) },
    effect(factory) { calls.push('effect'); this.cleanup = factory() },
    inject(services, callback) {
      calls.push(['inject', services])
      if (register !== undefined) callback({ settings: { register } })
    },
  }
}

test('defers the helper until settings injection registers the dialogue scope and watcher', async () => {
  const calls = []
  const scope = {
    get: () => ({ defaultSessionId: null, previewEnabled: false, previewMaxChars: 480 }),
    update: () => {},
    watch: () => { calls.push('watch'); return () => calls.push('unwatch') },
  }
  const register = (namespace, schema) => {
    calls.push(['register', namespace, schema])
    return scope
  }
  const context = createHostContext(calls, register)
  const helper = {
    start: async () => { calls.push('start') },
    send: () => {},
    stop: async () => {},
  }

  applyWithHelper(context, () => helper)
  await Promise.resolve()

  assert.deepEqual(calls.slice(0, 3), ['start', 'effect', ['inject', ['settings']]])
  assert.deepEqual(calls[3][0], 'register')
  assert.equal(calls[3][1], 'dsh-png-pet')
  assert.equal(typeof calls[3][2], 'function')
  assert.equal(typeof calls[3][2].toJSON, 'function')
  assert.deepEqual(calls[3][2]({ defaultSessionId: null, defaultWorkspaceId: null, previewEnabled: false, previewMaxChars: 480 }), {
    defaultSessionId: null,
    defaultWorkspaceId: null,
    previewEnabled: false,
    previewMaxChars: 480,
  })
  assert.deepEqual(calls.slice(4), ['watch'])
  assert.equal(calls.includes('start'), true)
})

test('starts a helper even when settings are not injected', () => {
  const calls = []
  const context = createHostContext(calls)

  assert.doesNotThrow(() => applyWithHelper(context, () => {
    calls.push('helper-created')
    return { start: async () => {}, send: () => {}, stop: async () => {} }
  }))

  assert.deepEqual(calls, ['helper-created', 'effect', ['inject', ['settings']]])
})

test('routes a request-history helper message to the controller', () => {
  const calls = []
  const controller = {
    acceptInput: () => calls.push('input'),
    requestHistory: (requestId) => calls.push(['history', requestId]),
    helperClosed: () => calls.push('closed'),
  }

  routeHelperMessage({ version: 6, kind: 'request-history', requestId: 6 }, controller)

  assert.deepEqual(calls, [['history', 6]])
})

test('routes a stop helper message to the controller', () => {
  const calls = []
  const controller = {
    acceptInput: () => calls.push('input'),
    stop: (requestId) => calls.push(['stop', requestId]),
    helperClosed: () => calls.push('closed'),
  }

  routeHelperMessage({ version: 6, kind: 'stop', requestId: 12 }, controller)

  assert.deepEqual(calls, [['stop', 12]])
})

test('routes a closed helper lifecycle message directly to preview cleanup', () => {
  const calls = []
  const controller = {
    acceptInput: () => calls.push('input'),
    helperClosed: () => calls.push('closed'),
  }

  routeHelperMessage({ version: 6, kind: 'closed' }, controller)

  assert.deepEqual(calls, ['closed'])
})

test('routes target-open and target-answer helper messages to the target controller', async () => {
  const calls = []
  const targetController = {
    open: async (message) => calls.push(['open', message.requestId]),
    answer: async (message) => calls.push(['answer', message.kind, message.sessionId]),
  }

  await routeHelperMessage({ version: 6, kind: 'target-open', requestId: 9 }, undefined, targetController)
  await routeHelperMessage(
    { version: 6, kind: 'target-answer', requestId: 10, sessionId: 's-1', workspaceId: 'w-1', newBlank: false },
    undefined,
    targetController,
  )

  assert.deepEqual(calls, [['open', 9], ['answer', 'target-answer', 's-1']])
})

test('does not leave a rejected helper input promise unhandled', async () => {
  let unhandled = false
  const onUnhandledRejection = () => { unhandled = true }
  process.once('unhandledRejection', onUnhandledRejection)
  const controller = {
    acceptInput: async () => { throw new Error('expected') },
    helperClosed: () => {},
  }

  routeHelperMessage({ version: 6, kind: 'input', requestId: 1, text: 'input omitted' }, controller)
  await new Promise((resolve) => setImmediate(resolve))
  process.removeListener('unhandledRejection', onUnhandledRejection)

  assert.equal(unhandled, false)
})

test('uses the owner settings watch callback to apply committed settings in order', () => {
  let listener
  const calls = []
  const settings = {
    watch(next) {
      listener = next
      return () => calls.push('unwatched')
    },
  }
  const controller = {
    settingsChanged: (next, previous) => calls.push({ next, previous }),
  }
  const previous = { defaultSessionId: 's-1', previewEnabled: true, previewMaxChars: 480 }
  const next = { defaultSessionId: 's-2', previewEnabled: false, previewMaxChars: 80 }

  const unwatch = watchDialogueSettings(settings, controller)
  listener(next, previous)
  unwatch()

  assert.deepEqual(calls, [{ next, previous }, 'unwatched'])
})

test('observes dialogue events before state bridge events', () => {
  const listeners = new Map()
  const calls = []
  const context = { on(name, listener) { listeners.set(name, listener) } }
  const bridge = { apply: () => calls.push('bridge'), dispose: () => calls.push('bridge-disposed') }
  const controller = {
    observeEvent: () => calls.push('controller'),
    sessionUnavailable: () => calls.push('controller-disposed'),
  }

  registerSessionObservers(context, bridge, controller)
  listeners.get('session/event')({ id: 's-1', header: {} }, { type: 'turn/start', seq: 1, data: {} })
  listeners.get('session/disposed')({ id: 's-1' })

  assert.deepEqual(calls, ['controller', 'bridge', 'controller-disposed', 'bridge-disposed'])
})

test('registers both DSH observers and contains malformed event input', () => {
  const listeners = new Map()
  const sent = []
  const context = {
    on(name, listener) {
      listeners.set(name, listener)
    },
  }
  const bridge = new CompanionBridge((message) => sent.push(message))

  registerSessionObservers(context, bridge)

  assert.equal(listeners.size, 2)
  assert.doesNotThrow(() => listeners.get('session/event')({ id: 'root', header: {} }, { type: 'unknown', seq: 1 }))
  listeners.get('session/event')({ id: 'root', header: {} }, { type: 'approval/asked', seq: 2 })
  listeners.get('session/disposed')({ id: 'root' })

  assert.deepEqual(sent, [
    { kind: 'state', state: 'waiting', activities: [], label: '等待你的操作', sequence: 2 },
    { kind: 'state', state: 'idle', activities: [], label: '', sequence: 0 },
  ])
})
