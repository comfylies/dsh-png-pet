import assert from 'node:assert/strict'
import test from 'node:test'

import { findBlankSession, projectTargetData, sessionTitle, workspaceOfSession } from '../lib/target-selection.js'

function summary(sessionId, { updatedAt = 1, blank = false, cwd = undefined, title } = {}) {
  return {
    sessionId,
    updatedAt,
    blank,
    ...(cwd === undefined ? {} : { cwd }),
    ...(title === undefined ? {} : { projections: { values: { title } } }),
  }
}

function workspace(workspaceId, path, title, sessionIds) {
  return { workspaceId, path, title, sessionIds }
}

test('groups sessions under their workspace in updatedAt descending order', () => {
  const workspaces = [
    workspace('w-1', 'C:\\a', 'Alpha', ['s-old', 's-new']),
    workspace('w-2', 'C:\\b', 'Beta', ['s-mid']),
  ]
  const sessions = [
    summary('s-new', { updatedAt: 30 }),
    summary('s-old', { updatedAt: 10 }),
    summary('s-mid', { updatedAt: 20 }),
  ]

  const data = projectTargetData(workspaces, sessions, [])

  assert.deepEqual(data.workspaces, [
    { id: 'w-1', title: 'Alpha', path: 'C:\\a' },
    { id: 'w-2', title: 'Beta', path: 'C:\\b' },
  ])
  assert.deepEqual(data.sessionsByWorkspace, {
    'w-1': [{ id: 's-new', title: '', blank: false }, { id: 's-old', title: '', blank: false }],
    'w-2': [{ id: 's-mid', title: '', blank: false }],
  })
  assert.deepEqual(data.ungrouped, [])
})

test('puts sessions owned by no workspace into the ungrouped bucket', () => {
  const data = projectTargetData(
    [workspace('w-1', 'C:\\a', 'Alpha', ['s-1'])],
    [summary('s-1'), summary('s-2', { updatedAt: 5 }), summary('s-3', { updatedAt: 9 })],
    [],
  )

  assert.deepEqual(data.ungrouped.map((session) => session.id), ['s-3', 's-2'])
  assert.deepEqual(data.sessionsByWorkspace['w-1'], [{ id: 's-1', title: '', blank: false }])
})

test('hides archived sessions from groups and the ungrouped bucket', () => {
  const data = projectTargetData(
    [workspace('w-1', 'C:\\a', 'Alpha', ['s-1', 's-archived'])],
    [summary('s-1'), summary('s-archived'), summary('s-loose', { updatedAt: 9 })],
    ['s-archived'],
  )

  assert.deepEqual(data.sessionsByWorkspace['w-1'], [{ id: 's-1', title: '', blank: false }])
  assert.deepEqual(data.ungrouped, [{ id: 's-loose', title: '', blank: false }])
})

test('derives a display title from the durable projection, then the cwd basename', () => {
  assert.equal(sessionTitle(summary('s-1', { title: '修复窗口' })), '修复窗口')
  assert.equal(sessionTitle(summary('s-2', { cwd: 'C:\\work\\pet-helper' })), 'pet-helper')
  assert.equal(sessionTitle(summary('s-3')), '')
  assert.equal(sessionTitle(summary('s-4', { cwd: 'C:\\' })), '')
  assert.equal(sessionTitle(summary('s-5', { title: 'x'.repeat(300) })).length, 200)
})

test('resolves session ownership from workspace accounting only', () => {
  const workspaces = [
    workspace('w-1', 'C:\\a', 'Alpha', ['s-1', 's-2']),
    workspace('w-2', 'C:\\b', 'Beta', ['s-3']),
  ]

  assert.equal(workspaceOfSession('s-2', workspaces), 'w-1')
  assert.equal(workspaceOfSession('s-3', workspaces), 'w-2')
  assert.equal(workspaceOfSession('s-unknown', workspaces), null)
})

test('reuses the accounted blank session whose cwd matches the workspace path', () => {
  const target = workspace('w-1', 'C:\\a', 'Alpha', ['s-blank'])
  const sessions = [
    summary('s-blank', { blank: true, cwd: 'C:\\a' }),
    summary('s-other', { blank: true, cwd: 'C:\\different' }),
    summary('s-archived', { blank: true, cwd: 'C:\\a' }),
  ]

  assert.equal(findBlankSession(target, sessions, [])?.sessionId, 's-blank')
  assert.equal(findBlankSession(target, sessions, ['s-blank']), undefined)
  assert.equal(
    findBlankSession(workspace('w-2', 'C:\\b', 'Beta', []), sessions, []),
    undefined,
  )
})
