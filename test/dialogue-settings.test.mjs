import assert from 'node:assert/strict'
import test from 'node:test'

import { dialogueSettingsDefaults } from '../lib/dialogue-settings.js'
import { projectSessionOptions, validateDialogueSettings } from '../lib/client-settings-model.js'

test('uses the safe dialogue setting defaults', () => {
  assert.deepEqual(dialogueSettingsDefaults, {
    defaultSessionId: null,
    previewEnabled: false,
    previewMaxChars: 480,
  })
})

test('projects only a session id and its DSH display title', () => {
  const accessed = []
  const row = new Proxy(
    { id: 's-1', displayTitle: '重构桌宠', cwd: 'C:\\private' },
    { get(target, key) { accessed.push(key); return target[key] } },
  )

  const options = projectSessionOptions([row])

  assert.deepEqual(options, [{ id: 's-1', title: '重构桌宠' }])
  assert.equal(accessed.includes('cwd'), false)
  assert.equal(JSON.stringify(options).includes('private'), false)
})

test('accepts preview bounds and rejects values outside 80 through 2000', () => {
  assert.deepEqual(
    validateDialogueSettings({ defaultSessionId: 's-1', previewEnabled: true, previewMaxChars: 480 }),
    { defaultSessionId: 's-1', previewEnabled: true, previewMaxChars: 480 },
  )
  assert.deepEqual(
    validateDialogueSettings({ defaultSessionId: null, previewEnabled: false, previewMaxChars: 80 }),
    { defaultSessionId: null, previewEnabled: false, previewMaxChars: 80 },
  )
  assert.deepEqual(
    validateDialogueSettings({ defaultSessionId: null, previewEnabled: false, previewMaxChars: 2000 }),
    { defaultSessionId: null, previewEnabled: false, previewMaxChars: 2000 },
  )
  assert.throws(
    () => validateDialogueSettings({ defaultSessionId: null, previewEnabled: false, previewMaxChars: 79 }),
    /previewMaxChars/,
  )
  assert.throws(
    () => validateDialogueSettings({ defaultSessionId: null, previewEnabled: false, previewMaxChars: 2001 }),
    /previewMaxChars/,
  )
  assert.throws(
    () => validateDialogueSettings({ defaultSessionId: null, previewEnabled: false, previewMaxChars: 480.5 }),
    /previewMaxChars/,
  )
})
