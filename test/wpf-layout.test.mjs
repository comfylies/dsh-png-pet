import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import test from 'node:test'

test('renders the state bubble above the pet image', () => {
  const xaml = readFileSync(new URL('../pet-helper/MainWindow.xaml', import.meta.url), 'utf8')

  assert.ok(xaml.indexOf('<Image') < xaml.indexOf('x:Name="StateBubble"'))
})
