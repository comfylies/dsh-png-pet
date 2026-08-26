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

test('wraps the web client in the DSH lazy-CJS module registration', async () => {
  const clientBundle = await readFile(new URL('../lib/client.js', import.meta.url), 'utf8')

  assert.match(clientBundle, /^window\.__ModuleLoader__\.load\(\{\s*id:\s*["']dsh-png-pet["']/)
  assert.match(clientBundle, /factory:\s*\(require\)\s*=>\s*\{[\s\S]*var module = \{ exports: \{\} \}/)
  assert.match(clientBundle, /return module\.exports;\s*}\s*}\);/)
  assert.match(clientBundle, /require\(["']react["']\)/)
  assert.doesNotMatch(clientBundle, /require\(["'](?:\.{1,2}\/|\/)/)
  assert.doesNotMatch(clientBundle, /^import\s/m)
})

test('declares React for the DSH client runtime without bundling it', async () => {
  const packageJson = JSON.parse(await readFile(new URL('../package.json', import.meta.url), 'utf8'))

  assert.equal(packageJson.peerDependencies.react, '^18.2.0')
  assert.equal(packageJson.devDependencies.react, '^18.3.1')
  assert.equal(packageJson.dependencies?.react, undefined)
})

test('uses the Harness settings ABI as an external peer instead of bundling a settings runtime', async () => {
  const packageJson = JSON.parse(await readFile(new URL('../package.json', import.meta.url), 'utf8'))

  assert.equal(packageJson.peerDependencies['@deepseek-ai/dsh-settings'], '^0.1.1-rc.2')
  assert.equal(packageJson.peerDependencies['@deepseek-ai/schemastery'], '^3.18.1')
  assert.equal(packageJson.devDependencies['@deepseek-ai/dsh-settings'], '^0.1.1-rc.2')
  assert.equal(packageJson.devDependencies['@deepseek-ai/schemastery'], '^3.18.1')
  assert.equal(packageJson.dependencies?.['@deepseek-ai/dsh-settings'], undefined)
  assert.equal(packageJson.dependencies?.['@deepseek-ai/schemastery'], undefined)
})

test('ships the LLM message factory required by the dialogue host', async () => {
  const packageJson = JSON.parse(await readFile(new URL('../package.json', import.meta.url), 'utf8'))

  assert.equal(packageJson.dependencies?.['@deepseek-ai/dsh-llm'], '^0.1.1-rc.2')
  assert.equal(packageJson.peerDependencies?.['@deepseek-ai/dsh-llm'], undefined)
})
