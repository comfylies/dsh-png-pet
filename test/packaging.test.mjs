import assert from 'node:assert/strict'
import { spawn, execFileSync } from 'node:child_process'
import { once } from 'node:events'
import { existsSync, mkdtempSync, rmSync } from 'node:fs'
import { join } from 'node:path'
import { tmpdir } from 'node:os'
import { createInterface } from 'node:readline'
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
    const archivePath = join(packageDirectory, packed[0].filename)
    const archiveEntries = execFileSync(
      'tar',
      ['-tzf', archivePath],
      { encoding: 'utf8' },
    ).split(/\r?\n/)

    assert.ok(archiveEntries.includes('package/runtime/bin/win32-x64/pet-helper.exe'))
    assert.ok(archiveEntries.includes('package/assets/placeholder-a.png'))
    assert.ok(archiveEntries.includes('package/licenses/ZCOOL-KuaiLe-OFL.txt'))
    execFileSync('tar', ['-xzf', archivePath, '-C', packageDirectory])
    assert.equal(existsSync(join(packageDirectory, 'package', '启动 DSH 桌宠.vbs')), true)
  } finally {
    rmSync(packageDirectory, { recursive: true, force: true })
  }

  assert.equal(existsSync(packageDirectory), false)
})

test('packed Helper completes a ready, config, state, and shutdown handshake', async () => {
  const packageDirectory = mkdtempSync(join(tmpdir(), 'dsh-png-pet-package-'))
  const extractionDirectory = mkdtempSync(join(tmpdir(), 'dsh-png-pet-extracted-'))
  const packageCache = join(packageDirectory, 'npm-cache')
  const npmCommand = process.platform === 'win32'
    ? ['powershell.exe', [
      '-NoProfile',
      '-Command',
      '& npm.cmd pack --json --pack-destination $args[0]',
      packageDirectory,
    ]]
    : ['npm', ['pack', '--json', '--pack-destination', packageDirectory]]
  let child

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
    const archivePath = join(packageDirectory, packed[0].filename)
    execFileSync('tar', ['-xzf', archivePath, '-C', extractionDirectory])

    const packagedHelperPath = join(extractionDirectory, 'package', 'runtime', 'bin', 'win32-x64', 'pet-helper.exe')
    assert.equal(existsSync(packagedHelperPath), true)

    child = spawn(packagedHelperPath, [], {
      stdio: ['pipe', 'pipe', 'pipe'],
      windowsHide: true,
    })
    const exited = once(child, 'exit')
    let stderr = ''
    let shutdownSent = false
    child.stderr.setEncoding('utf8')
    child.stderr.on('data', (chunk) => { stderr += chunk })

    await new Promise((resolve, reject) => {
      const timeout = setTimeout(() => reject(new Error(`packed Helper did not complete its handshake: ${stderr}`)), 5_000)
      const output = createInterface({ input: child.stdout })
      output.on('line', (line) => {
        if (line === '{"version":7,"kind":"ready"}') {
          child.stdin.write('{"version":7,"kind":"config","scale":1,"reducedMotion":false,"petPlacement":"center","dialoguePlacement":"near-pet","dialogueWidth":320,"dialogueHeight":420}\n')
          child.stdin.write('{"version":7,"kind":"state","state":"active","activities":["thinking","working"],"label":"思考中/工作中","sequence":1}\n')
          child.stdin.write('{"version":7,"kind":"state","state":"question","activities":[],"label":"等你回答…","sequence":2}\n')
          setTimeout(() => {
            shutdownSent = true
            child.stdin.write('{"version":7,"kind":"shutdown"}\n')
          }, 250)
        }
        if (line === '{"version":7,"kind":"closed"}') {
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
        if (!shutdownSent) reject(new Error(`packed Helper exited before shutdown with ${code}: ${stderr}`))
      })
    })
    const [code] = await exited
    assert.equal(code, 0)
  } finally {
    if (child?.exitCode === null) {
      child.kill()
      await once(child, 'exit')
    }
    rmSync(packageDirectory, { recursive: true, force: true })
    rmSync(extractionDirectory, { recursive: true, force: true })
  }

  assert.equal(existsSync(packageDirectory), false)
  assert.equal(existsSync(extractionDirectory), false)
})
