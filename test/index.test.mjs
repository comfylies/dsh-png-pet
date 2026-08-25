import assert from 'node:assert/strict'
import test from 'node:test'

import { CompanionBridge } from '../lib/companion-bridge.js'
import { apply, createDialogueContext, registerSessionObservers, routeHelperMessage, watchDialogueSettings } from '../lib/index.js'

test('keeps prototype-backed DSH services reachable through the dialogue context adapter', () => {
  const agents = { get: () => undefined, resume: async () => undefined }
  const source = Object.create({
    agents,
    createUserMessage: () => ({ id: 'message-id' }),
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
  assert.deepEqual(dialogue.createUserMessage({ content: [{ type: 'text', text: 'input omitted' }], source: { kind: 'user' } }), { id: 'message-id' })
  assert.equal(accessed.includes('agents'), true)
  assert.equal(accessed.includes('createUserMessage'), true)
})

function createHostContext(calls, register) {
  const listeners = new Map()
  return {
    agents: { get: () => undefined, resume: async () => undefined },
    createUserMessage: () => ({ id: 'message-id' }),
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

  apply(context, () => helper)
  await Promise.resolve()

  assert.deepEqual(calls[0], ['inject', ['settings']])
  assert.deepEqual(calls[1][0], 'register')
  assert.equal(calls[1][1], 'dsh-png-pet')
  assert.equal(typeof calls[1][2], 'function')
  assert.equal(typeof calls[1][2].toJSON, 'function')
  assert.deepEqual(calls[1][2]({ defaultSessionId: null, previewEnabled: false, previewMaxChars: 480 }), {
    defaultSessionId: null,
    previewEnabled: false,
    previewMaxChars: 480,
  })
  assert.deepEqual(calls.slice(2, 4), ['watch', 'effect'])
  assert.equal(calls.includes('start'), true)
})

test('does not construct or start a helper when settings are not injected', () => {
  const calls = []
  const context = createHostContext(calls)

  assert.doesNotThrow(() => apply(context, () => {
    calls.push('helper-created')
    return { start: async () => {}, send: () => {}, stop: async () => {} }
  }))

  assert.deepEqual(calls, [['inject', ['settings']]])
})

test('routes a closed helper lifecycle message directly to preview cleanup', () => {
  const calls = []
  const controller = {
    acceptInput: () => calls.push('input'),
    helperClosed: () => calls.push('closed'),
  }

  routeHelperMessage({ version: 4, kind: 'closed' }, controller)

  assert.deepEqual(calls, ['closed'])
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
