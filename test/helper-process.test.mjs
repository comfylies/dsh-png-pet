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

test('sends only typed v3 config and state messages', async () => {
  const lines = []
  const helper = new HelperProcess({
    command: process.execPath,
    args: [fixture],
    readyTimeoutMs: 1_000,
    shutdownTimeoutMs: 1_000,
    onSend: (line) => lines.push(line),
  })

  await helper.start()
  helper.send({ kind: 'config', scale: 1, reducedMotion: false })
  helper.send({ kind: 'state', state: 'idle', activities: [], label: '', sequence: 0 })
  await helper.stop()

  assert.deepEqual(lines.slice(0, 2), [
    '{"version":3,"kind":"config","scale":1,"reducedMotion":false}\n',
    '{"version":3,"kind":"state","state":"idle","activities":[],"label":"","sequence":0}\n',
  ])
})
