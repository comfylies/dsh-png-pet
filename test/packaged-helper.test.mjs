import assert from 'node:assert/strict'
import { spawn } from 'node:child_process'
import { once } from 'node:events'
import { existsSync } from 'node:fs'
import { createInterface } from 'node:readline'
import test from 'node:test'
import { fileURLToPath } from 'node:url'

const helperPath = fileURLToPath(new URL('../runtime/bin/win32-x64/pet-helper.exe', import.meta.url))

test('published Helper renders a v18 question and resolves it without disconnecting', { timeout: 10000 }, async () => {
  const child = spawn(helperPath, [], { stdio: ['pipe', 'pipe', 'pipe'], windowsHide: true })
  const exited = once(child, 'exit'); const output = createInterface({ input: child.stdout })
  let ready
  const started = new Promise(resolve => { ready = resolve })
  output.on('line', line => { if (JSON.parse(line).kind === 'ready') ready() })
  try {
    await started
    child.stdin.write(JSON.stringify({ version: 18, kind: 'question-request', requestId: 1, answerable: true, questions: [{ id: 'q1', question: '测试题目', options: ['A', 'B'], multiSelect: false }] }) + '\n')
    await new Promise(resolve => setTimeout(resolve, 350))
    assert.equal(child.exitCode, null)
    child.stdin.write('{"version":18,"kind":"question-resolved","requestId":1,"outcome":"answered"}\n')
    await new Promise(resolve => setTimeout(resolve, 150))
    assert.equal(child.exitCode, null)
    child.stdin.write('{"version":18,"kind":"shutdown"}\n')
    assert.equal((await exited)[0], 0)
  } finally { output.close(); if (child.exitCode === null) child.kill() }
})

test('published Helper completes ready and shutdown handshakes', async () => {
  assert.equal(existsSync(helperPath), true)

  const child = spawn(helperPath, [], {
    stdio: ['pipe', 'pipe', 'pipe'],
    windowsHide: true,
  })
  let stderr = ''
  let shutdownSent = false
  child.stderr.setEncoding('utf8')
  child.stderr.on('data', (chunk) => { stderr += chunk })
  const exited = once(child, 'exit')

  try {
    await new Promise((resolve, reject) => {
      const timeout = setTimeout(() => reject(new Error(`published Helper did not complete its handshake: ${stderr}`)), 5_000)
      const output = createInterface({ input: child.stdout })
      output.on('line', (line) => {
        if (line === '{"version":18,"kind":"ready"}' && !shutdownSent) {
          shutdownSent = true
          child.stdin.write('{"version":18,"kind":"shutdown"}\n')
        }
        if (line === '{"version":18,"kind":"closed"}') {
          clearTimeout(timeout)
          resolve()
        }
      })
      child.once('error', (error) => {
        clearTimeout(timeout)
        reject(error)
      })
      child.once('exit', (code) => {
        clearTimeout(timeout)
        reject(new Error(`published Helper exited with ${code}: ${stderr}`))
      })
    })
    const [code] = await exited
    assert.equal(code, 0)
  } finally {
    if (child.exitCode === null) child.kill()
  }
})

test('published Helper remains alive after a valid outputting active state', async () => {
  const child = spawn(helperPath, [], {
    stdio: ['pipe', 'pipe', 'pipe'],
    windowsHide: true,
  })
  const exited = once(child, 'exit')
  let shutdownSent = false
  let stderr = ''
  child.stderr.setEncoding('utf8')
  child.stderr.on('data', (chunk) => { stderr += chunk })

  try {
    await new Promise((resolve, reject) => {
      const timeout = setTimeout(() => reject(new Error(`published Helper did not remain alive after a valid state: ${stderr}`)), 5_000)
      const output = createInterface({ input: child.stdout })
      output.on('line', (line) => {
        if (line === '{"version":18,"kind":"ready"}') {
          child.stdin.write('{"version":18,"kind":"hello"}\n')
          child.stdin.write('{"version":18,"kind":"config","scale":1,"reducedMotion":false,"physicsEnabled":false,"physicsBouncePercent":65,"petPlacement":"center","dialoguePlacement":"near-pet","dialogueWidth":320,"dialogueHeight":420,"dialogueFontSize":14,"randomChatEnabled":false,"randomChatBrowseOnOpen":false,"randomChatConfigured":false,"randomChatMinIntervalMinutes":8,"randomChatMaxIntervalMinutes":24,"randomChatCustomPrompts":[]}\n')
          child.stdin.write('{"version":18,"kind":"state","state":"active","activities":["thinking","working"],"label":"思考中/工作中","sequence":1}\n')
          child.stdin.write('{"version":18,"kind":"state","state":"question","activities":[],"label":"点击回到 Harness 回答","sequence":2}\n')
          setTimeout(() => {
            shutdownSent = true
            child.stdin.write('{"version":18,"kind":"shutdown"}\n')
          }, 250)
        }
        if (line === '{"version":18,"kind":"closed"}') {
          clearTimeout(timeout)
          resolve()
        }
      })
      child.once('error', reject)
      child.once('exit', (code) => {
        if (!shutdownSent) reject(new Error(`published Helper exited before shutdown with ${code}`))
      })
    })
    const [code] = await exited
    assert.equal(code, 0)
  } finally {
    if (child.exitCode === null) child.kill()
  }
})
