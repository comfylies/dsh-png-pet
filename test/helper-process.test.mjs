import assert from 'node:assert/strict'
import { fileURLToPath } from 'node:url'
import test from 'node:test'

import { HelperProcess } from '../lib/helper-process.js'

const fixture = fileURLToPath(new URL('./fixtures/fake-helper.mjs', import.meta.url))

test('starts a helper after a ready handshake and closes it gracefully', async () => {
  const helper = new HelperProcess({
    command: process.execPath,
    args: [fixture],
    readyTimeoutMs: 1_000,
    shutdownTimeoutMs: 1_000,
  })

  await helper.start()
  assert.equal(helper.isReady, true)

  await helper.stop()
  assert.equal(helper.exitCode, 0)
})
