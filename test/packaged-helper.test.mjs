import assert from 'node:assert/strict'
import { spawn } from 'node:child_process'
import { once } from 'node:events'
import { existsSync } from 'node:fs'
import { createInterface } from 'node:readline'
import test from 'node:test'
import { fileURLToPath } from 'node:url'

const helperPath = fileURLToPath(new URL('../runtime/bin/win32-x64/pet-helper.exe', import.meta.url))

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
        if (line === '{"version":6,"kind":"ready"}' && !shutdownSent) {
          shutdownSent = true
          child.stdin.write('{"version":6,"kind":"shutdown"}\n')
        }
        if (line === '{"version":6,"kind":"closed"}') {
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
        if (line === '{"version":6,"kind":"ready"}') {
          child.stdin.write('{"version":6,"kind":"hello"}\n')
          child.stdin.write('{"version":6,"kind":"config","scale":1,"reducedMotion":false}\n')
          child.stdin.write('{"version":6,"kind":"state","state":"active","activities":["thinking","working"],"label":"思考中/工作中","sequence":1}\n')
          child.stdin.write('{"version":6,"kind":"state","state":"question","activities":[],"label":"等你回答…","sequence":2}\n')
          setTimeout(() => {
            shutdownSent = true
            child.stdin.write('{"version":6,"kind":"shutdown"}\n')
          }, 250)
        }
        if (line === '{"version":6,"kind":"closed"}') {
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
