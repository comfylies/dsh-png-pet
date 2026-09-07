import assert from 'node:assert/strict'
import { execFileSync } from 'node:child_process'
import { join } from 'node:path'
import test from 'node:test'

test('Windows installer behavior and release generation', { skip: process.platform !== 'win32' }, () => {
  const output = execFileSync('powershell.exe', ['-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', 'test/installer-tests.ps1', '-TestNode', process.execPath], {
    encoding: 'utf8', timeout: 60_000,
    env: { ...process.env, PSModulePath: join(process.env.SystemRoot, 'System32/WindowsPowerShell/v1.0/Modules') },
  })
  assert.match(output, /INSTALLER TESTS PASSED/)
})
