import assert from 'node:assert/strict'
import { readFile } from 'node:fs/promises'
import { createRequire } from 'node:module'
import test from 'node:test'
import vm from 'node:vm'
import { projectSessionOptions, projectWorkspaceSessionTree } from '../lib/client-settings-model.js'

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

  assert.deepEqual([...client.inject], ['sessions', 'workspaces', 'settingsScope', 'connection', 'remote', 'slots'])
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

test('decodes the Host-resolved settings view after a transformed schema write', async () => {
  const client = await loadClientBundle()
  const value = client.decodeDialogueSettings({
    defaultSessionId: 's-1', defaultWorkspaceId: 'w-1', previewEnabled: true, previewMaxChars: 500,
    randomChatEnabled: true, randomChatBrowseOnOpen: true, randomChatWorkspaceIds: ['w-1'], randomChatMinIntervalMinutes: 5, randomChatMaxIntervalMinutes: 60, randomChatCustomPrompts: ['和我聊聊？'], randomChatTestNonce: 0,
    scale: 1, reducedMotion: false, petPlacement: 'top-right', dialoguePlacement: 'near-pet', dialogueWidth: 320, dialogueHeight: 420,
  })

  assert.equal(JSON.stringify(value), JSON.stringify({
    defaultSessionId: 's-1', defaultWorkspaceId: 'w-1', previewEnabled: true, previewMaxChars: 500,
    randomChatEnabled: true, randomChatBrowseOnOpen: true, randomChatWorkspaceIds: ['w-1'], randomChatMinIntervalMinutes: 5, randomChatMaxIntervalMinutes: 60, randomChatCustomPrompts: ['和我聊聊？'], randomChatTestNonce: 0,
    scale: 1, reducedMotion: false, petPlacement: 'top-right', dialoguePlacement: 'near-pet', dialogueWidth: 320, dialogueHeight: 420,
  }))
  assert.equal(client.decodeDialogueSettings({ defaultSessionId: 's-1' }), undefined)
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
  assert.equal(await client.writeDialogueSetting(scope, 'scale', 1.25), true)
  assert.equal(await client.writeDialogueSetting(scope, 'petPlacement', 'bottom-right'), true)
  assert.equal(await client.writeDialogueSetting(scope, 'dialoguePlacement', 'near-pet'), true)
  assert.equal(await client.writeDialogueSetting(scope, 'dialogueWidth', 220), true)
  assert.equal(await client.writeDialogueSetting(scope, 'dialogueHeight', 240), true)
  assert.equal(await client.writeDialogueSetting(scope, 'dialogueWidth', 219), false)
  assert.equal(await client.writeDialogueSetting(scope, 'randomChatWorkspaceIds', ['w-1', 'w-2']), true)
  assert.equal(await client.writeDialogueSetting(scope, 'randomChatWorkspaceIds', ['w-1', 'w-1']), false)
  assert.equal(await client.writeDialogueSetting(scope, 'randomChatMinIntervalMinutes', 5), true)
  assert.equal(await client.writeDialogueSetting(scope, 'randomChatMaxIntervalMinutes', 1440), true)
  assert.equal(await client.writeDialogueSetting(scope, 'randomChatMinIntervalMinutes', 4), false)
  assert.equal(await client.writeDialogueSetting(scope, 'randomChatCustomPrompts', ['和我聊聊？']), true)
  assert.equal(await client.writeDialogueSetting(scope, 'randomChatCustomPrompts', ['重复', '重复']), false)
  assert.equal(await client.writeDialogueSetting(scope, 'randomChatTestNonce', 1), true)
  assert.equal(await client.writeDialogueSetting(scope, 'randomChatTestNonce', -1), false)
  assert.deepEqual(writes, [
    ['previewEnabled', true],
    ['previewMaxChars', 80],
    ['previewMaxChars', 8000],
    ['scale', 1.25],
    ['petPlacement', 'bottom-right'],
    ['dialoguePlacement', 'near-pet'],
    ['dialogueWidth', 220],
    ['dialogueHeight', 240],
    ['randomChatWorkspaceIds', ['w-1', 'w-2']],
    ['randomChatMinIntervalMinutes', 5],
    ['randomChatMaxIntervalMinutes', 1440],
    ['randomChatCustomPrompts', ['和我聊聊？']],
    ['randomChatTestNonce', 1],
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
    { items: [], archivedSessionIds: [], baselinesReady: true },
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
      { items: undefined, archivedSessionIds: undefined, baselinesReady: undefined },
      { status: 'loading', value: undefined },
      false,
    ).error,
    '会话列表尚未就绪。',
  )
  assert.equal(
    client.projectDialogueSettingsView(
      { ids: [], byId: {} },
      { items: [], archivedSessionIds: [], baselinesReady: true },
      { status: 'ready', value: undefined },
      true,
    ).error,
    '无法保存桌宠设置。',
  )
})

test('does not disable session selection while the workspace baseline is still loading', async () => {
  const client = await loadClientBundle()
  const state = client.projectDialogueSettingsView(
    { ids: ['s-1'], byId: { 's-1': { id: 's-1', displayTitle: '现有会话' } } },
    { items: undefined, archivedSessionIds: undefined, baselinesReady: false },
    { status: 'ready', value: undefined },
    false,
  )

  assert.equal(state.listUnavailable, false)
  assert.equal(state.error, undefined)
  assert.equal(JSON.stringify(state.tree.ungrouped), JSON.stringify([{ id: 's-1', title: '现有会话' }]))
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
    workspaces: { list: { getSnapshot: () => ({ items: [], archivedSessionIds: [], baselinesReady: true }), subscribe: () => () => {} } },
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
    workspaces: { list: { getSnapshot: () => ({ items: [], archivedSessionIds: [], baselinesReady: true }), subscribe: () => () => {} } },
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

test('projects a two-level workspace tree without reading paths, cwd, or archived sessions', () => {
  const workspaceAccess = []
  const workspace = new Proxy(
    { workspaceId: 'w-1', title: '桌宠项目', sessionIds: ['s-1'], path: 'C:\\private' },
    { get(target, key) { workspaceAccess.push(key); return target[key] } },
  )
  const tree = projectWorkspaceSessionTree(
    [
      { id: 's-1', displayTitle: '工作区会话' },
      { id: 's-2', displayTitle: '未分组会话' },
      { id: 's-3', displayTitle: '已归档会话' },
    ],
    [workspace],
    ['s-3'],
  )

  assert.deepEqual(tree, {
    workspaces: [{ id: 'w-1', title: '桌宠项目', sessions: [{ id: 's-1', title: '工作区会话' }] }],
    ungrouped: [{ id: 's-2', title: '未分组会话' }],
  })
  assert.equal(workspaceAccess.includes('path'), false)
})
