import assert from 'node:assert/strict'
import test from 'node:test'

import { dialogueSettingsDefaults, dialogueSettingsSchema } from '../lib/dialogue-settings.js'
import { projectSessionOptions } from '../lib/client-settings-model.js'
import { validateDialogueSettings } from '../lib/dialogue-settings.js'

const layoutDefaults = {
  scale: 1,
  reducedMotion: false,
  petPlacement: 'center',
  dialoguePlacement: 'near-pet',
  dialogueWidth: 320,
  dialogueHeight: 420,
}

const randomChatDefaults = {
  randomChatEnabled: false,
  randomChatBrowseOnOpen: false,
  randomChatWorkspaceIds: [],
  randomChatMinIntervalMinutes: 8,
  randomChatMaxIntervalMinutes: 24,
  randomChatCustomPrompts: [],
  randomChatTestNonce: 0,
}

test('uses the safe dialogue setting defaults', () => {
  assert.deepEqual(dialogueSettingsDefaults, {
    defaultSessionId: null,
    defaultWorkspaceId: null,
    previewEnabled: true,
    previewMaxChars: 2000,
    ...randomChatDefaults,
    ...layoutDefaults,
  })
})

test('includes safe defaults for the pet and dialogue layout', () => {
  assert.deepEqual(dialogueSettingsSchema({}), {
    defaultSessionId: null,
    defaultWorkspaceId: null,
    previewEnabled: true,
    previewMaxChars: 2000,
    ...randomChatDefaults,
    ...layoutDefaults,
  })
})

test('exposes the dialogue settings as a serializable schemastery schema', () => {
  assert.equal(typeof dialogueSettingsSchema, 'function')
  assert.equal(typeof dialogueSettingsSchema.toJSON, 'function')
  assert.deepEqual(dialogueSettingsSchema(undefined), {
    defaultSessionId: null,
    defaultWorkspaceId: null,
    previewEnabled: true,
    previewMaxChars: 2000,
    ...randomChatDefaults,
    ...layoutDefaults,
  })
  assert.deepEqual(dialogueSettingsSchema({}), {
    defaultSessionId: null,
    defaultWorkspaceId: null,
    previewEnabled: true,
    previewMaxChars: 2000,
    ...randomChatDefaults,
    ...layoutDefaults,
  })
  assert.deepEqual(
    dialogueSettingsSchema({ defaultSessionId: null, previewEnabled: false, previewMaxChars: 80 }),
    { defaultSessionId: null, defaultWorkspaceId: null, previewEnabled: false, previewMaxChars: 80, ...randomChatDefaults, ...layoutDefaults },
  )
  assert.throws(
    () => dialogueSettingsSchema({ defaultSessionId: null, previewEnabled: true, previewMaxChars: 80.5 }),
  )
  assert.throws(
    () => dialogueSettingsSchema({ defaultSessionId: '', previewEnabled: true, previewMaxChars: 80 }),
  )
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

test('accepts preview bounds and rejects values outside 80 through 8000', () => {
  assert.deepEqual(
    validateDialogueSettings({ defaultSessionId: 's-1', defaultWorkspaceId: 'w-1', previewEnabled: true, previewMaxChars: 480 }),
    { defaultSessionId: 's-1', defaultWorkspaceId: 'w-1', previewEnabled: true, previewMaxChars: 480, ...randomChatDefaults, ...layoutDefaults },
  )
  assert.deepEqual(
    validateDialogueSettings({ defaultSessionId: null, previewEnabled: false, previewMaxChars: 80 }),
    { defaultSessionId: null, defaultWorkspaceId: null, previewEnabled: false, previewMaxChars: 80, ...randomChatDefaults, ...layoutDefaults },
  )
  assert.deepEqual(
    validateDialogueSettings({ defaultSessionId: null, previewEnabled: false, previewMaxChars: 8000 }),
    { defaultSessionId: null, defaultWorkspaceId: null, previewEnabled: false, previewMaxChars: 8000, ...randomChatDefaults, ...layoutDefaults },
  )
  assert.throws(
    () => validateDialogueSettings({ defaultSessionId: null, previewEnabled: false, previewMaxChars: 79 }),
    /previewMaxChars/,
  )
  assert.throws(
    () => validateDialogueSettings({ defaultSessionId: null, previewEnabled: false, previewMaxChars: 8001 }),
    /previewMaxChars/,
  )
  assert.throws(
    () => validateDialogueSettings({ defaultSessionId: null, previewEnabled: false, previewMaxChars: 480.5 }),
    /previewMaxChars/,
  )
})

test('keeps random chat disabled until browsing consent and workspace choices are explicitly saved', () => {
  assert.deepEqual(
    validateDialogueSettings({
      randomChatEnabled: true,
      randomChatBrowseOnOpen: true,
      randomChatWorkspaceIds: ['w-1', 'w-2'],
      randomChatMinIntervalMinutes: 5,
      randomChatMaxIntervalMinutes: 60,
      randomChatCustomPrompts: ['要不要休息一分钟，和我聊聊？'],
      randomChatTestNonce: 0,
    }),
    {
      defaultSessionId: null,
      defaultWorkspaceId: null,
      previewEnabled: true,
      previewMaxChars: 2000,
      randomChatEnabled: true,
      randomChatBrowseOnOpen: true,
      randomChatWorkspaceIds: ['w-1', 'w-2'],
      randomChatMinIntervalMinutes: 5,
      randomChatMaxIntervalMinutes: 60,
      randomChatCustomPrompts: ['要不要休息一分钟，和我聊聊？'],
      randomChatTestNonce: 0,
      ...layoutDefaults,
    },
  )
  assert.throws(() => validateDialogueSettings({ randomChatWorkspaceIds: ['w-1', 'w-1'] }))
  assert.throws(() => validateDialogueSettings({ randomChatWorkspaceIds: Array.from({ length: 9 }, (_, index) => `w-${index}`) }))
  assert.throws(() => validateDialogueSettings({ randomChatMinIntervalMinutes: 4 }))
  assert.throws(() => validateDialogueSettings({ randomChatMinIntervalMinutes: 30, randomChatMaxIntervalMinutes: 29 }))
  assert.throws(() => validateDialogueSettings({ randomChatCustomPrompts: ['重复', '重复'] }))
  assert.throws(() => validateDialogueSettings({ randomChatCustomPrompts: ['x'.repeat(121)] }))
})
