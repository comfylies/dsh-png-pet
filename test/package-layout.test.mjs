import assert from 'node:assert/strict'
import { existsSync } from 'node:fs'
import test from 'node:test'

test('DSH bundle declares a compiled entrypoint', () => {
  assert.equal(existsSync(new URL('../cordis.patch.yml', import.meta.url)), true)
  assert.equal(existsSync(new URL('../lib/index.js', import.meta.url)), true)
})
