import assert from 'node:assert/strict'
import { existsSync } from 'node:fs'
import { readFile } from 'node:fs/promises'
import test from 'node:test'

test('DSH bundle declares a compiled entrypoint', () => {
  assert.equal(existsSync(new URL('../cordis.patch.yml', import.meta.url)), true)
  assert.equal(existsSync(new URL('../lib/index.js', import.meta.url)), true)
})

test('package layout exposes the compiled DSH web client entrypoint', async () => {
  const packageJson = JSON.parse(await readFile(new URL('../package.json', import.meta.url), 'utf8'))

  assert.equal(packageJson.exports['./client'], './lib/client.js')
  assert.equal(packageJson.exports['./package.json'], './package.json')
  assert.deepEqual(packageJson.dsh.client, {
    platform: 'web',
    inject: [
      '@deepseek-ai/dsh-client-ui-primitives',
      '@deepseek-ai/dsh-client-connection',
      '@deepseek-ai/dsh-api-remotes',
    ],
  })
  assert.equal(existsSync(new URL('../lib/client.js', import.meta.url)), true)
})

test('declares React for the DSH client runtime without bundling it', async () => {
  const packageJson = JSON.parse(await readFile(new URL('../package.json', import.meta.url), 'utf8'))

  assert.equal(packageJson.peerDependencies.react, '^18.2.0')
  assert.equal(packageJson.devDependencies.react, '^18.3.1')
  assert.equal(packageJson.dependencies?.react, undefined)
})
