import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import test from 'node:test'

test('names the pet image for runtime animation without a fixed source', () => {
  const xaml = readFileSync(new URL('../pet-helper/MainWindow.xaml', import.meta.url), 'utf8')

  assert.match(xaml, /<Image\s+x:Name="PetImage"\s+Stretch="Uniform"\s+VerticalAlignment="Bottom"/)
  assert.doesNotMatch(xaml, /<Image[^>]*Source="Assets\/placeholder-a\.png"/)
})

test('anchors the state bubble above the pet image instead of using a fixed margin', () => {
  const xaml = readFileSync(new URL('../pet-helper/MainWindow.xaml', import.meta.url), 'utf8')
  const code = readFileSync(new URL('../pet-helper/MainWindow.xaml.cs', import.meta.url), 'utf8')

  assert.ok(xaml.indexOf('<Image') < xaml.indexOf('x:Name="StateBubble"'))
  assert.match(xaml, /<Canvas\s+x:Name="StateBubbleCanvas"/)
  assert.doesNotMatch(xaml, /x:Name="StateBubble"[^>]*Margin=/)
  assert.match(code, /Canvas\.SetLeft\(StateBubble/)
  assert.match(code, /Canvas\.SetTop\(StateBubble/)
  assert.match(code, /animationPlayer\.StatusAnchor/)
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

test('links the dialogue window to double-click and Ctrl-combined pet dragging', () => {
  const code = readFileSync(new URL('../pet-helper/MainWindow.xaml.cs', import.meta.url), 'utf8')

  assert.match(code, /ToggleDialogueWindow\(\)/)
  assert.match(code, /ClickCount\s*==\s*2/)
  // Native DragMove keeps the pet smooth; Ctrl combines the dialogue by the applied delta.
  assert.match(code, /DragMove\(\)/)
  assert.match(code, /combinedDrag\s*=\s*\(Keyboard\.Modifiers\s*&/)
  assert.match(code, /WindowMover\.Move\(dialogueWindow/)
  assert.doesNotMatch(code, /dialogueWindow\.Left\s*=\s*Left\s*\+\s*Width\s*\+\s*8/)
})

test('lets the dialogue window drag, resize, and remember its position', () => {
  const xaml = readFileSync(new URL('../pet-helper/DialogueWindow.xaml', import.meta.url), 'utf8')
  const code = readFileSync(new URL('../pet-helper/DialogueWindow.xaml.cs', import.meta.url), 'utf8')

  assert.match(code, /CaptureMouse\(\)/)
  assert.match(code, /Mouse\.GetPosition\(null\)/)
  assert.match(code, /stateStore\.Save\(/)
  assert.match(code, /stateStore\.Load\(\)/)
  // Whole-window dragging, edge/corner resize, and wake placement replace per-move saves.
  assert.match(code, /WindowResizeMath\.HitTest/)
  assert.match(code, /ShowDialogue\(/)
  assert.doesNotMatch(xaml, /LocationChanged="DialogueWindow_LocationChanged"/)
})
