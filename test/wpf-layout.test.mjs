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

test('scales every bubble with the window so the send button stays inside small windows', () => {
  const code = readFileSync(new URL('../pet-helper/MainWindow.xaml.cs', import.meta.url), 'utf8')

  assert.match(code, /new\s+ScaleTransform\(scale,\s*scale\)/)
  assert.match(code, /DialogueStack\.LayoutTransform\s*=\s*bubbleScale/)
  assert.match(code, /PreviewBubble\.LayoutTransform\s*=\s*bubbleScale/)
  assert.match(code, /StateBubble\.LayoutTransform\s*=\s*bubbleScale/)
  assert.match(code, /HistoryPanel\.LayoutTransform\s*=\s*bubbleScale/)
})

test('hides the state bubble while an input or preview bubble is open', () => {
  const code = readFileSync(new URL('../pet-helper/MainWindow.xaml.cs', import.meta.url), 'utf8')

  assert.match(code, /StateBubble\.Visibility\s*=\s*lastDisplayState\.State\s*==\s*"idle"/)
  assert.match(code, /InputBubble\.Visibility\s*==\s*Visibility\.Visible/)
  assert.match(code, /PreviewBubble\.Visibility\s*==\s*Visibility\.Visible/)
})

test('renders the history button, reply bubble and history overlay', () => {
  const xaml = readFileSync(new URL('../pet-helper/MainWindow.xaml', import.meta.url), 'utf8')
  const code = readFileSync(new URL('../pet-helper/MainWindow.xaml.cs', import.meta.url), 'utf8')

  assert.match(xaml, /HistoryButton/)
  assert.match(xaml, /x:Name="ReplyBubble"/)
  assert.match(xaml, /x:Name="HistoryPanel"/)
  assert.match(xaml, /x:Name="HistoryList"/)
  assert.match(code, /HistoryRequested/)
})

test('scales the dialogue stack with the window', () => {
  const xaml = readFileSync(new URL('../pet-helper/MainWindow.xaml', import.meta.url), 'utf8')
  const code = readFileSync(new URL('../pet-helper/MainWindow.xaml.cs', import.meta.url), 'utf8')

  assert.match(xaml, /x:Name="DialogueStack"/)
  assert.match(code, /DialogueStack\.LayoutTransform\s*=\s*bubbleScale/)
})
