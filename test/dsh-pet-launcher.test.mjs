import assert from 'node:assert/strict'
import { existsSync, readFileSync } from 'node:fs'
import test from 'node:test'

const launcherPath = new URL('../启动 DSH 桌宠.vbs', import.meta.url)
const shortcutScriptPath = new URL('../scripts/new-dsh-pet-shortcut.ps1', import.meta.url)

test('ships a double-click launcher that starts the DSH web host without opening a browser', () => {
  assert.equal(existsSync(launcherPath), true)

  const launcher = readFileSync(launcherPath, 'utf8')
  assert.match(launcher, /Option Explicit/)
  assert.match(launcher, /dsh\.cmd/i)
  assert.match(launcher, /web --no-open/)
  assert.match(launcher, /shell\.Run[\s\S]*?,\s*0,\s*False/i)
})

test('provides an opt-in script to create a desktop shortcut without overwriting one', () => {
  assert.equal(existsSync(shortcutScriptPath), true)

  const shortcutScript = readFileSync(shortcutScriptPath, 'utf8')
  assert.match(shortcutScript, /CreateShortcut/)
  assert.match(shortcutScript, /wscript\.exe/i)
  assert.match(shortcutScript, /启动 DSH 桌宠\.vbs/)
  assert.match(shortcutScript, /Test-Path -LiteralPath \$Destination/)
  assert.match(shortcutScript, /throw/i)
})
