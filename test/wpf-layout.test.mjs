import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import test from 'node:test'

test('names the pet image for runtime animation without a fixed source', () => {
  const xaml = readFileSync(new URL('../pet-helper/MainWindow.xaml', import.meta.url), 'utf8')

  assert.match(xaml, /<Image\s+x:Name="PetImage"\s+Stretch="Uniform"\s+VerticalAlignment="Bottom"/)
  assert.doesNotMatch(xaml, /<Image[^>]*Source="Assets\/placeholder-a\.png"/)
})

test('renders the state bubble above the pet image', () => {
  const xaml = readFileSync(new URL('../pet-helper/MainWindow.xaml', import.meta.url), 'utf8')

  assert.ok(xaml.indexOf('<Image') < xaml.indexOf('x:Name="StateBubble"'))
})

test('scales the pet window and its state bubble', () => {
  const code = readFileSync(new URL('../pet-helper/MainWindow.xaml.cs', import.meta.url), 'utf8')

  assert.match(code, /new\s+ScaleTransform\(state\.Scale,\s*state\.Scale\)/)
  assert.match(code, /StateBubble\.LayoutTransform\s*=\s*new\s+ScaleTransform/)
})

test('renders the white translucent dialogue window with merged input and output', () => {
  const xaml = readFileSync(new URL('../pet-helper/DialogueWindow.xaml', import.meta.url), 'utf8')

  assert.match(xaml, /Background="#F0FFFFFF"/)
  assert.match(xaml, /ResizeMode="CanResize"/)
  assert.match(xaml, /x:Name="InputTextBox"/)
  assert.match(xaml, /x:Name="ReplyTextBlock"/)
  assert.match(xaml, /HistoryButton/)
  assert.match(xaml, /x:Name="HistoryPanel"/)
})

test('styles scroll bars narrow without arrow buttons', () => {
  const xaml = readFileSync(new URL('../pet-helper/DialogueWindow.xaml', import.meta.url), 'utf8')

  assert.match(xaml, /ScrollBar/)
  assert.match(xaml, /Thumb/)
  assert.doesNotMatch(xaml, /RepeatButton/)
})

test('removes dialogue bubbles from the pet window and keeps the state bubble independent', () => {
  const xaml = readFileSync(new URL('../pet-helper/MainWindow.xaml', import.meta.url), 'utf8')

  assert.doesNotMatch(xaml, /InputBubble/)
  assert.doesNotMatch(xaml, /ReplyBubble/)
  assert.doesNotMatch(xaml, /HistoryPanel/)
  assert.doesNotMatch(xaml, /PreviewBubble/)
  assert.match(xaml, /x:Name="StateBubble"/)
})

test('keeps the state bubble visible regardless of other windows', () => {
  const code = readFileSync(new URL('../pet-helper/MainWindow.xaml.cs', import.meta.url), 'utf8')

  assert.match(code, /StateBubble\.Visibility\s*=\s*state\.State\s*==\s*"idle"/)
  assert.doesNotMatch(code, /InputBubble\.Visibility/)
  assert.doesNotMatch(code, /UpdateStateBubbleVisibility/)
})

test('links the dialogue window to double-click and pet dragging', () => {
  const code = readFileSync(new URL('../pet-helper/MainWindow.xaml.cs', import.meta.url), 'utf8')

  assert.match(code, /ToggleDialogueWindow\(\)/)
  assert.match(code, /ClickCount\s*==\s*2/)
  assert.match(code, /dialogueWindow\.Left\s*=\s*Left\s*\+\s*Width\s*\+\s*8/)
})

test('lets the dialogue window drag and remember its position', () => {
  const xaml = readFileSync(new URL('../pet-helper/DialogueWindow.xaml', import.meta.url), 'utf8')
  const code = readFileSync(new URL('../pet-helper/DialogueWindow.xaml.cs', import.meta.url), 'utf8')

  assert.match(code, /DragMove\(\)/)
  assert.match(code, /stateStore\.Save\(/)
  assert.match(code, /stateStore\.Load\(\)/)
  assert.match(xaml, /LocationChanged="DialogueWindow_LocationChanged"/)
})
