import assert from 'node:assert/strict'
import { fileURLToPath } from 'node:url'
import test from 'node:test'

import { defaultHelperReadyTimeoutMs, HelperProcess, withRequiredWindowsEnvironment } from '../lib/helper-process.js'

const fixture = fileURLToPath(new URL('./fixtures/fake-helper.mjs', import.meta.url))

test('allows 15 seconds for the Helper ready handshake by default', () => {
  assert.equal(defaultHelperReadyTimeoutMs, 15_000)
})

test('adds WINDIR from SystemRoot only when the helper environment lacks it', () => {
  const environment = withRequiredWindowsEnvironment({ SystemRoot: 'C:\\Windows', KEEP: 'value' })

  assert.deepEqual(environment, { SystemRoot: 'C:\\Windows', WINDIR: 'C:\\Windows', KEEP: 'value' })
})

test('preserves an existing WINDIR and leaves environments without SystemRoot unchanged', () => {
  assert.deepEqual(
    withRequiredWindowsEnvironment({ SystemRoot: 'C:\\Windows', WINDIR: 'D:\\Windows' }),
    { SystemRoot: 'C:\\Windows', WINDIR: 'D:\\Windows' },
  )
  assert.deepEqual(withRequiredWindowsEnvironment({ KEEP: 'value' }), { KEEP: 'value' })
})

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

test('sends only typed v7 config and state messages', async () => {
  const lines = []
  const helper = new HelperProcess({
    command: process.execPath,
    args: [fixture],
    readyTimeoutMs: 1_000,
    shutdownTimeoutMs: 1_000,
    onSend: (line) => lines.push(line),
  })

  await helper.start()
  helper.send({ kind: 'config', scale: 1, reducedMotion: false, petPlacement: 'center', dialoguePlacement: 'near-pet', dialogueWidth: 320, dialogueHeight: 420 })
  helper.send({ kind: 'state', state: 'idle', activities: [], label: '', sequence: 0 })
  await helper.stop()

  assert.deepEqual(lines.slice(0, 2), [
    '{"version":7,"kind":"config","scale":1,"reducedMotion":false,"petPlacement":"center","dialoguePlacement":"near-pet","dialogueWidth":320,"dialogueHeight":420}\n',
    '{"version":7,"kind":"state","state":"idle","activities":[],"label":"","sequence":0}\n',
  ])
})

test('forwards only validated helper input messages to onMessage', async () => {
  const received = []
  let resolveMessage
  const messageReceived = new Promise((resolve) => {
    resolveMessage = resolve
  })
  const helper = new HelperProcess({
    command: process.execPath,
    args: [fixture, '--input'],
    readyTimeoutMs: 1_000,
    shutdownTimeoutMs: 1_000,
    onMessage: (message) => {
      received.push(message)
      resolveMessage()
    },
  })

  try {
    await helper.start()
    await messageReceived
    assert.deepEqual(received, [{ version: 7, kind: 'input', requestId: 9, text: 'hello' }])
  } finally {
    await helper.stop()
  }
})


test('forwards helper input request ids only once and in ascending order', async () => {
  const received = []
  let resolveSecondMessage
  const secondMessageReceived = new Promise((resolve) => {
    resolveSecondMessage = resolve
  })
  const helper = new HelperProcess({
    command: process.execPath,
    args: [fixture, '--out-of-order-inputs'],
    readyTimeoutMs: 1_000,
    shutdownTimeoutMs: 1_000,
    onMessage: (message) => {
      received.push(message)
      if (received.length === 2) resolveSecondMessage()
    },
  })

  try {
    await helper.start()
    await secondMessageReceived
    assert.deepEqual(
      received.map((message) => message.requestId),
      [9, 10],
    )
  } finally {
    await helper.stop()
  }
})

test('forwards a validated closed lifecycle message without an input body', async () => {
  const received = []
  const helper = new HelperProcess({
    command: process.execPath,
    args: [fixture],
    readyTimeoutMs: 1_000,
    shutdownTimeoutMs: 1_000,
    onMessage: (message) => received.push(message),
  })

  await helper.start()
  await helper.stop()

  assert.deepEqual(received, [{ version: 7, kind: 'closed' }])
})

test('retries an input callback that throws before advancing its request id', async () => {
  const requestIds = []
  const helper = new HelperProcess({
    command: process.execPath,
    args: [fixture, '--retry-input'],
    readyTimeoutMs: 1_000,
    shutdownTimeoutMs: 1_000,
    onMessage: (message) => {
      requestIds.push(message.requestId)
      if (requestIds.length === 1) throw new Error('expected callback failure')
    },
  })

  try {
    await helper.start()
    await new Promise((resolve) => setTimeout(resolve, 100))
    assert.deepEqual(requestIds, [11, 11, 12])
  } finally {
    await helper.stop()
  }
})

test('forwards history and input requests with independent request ids', async () => {
  const received = []
  let resolveSecond
  const secondReceived = new Promise((resolve) => {
    resolveSecond = resolve
  })
  const helper = new HelperProcess({
    command: process.execPath,
    args: [fixture, '--history-then-input'],
    readyTimeoutMs: 1_000,
    shutdownTimeoutMs: 1_000,
    onMessage: (message) => {
      received.push(message)
      if (received.length === 2) resolveSecond()
    },
  })

  try {
    await helper.start()
    await secondReceived
    assert.deepEqual(
      received.map((message) => [message.kind, message.requestId]),
      [['request-history', 1], ['input', 1]],
    )
  } finally {
    await helper.stop()
  }
})

test('forwards a validated stop message to onMessage', async () => {
  const received = []
  let resolveMessage
  const messageReceived = new Promise((resolve) => {
    resolveMessage = resolve
  })
  const helper = new HelperProcess({
    command: process.execPath,
    args: [fixture, '--stop'],
    readyTimeoutMs: 1_000,
    shutdownTimeoutMs: 1_000,
    onMessage: (message) => {
      received.push(message)
      resolveMessage()
    },
  })

  try {
    await helper.start()
    await messageReceived
    assert.deepEqual(received, [{ version: 7, kind: 'stop', requestId: 9 }])
  } finally {
    await helper.stop()
  }
})

test('forwards target-open and target-answer helper messages to onMessage', async () => {
  const received = []
  let resolveSecond
  const secondReceived = new Promise((resolve) => {
    resolveSecond = resolve
  })
  const helper = new HelperProcess({
    command: process.execPath,
    args: [fixture, '--target-open', '--target-answer'],
    readyTimeoutMs: 1_000,
    shutdownTimeoutMs: 1_000,
    onMessage: (message) => {
      received.push(message)
      if (received.length === 2) resolveSecond()
    },
  })

  try {
    await helper.start()
    await secondReceived
    assert.deepEqual(received, [
      { version: 7, kind: 'target-open', requestId: 21 },
      { version: 7, kind: 'target-answer', requestId: 22, sessionId: 's-1', workspaceId: 'w-1', newBlank: false },
    ])
  } finally {
    await helper.stop()
  }
})
