import assert from 'node:assert/strict'
import test from 'node:test'

import { RandomChatController } from '../lib/random-chat-controller.js'

function enabledSettings() {
  return {
    randomChatEnabled: true,
    randomChatBrowseOnOpen: true,
    randomChatWorkspaceIds: ['w-1'],
  }
}

test('creates a target-workspace session only after a labelled invitation is clicked', async () => {
  const sent = []
  const calls = []
  const api = {
    workspace: {
      list: async () => ({
        result: { ok: true, value: { items: [{ workspaceId: 'w-1', title: '工作区', path: 'C:\\private', sessionIds: [] }], archivedSessionIds: [] } },
      }),
    },
    sessions: {
      create: async ({ payload }) => {
        calls.push(payload)
        return { result: { ok: true, value: { sessionId: 's-random' } } }
      },
    },
  }
  const dialogue = {
    setRandomChatTarget: (sessionId, workspaceId) => calls.push([sessionId, workspaceId]),
    startRandomChatTopic: async (requestId, topic) => calls.push([requestId, topic]),
    clearRandomChatTarget: () => calls.push('cleared'),
  }
  const controller = new RandomChatController(api, { get: enabledSettings }, dialogue, (message) => sent.push(message), (items) => items[0])

  await controller.open({ version: 14, kind: 'random-chat-open', invitationId: 9, topic: 'news' })

  assert.deepEqual(calls, [{ workspaceId: 'w-1' }, ['s-random', 'w-1'], [1_000_000_001, 'news']])
  assert.deepEqual(sent, [{ kind: 'random-chat-ready', invitationId: 9 }])
})

test('uses the same explicit-click path for a weather invitation', async () => {
  const calls = []
  const api = {
    workspace: { list: async () => ({ result: { ok: true, value: { items: [{ workspaceId: 'w-1', title: '工作区', path: 'C:\\private', sessionIds: [] }], archivedSessionIds: [] } } }) },
    sessions: { create: async () => ({ result: { ok: true, value: { sessionId: 's-weather' } } }) },
  }
  const dialogue = {
    setRandomChatTarget: (sessionId, workspaceId) => calls.push([sessionId, workspaceId]),
    startRandomChatTopic: async (requestId, topic) => calls.push([requestId, topic]),
    clearRandomChatTarget: () => {},
  }
  const controller = new RandomChatController(api, { get: enabledSettings }, dialogue, () => {}, (items) => items[0])

  await controller.open({ version: 14, kind: 'random-chat-open', invitationId: 10, topic: 'weather' })

  assert.deepEqual(calls, [['s-weather', 'w-1'], [1_000_000_001, 'weather']])
})

test('does not call the Host API when the user has not completed the explicit opt-in', async () => {
  const sent = []
  const api = { workspace: { list: async () => { throw new Error('must not load') } }, sessions: { create: async () => { throw new Error('must not create') } } }
  const dialogue = { setRandomChatTarget: () => {}, startRandomChatTopic: async () => {}, clearRandomChatTarget: () => {} }
  const controller = new RandomChatController(api, { get: () => ({ ...enabledSettings(), randomChatBrowseOnOpen: false }) }, dialogue, (message) => sent.push(message))

  await controller.open({ version: 14, kind: 'random-chat-open', invitationId: 4, topic: 'discovery' })

  assert.deepEqual(sent, [{ kind: 'random-chat-error', invitationId: 4, reason: 'not-configured' }])
})

test('clears the ephemeral random-chat target when its dialogue window closes', () => {
  let cleared = 0
  const controller = new RandomChatController({}, { get: enabledSettings }, { setRandomChatTarget: () => {}, startRandomChatTopic: async () => {}, clearRandomChatTarget: () => { cleared++ } }, () => {})
  controller.dialogueClosed()
  assert.equal(cleared, 1)
})
