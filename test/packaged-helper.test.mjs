import assert from 'node:assert/strict'
import { spawn } from 'node:child_process'
import { existsSync } from 'node:fs'
import { createInterface } from 'node:readline'
import test from 'node:test'
import { fileURLToPath } from 'node:url'

const helperPath = fileURLToPath(new URL('../runtime/bin/win32-x64/pet-helper.exe', import.meta.url))

test('published Helper completes its ready handshake', async () => {
  assert.equal(existsSync(helperPath), true)

  const child = spawn(helperPath, [], {
    stdio: ['pipe', 'pipe', 'pipe'],
    windowsHide: true,
  })
  let stderr = ''
  child.stderr.setEncoding('utf8')
  child.stderr.on('data', (chunk) => { stderr += chunk })

  try {
    await new Promise((resolve, reject) => {
      const timeout = setTimeout(() => reject(new Error('published Helper did not send ready')), 5_000)
      const output = createInterface({ input: child.stdout })
      output.on('line', (line) => {
        if (line === '{"version":1,"kind":"ready"}') {
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
  } finally {
    if (!child.killed) child.kill()
  }
})
