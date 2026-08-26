import assert from 'node:assert/strict'
import { existsSync } from 'node:fs'
import test from 'node:test'

test('package contains the self-contained helper and placeholder asset', () => {
  assert.equal(existsSync('runtime/bin/win32-x64/pet-helper.exe'), true)
  assert.equal(existsSync('assets/placeholder-a.png'), true)
})

test('source includes a local animation manifest and default idle asset', () => {
  assert.equal(existsSync('pet-helper/Assets/pet-animations.json'), true)
  assert.equal(existsSync('pet-helper/Assets/placeholder-a.png'), true)
})
