import assert from 'node:assert/strict'
import { existsSync, mkdtempSync, rmSync } from 'node:fs'
import { execFileSync } from 'node:child_process'
import { join } from 'node:path'
import { tmpdir } from 'node:os'
import test from 'node:test'

test('package contains the self-contained helper and placeholder asset', () => {
  assert.equal(existsSync('runtime/bin/win32-x64/pet-helper.exe'), true)
  assert.equal(existsSync('assets/placeholder-a.png'), true)
})

test('source includes a local animation manifest and default idle asset', () => {
  assert.equal(existsSync('pet-helper/Assets/pet-animations.json'), true)
  assert.equal(existsSync('pet-helper/Assets/placeholder-a.png'), true)
})

test('packed archive includes the helper executable and default idle asset', () => {
  const packageDirectory = mkdtempSync(join(tmpdir(), 'dsh-png-pet-package-'))
  const packageCache = join(packageDirectory, 'npm-cache')
  const npmCommand = process.platform === 'win32'
    ? ['powershell.exe', [
      '-NoProfile',
      '-Command',
      '& npm.cmd pack --json --pack-destination $args[0]',
      packageDirectory,
    ]]
    : ['npm', ['pack', '--json', '--pack-destination', packageDirectory]]

  try {
    const packed = JSON.parse(execFileSync(
      npmCommand[0],
      npmCommand[1],
      {
        cwd: process.cwd(),
        encoding: 'utf8',
        env: { ...process.env, npm_config_cache: packageCache },
      },
    ))
    assert.equal(existsSync(packageCache), true)
    const archiveEntries = execFileSync(
      'tar',
      ['-tzf', join(packageDirectory, packed[0].filename)],
      { encoding: 'utf8' },
    ).split(/\r?\n/)

    assert.ok(archiveEntries.includes('package/runtime/bin/win32-x64/pet-helper.exe'))
    assert.ok(archiveEntries.includes('package/assets/placeholder-a.png'))
  } finally {
    rmSync(packageDirectory, { recursive: true, force: true })
  }

  assert.equal(existsSync(packageDirectory), false)
})
