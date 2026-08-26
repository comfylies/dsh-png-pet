import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import test from 'node:test'

test('names the pet image for runtime animation without a fixed source', () => {
  const xaml = readFileSync(new URL('../pet-helper/MainWindow.xaml', import.meta.url), 'utf8')

  assert.match(xaml, /<Image\s+x:Name="PetImage"\s+Stretch="Uniform"\s+VerticalAlignment="Bottom"\s*\/>/)
  assert.doesNotMatch(xaml, /<Image[^>]*Source="Assets\/placeholder-a\.png"/)
})

test('renders the state bubble above the pet image', () => {
  const xaml = readFileSync(new URL('../pet-helper/MainWindow.xaml', import.meta.url), 'utf8')

  assert.ok(xaml.indexOf('<Image') < xaml.indexOf('x:Name="StateBubble"'))
})

test('renders an input bubble with a bounded preview above the pet image', () => {
  const xaml = readFileSync(new URL('../pet-helper/MainWindow.xaml', import.meta.url), 'utf8')

  assert.match(xaml, /x:Name="InputBubble"/)
  assert.match(xaml, /x:Name="InputTextBox"/)
  assert.match(xaml, /x:Name="PreviewBubble"/)
  assert.match(xaml, /ScrollViewer[^>]*MaxHeight/)
  assert.ok(xaml.indexOf('x:Name="InputBubble"') < xaml.indexOf('<Image'))
})
