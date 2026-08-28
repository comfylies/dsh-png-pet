import assert from 'node:assert/strict'
import test from 'node:test'

import { TargetController } from '../lib/target-controller.js'

function createApi({ workspaces = [], sessions = [], archived = [], fail = {} } = {}) {
  const calls = { workspaceList: 0, sessionList: 0, workspaceCreate: [], sessionCreate: [] }
  return {
    calls,
    api: {
      workspace: {
        list: async () => {
          calls.workspaceList++
          if (fail.workspaceList) throw new Error('list failed')
          return { result: { ok: true, value: { items: workspaces, archivedSessionIds: archived } } }
        },
        create: async ({ payload }) => {
          calls.workspaceCreate.push(payload.path)
          if (fail.workspaceCreate) return { result: { ok: false, error: { message: 'create failed' } } }
          return { result: { ok: true, value: { workspace: { workspaceId: 'w-new', path: payload.path, title: 'new', sessionIds: [] }, created: true } } }
        },
      },
      sessions: {
        list: async () => {
          calls.sessionList++
          if (fail.sessionList) throw new Error('list failed')
          return { result: { ok: true, value: { items: sessions } } }
        },
        create: async ({ payload }) => {
          calls.sessionCreate.push(payload)
          // Mirror DSH: creating with a workspaceId attaches the new session to that workspace.
          if (payload.workspaceId !== undefined) {
            const workspace = workspaces.find((entry) => entry.workspaceId === payload.workspaceId)
            if (workspace !== undefined) workspace.sessionIds.push('s-created')
          }
          return { result: { ok: true, value: { sessionId: 's-created' } } }
        },
      },
    },
  }
}

function createScope(initial = { defaultSessionId: null, defaultWorkspaceId: null }) {
  let current = initial
  const writes = []
  return {
    scope: {
      get: () => current,
      update: async (next) => {
        current = { ...current, ...next }
        writes.push(next)
      },
      watch: () => () => {},
    },
    writes,
    current: () => current,
  }
}

test('open publishes grouped and ungrouped data with the configured defaults', async () => {
  const sent = []
  const harness = createApi({
    workspaces: [{ workspaceId: 'w-1', path: 'C:\\a', title: 'Alpha', sessionIds: ['s-1'] }],
    sessions: [{ sessionId: 's-1', updatedAt: 5, blank: false, cwd: 'C:\\a' }],
  })
  const settings = createScope({ defaultSessionId: 's-1', defaultWorkspaceId: 'w-1' })
  const controller = new TargetController(harness.api, settings.scope, (message) => sent.push(message))

  await controller.open({ version: 5, kind: 'target-open', requestId: 7 })

  assert.deepEqual(sent, [{
    kind: 'target-request',
    requestId: 7,
    workspaces: [{ id: 'w-1', title: 'Alpha', path: 'C:\\a' }],
    sessionsByWorkspace: { 'w-1': [{ id: 's-1', title: 'a', blank: false }] },
    ungrouped: [],
    defaultWorkspaceId: 'w-1',
    defaultSessionId: 's-1',
  }])
})

test('open publishes a retryable error state when a host call fails', async () => {
  const sent = []
  const harness = createApi({ fail: { sessionList: true } })
  const controller = new TargetController(harness.api, createScope().scope, (message) => sent.push(message))

  await controller.open({ version: 5, kind: 'target-open', requestId: 7 })

  assert.deepEqual(sent, [{
    kind: 'target-request',
    requestId: 7,
    workspaces: [],
    sessionsByWorkspace: {},
    ungrouped: [],
    defaultWorkspaceId: null,
    defaultSessionId: null,
    error: '数据加载失败，请重试',
  }])
})

test('picking a session persists it and derives the owning workspace', async () => {
  const sent = []
  const harness = createApi({
    workspaces: [{ workspaceId: 'w-1', path: 'C:\\a', title: 'Alpha', sessionIds: ['s-1'] }],
    sessions: [{ sessionId: 's-1', updatedAt: 5, blank: false, cwd: 'C:\\a' }],
  })
  const settings = createScope()
  const controller = new TargetController(harness.api, settings.scope, (message) => sent.push(message))
  await controller.open({ version: 5, kind: 'target-open', requestId: 7 })

  await controller.answer({ version: 5, kind: 'target-answer', requestId: 7, sessionId: 's-1', workspaceId: null, newBlank: false })

  assert.deepEqual(settings.current(), { defaultSessionId: 's-1', defaultWorkspaceId: 'w-1' })
})

test('new blank session inside a workspace is created and persisted with that workspace', async () => {
  const harness = createApi({
    workspaces: [{ workspaceId: 'w-1', path: 'C:\\a', title: 'Alpha', sessionIds: [] }],
    sessions: [],
  })
  const settings = createScope()
  const controller = new TargetController(harness.api, settings.scope, () => {})
  await controller.open({ version: 5, kind: 'target-open', requestId: 7 })

  await controller.answer({ version: 5, kind: 'target-answer', requestId: 7, sessionId: null, workspaceId: 'w-1', newBlank: true })

  assert.deepEqual(harness.calls.sessionCreate, [{ workspaceId: 'w-1' }])
  assert.deepEqual(settings.current(), { defaultSessionId: 's-created', defaultWorkspaceId: 'w-1' })
})

test('new blank session without a workspace falls back to the host cwd', async () => {
  const harness = createApi({ workspaces: [], sessions: [] })
  const settings = createScope()
  const controller = new TargetController(harness.api, settings.scope, () => {})
  await controller.open({ version: 5, kind: 'target-open', requestId: 7 })

  await controller.answer({ version: 5, kind: 'target-answer', requestId: 7, sessionId: null, workspaceId: null, newBlank: true })

  assert.deepEqual(harness.calls.sessionCreate, [{}])
  assert.deepEqual(settings.current(), { defaultSessionId: 's-created', defaultWorkspaceId: null })
})

test('workspace create registers the directory and re-publishes so the card can enter its second level', async () => {
  const sent = []
  const harness = createApi({ workspaces: [], sessions: [] })
  const controller = new TargetController(harness.api, createScope().scope, (message) => sent.push(message))
  await controller.open({ version: 5, kind: 'target-open', requestId: 7 })

  await controller.answer({ version: 5, kind: 'target-answer', requestId: 7, sessionId: null, workspaceId: null, newBlank: false, path: 'C:\\new', newWorkspace: true })

  assert.deepEqual(harness.calls.workspaceCreate, ['C:\\new'])
  assert.equal(sent.length, 2) // initial request + re-publish after create
  assert.equal(sent[1].kind, 'target-request')
  assert.equal(sent[1].requestId, 7)
})

test('a failing answer mirrors a retryable error state', async () => {
  const sent = []
  const harness = createApi({ workspaces: [], sessions: [], fail: { workspaceCreate: true } })
  const controller = new TargetController(harness.api, createScope().scope, (message) => sent.push(message))
  await controller.open({ version: 5, kind: 'target-open', requestId: 7 })

  await controller.answer({ version: 5, kind: 'target-answer', requestId: 7, sessionId: null, workspaceId: null, newBlank: false, path: 'C:\\new', newWorkspace: true })

  assert.equal(sent.at(-1)?.kind, 'target-request')
  assert.equal(sent.at(-1)?.error, '操作失败，请重试')
})
