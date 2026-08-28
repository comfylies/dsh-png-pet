import assert from 'node:assert/strict'
import { readFile } from 'node:fs/promises'
import { createRequire } from 'node:module'
import test from 'node:test'
import vm from 'node:vm'
import { projectSessionOptions } from '../lib/client-settings-model.js'

const nodeRequire = createRequire(import.meta.url)

async function loadClientBundle(overrides = {}) {
  let registration
  const bundle = await readFile(new URL('../lib/client.js', import.meta.url), 'utf8')
  vm.runInNewContext(bundle, {
    window: {
      __ModuleLoader__: {
        load(candidate) {
          registration = candidate
        },
      },
    },
  })

  assert.equal(registration?.id, 'dsh-png-pet')
  return registration.factory((name) => overrides[name] ?? nodeRequire(name))
}

test('uses the DSH session display title and never reads cwd', () => {
  const accessed = []
  const row = new Proxy(
    { id: 's-1', displayTitle: '现有会话', cwd: 'C:\\private' },
    {
      get(target, key) {
        accessed.push(key)
        return target[key]
      },
    },
  )

  assert.deepEqual(projectSessionOptions([row]), [{ id: 's-1', title: '现有会话' }])
  assert.equal(accessed.includes('cwd'), false)
})

test('declares the built-in DSH settings transport injections', async () => {
  const client = await loadClientBundle()

  assert.deepEqual([...client.inject], ['sessions', 'settingsScope', 'connection', 'remote', 'slots'])
  assert.equal(typeof client.apply, 'function')
})

test('binds the standard DSH settings scope through ctx.get', async () => {
  const client = await loadClientBundle()
  const scope = {
    getSnapshot: () => ({ status: 'ready', value: undefined }),
    subscribe: () => () => {},
    set: async () => {},
  }
  const requested = []
  const ctx = {
    get: (service) => {
      requested.push(service)
      return { bind: ({ namespace }) => {
        assert.equal(namespace, 'dsh-png-pet')
        return scope
      } }
    },
  }

  assert.equal(client.bindDialogueSettings(ctx), scope)
  assert.deepEqual(requested, ['settingsScope'])
})

test('writes preview settings through the standard scope setter', async () => {
  const client = await loadClientBundle()
  const writes = []
  const scope = {
    set: async (field, value) => { writes.push([field, value]) },
  }

  assert.equal(await client.writeDialogueSetting(scope, 'previewEnabled', true), true)
  assert.equal(await client.writeDialogueSetting(scope, 'previewMaxChars', 80), true)
  assert.equal(await client.writeDialogueSetting(scope, 'previewMaxChars', 8000), true)
  assert.equal(await client.writeDialogueSetting(scope, 'previewMaxChars', 8001), false)
  assert.deepEqual(writes, [
    ['previewEnabled', true],
    ['previewMaxChars', 80],
    ['previewMaxChars', 8000],
  ])
})

test('preserves a fixed error path when the standard scope rejects a write', async () => {
  const client = await loadClientBundle()

  assert.equal(await client.writeDialogueSetting({ set: async () => { throw new Error('conflict') } }, 'previewEnabled', false), false)
})

test('shows the fixed unavailable-session error when the configured default disappears', async () => {
  const client = await loadClientBundle()
  const state = client.projectDialogueSettingsView(
    { ids: ['s-1'], byId: { 's-1': { id: 's-1', displayTitle: '现有会话' } } },
    { status: 'ready', value: { defaultSessionId: 'removed', previewEnabled: false, previewMaxChars: 480 } },
    false,
  )

  assert.equal(state.error, '所选会话已不可用。')
  assert.equal(state.selectedSessionUnavailable, true)
})

test('keeps fixed list and write errors distinct', async () => {
  const client = await loadClientBundle()

  assert.equal(
    client.projectDialogueSettingsView(
      { ids: undefined, byId: undefined },
      { status: 'loading', value: undefined },
      false,
    ).error,
    '会话列表尚未就绪。',
  )
  assert.equal(
    client.projectDialogueSettingsView(
      { ids: [], byId: {} },
      { status: 'ready', value: undefined },
      true,
    ).error,
    '无法保存桌宠设置。',
  )
})

test('registers the desktop-pet settings section', async () => {
  const client = await loadClientBundle()
  const registrations = []
  const ctx = {
    sessions: {
      list: {
        getSnapshot: () => ({ ids: [], byId: {} }),
        subscribe: () => () => {},
      },
    },
    get: (service) => {
      assert.equal(service, 'settingsScope')
      return { bind: () => ({
        getSnapshot: () => ({ status: 'ready', value: undefined }),
        subscribe: () => () => {},
        set: async () => {},
      }) }
    },
    slots: {
      inject: (_name, factory) => factory(),
      register: (options, component) => registrations.push({ options, component }),
    },
  }

  client.apply(ctx)

  assert.deepEqual(registrations.map(({ options }) => ({ ...options })), [{
    name: 'settings.section',
    id: 'dsh-png-pet',
    order: 100,
    label: '桌宠',
  }])
  assert.equal(typeof registrations[0].component, 'function')
})

test('keeps SettingsScope snapshot methods bound to the scope', async () => {
  const react = {
    createElement: (type, props, ...children) => ({ type, props: { ...props, children } }),
    useState: (value) => [value, () => {}],
    useSyncExternalStore: (subscribe, getSnapshot) => {
      const unsubscribe = subscribe(() => {})
      unsubscribe()
      return getSnapshot()
    },
  }
  const client = await loadClientBundle({ react })
  const registrations = []
  const scope = {
    store: { status: 'ready', value: undefined },
    getSnapshot() { return this.store },
    subscribe(listener) {
      this.store.listener = listener
      return () => { this.store.listener = undefined }
    },
    set: async () => {},
  }
  const ctx = {
    sessions: { list: { getSnapshot: () => ({ ids: [], byId: {} }), subscribe: () => () => {} } },
    get: () => ({ bind: () => scope }),
    slots: {
      inject: (_name, factory) => factory(),
      register: (_options, component) => registrations.push(component),
    },
  }

  client.apply(ctx)
  const element = registrations[0]()

  assert.doesNotThrow(() => element.type(element.props))
})
