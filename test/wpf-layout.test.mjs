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

test('renders an opaque dialogue surface with a message list and composer', () => {
  const xaml = readFileSync(new URL('../pet-helper/DialogueWindow.xaml', import.meta.url), 'utf8')

  assert.match(xaml, /Background="#FFF8F9FC"/)
  assert.match(xaml, /ResizeMode="CanResize"/)
  assert.match(xaml, /x:Name="InputTextBox"/)
  assert.match(xaml, /x:Name="MessageList"/)
  assert.match(xaml, /x:Name="MessageScroll"/)
  assert.match(xaml, /x:Name="SendButton"/)
  assert.match(xaml, /x:Name="PendingAttachmentsList"/)
  assert.doesNotMatch(xaml, /HistoryButton/)
  assert.doesNotMatch(xaml, /x:Name="HistoryPanel"/)
  assert.doesNotMatch(xaml, /x:Name="ReplyTextBlock"/)
})

test('renders assistant markdown into a read-only rich text host', () => {
  const xaml = readFileSync(new URL('../pet-helper/DialogueWindow.xaml', import.meta.url), 'utf8')

  assert.match(xaml, /x:Name="MarkdownHost"/)
  assert.match(xaml, /IsReadOnly="True"/)
  assert.match(xaml, /IsDocumentEnabled="True"/)
  assert.match(xaml, /VirtualizingStackPanel/)
})

test('uses a Codex-style document flow with only user messages in bubbles', () => {
  const xaml = readFileSync(new URL('../pet-helper/DialogueWindow.xaml', import.meta.url), 'utf8')

  assert.match(xaml, /x:Key="UserBubbleTemplate"/)
  assert.match(xaml, /x:Key="AssistantTextTemplate"/)
  assert.match(xaml, /x:Key="AssistantMarkdownTemplate"/)
  assert.doesNotMatch(xaml, /x:Key="AssistantBubbleTemplate"/)
  assert.match(xaml, /Background="#FFF8F9FC"/)
})

test('uses a single translucent frosted composer rather than separate input controls', () => {
  const xaml = readFileSync(new URL('../pet-helper/DialogueWindow.xaml', import.meta.url), 'utf8')
  const code = readFileSync(new URL('../pet-helper/DialogueWindow.xaml.cs', import.meta.url), 'utf8')

  assert.match(xaml, /x:Name="InputComposer"/)
  assert.match(xaml, /Background="#D9FFFFFF"/)
  assert.match(xaml, /CornerRadius="18"/)
  assert.match(xaml, /Text="给智能体发消息"/)
  assert.match(xaml, /x:Name="SendButton"[\s\S]*Width="26"[\s\S]*Height="26"/)
  assert.match(xaml, /x:Name="SendGlyph"/)
  assert.match(xaml, /<Thumb Background="Transparent" \/>/)
  assert.match(xaml, /Visibility="Collapsed"/)
  assert.match(code, /SendGlyph\.Data/)
  assert.match(xaml, /x:Key="SubtleIconButton"/)
  assert.match(xaml, /Background" Value="#14000000"/)
  assert.match(xaml, /Content="＋"[\s\S]*Style="\{StaticResource SubtleIconButton\}"/)
})

test('accepts dropped and picked attachments in the dialogue window', () => {
  const xaml = readFileSync(new URL('../pet-helper/DialogueWindow.xaml', import.meta.url), 'utf8')
  const code = readFileSync(new URL('../pet-helper/DialogueWindow.xaml.cs', import.meta.url), 'utf8')

  assert.match(xaml, /AllowDrop="True"/)
  assert.match(code, /Window_Drop\(/)
  assert.match(code, /AddAttachments\(/)
  assert.match(code, /AttachmentButton_Click/)
  assert.match(code, /Convert\.ToBase64String\(bytes\)|System\.Convert\.FromBase64String/)
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
  // Native DragMove keeps the pet smooth; Ctrl is captured when the pointer gesture starts.
  assert.match(code, /DragMove\(\)/)
  assert.match(code, /pointerGesture\.Begin\([\s\S]*Keyboard\.Modifiers\s*&/)
  assert.match(code, /StartPetDrag\(useCombinedDrag\)/)
  assert.match(code, /WindowMover\.Move\(dialogueWindow/)
  assert.doesNotMatch(code, /dialogueWindow\.Left\s*=\s*Left\s*\+\s*Width\s*\+\s*8/)
})

test('shows a five-second peak valley card from a short left click', () => {
  const card = readFileSync(new URL('../pet-helper/PeakValleyCardWindow.xaml', import.meta.url), 'utf8')
  const cardCode = readFileSync(new URL('../pet-helper/PeakValleyCardWindow.xaml.cs', import.meta.url), 'utf8')
  const pet = readFileSync(new URL('../pet-helper/MainWindow.xaml', import.meta.url), 'utf8')
  const petCode = readFileSync(new URL('../pet-helper/MainWindow.xaml.cs', import.meta.url), 'utf8')

  assert.match(card, /ShowActivated="False"/)
  assert.match(card, /Content="×"/)
  assert.match(card, /AutomationProperties\.Name="关闭峰谷提示"/)
  assert.match(card, /Text="现在是："/)
  assert.match(card, /x:Name="PeriodLabel"\s+Grid\.Row="1"/)
  assert.match(card, /x:Key="PeakValleyCloseButton"/)
  assert.match(card, /IsMouseOver" Value="True"[\s\S]*#14000000/)
  assert.match(cardCode, /TimeSpan\.FromSeconds\(5\)/)
  assert.match(cardCode, /"梁文峰"/)
  assert.match(cardCode, /"梁文谷"/)
  assert.match(cardCode, /ZCOOL KuaiLe/)
  assert.match(pet, /MouseMove="Pet_MouseMove"/)
  assert.match(pet, /MouseLeftButtonUp="Pet_MouseLeftButtonUp"/)
  assert.match(petCode, /SystemParameters\.MinimumHorizontalDragDistance/)
  assert.match(petCode, /ShowPeakValleyCard\(\)/)
  assert.match(petCode, /PeakValleySchedule\.Current\(\)/)
  assert.match(petCode, /PetLayout\.CaptureMouse\(\)/)
  assert.match(petCode, /PetLayout\.ReleaseMouseCapture\(\)/)
  assert.match(petCode, /SystemInformation\.DoubleClickTime/)
  assert.match(petCode, /SchedulePeakValleyCard\(\)/)
  assert.match(petCode, /CancelPendingPeakValleyCard\(\)/)
  assert.doesNotMatch(petCode, /(?<!PetLayout\.)CaptureMouse\(\)/)
})

test('lets the dialogue window drag, resize, and remember its position', () => {
  const xaml = readFileSync(new URL('../pet-helper/DialogueWindow.xaml', import.meta.url), 'utf8')
  const code = readFileSync(new URL('../pet-helper/DialogueWindow.xaml.cs', import.meta.url), 'utf8')

  assert.match(code, /WindowMover\.BeginNativeResize/)
  assert.match(code, /stateStore\.Save\(/)
  assert.match(code, /stateStore\.Load\(\)/)
  // Native non-client resizing keeps west/north edges from fighting WPF layout.
  assert.match(code, /WindowResizeMath\.HitTest/)
  assert.match(code, /ShowDialogue\(/)
  assert.doesNotMatch(code, /CaptureMouse\(\)/)
  assert.doesNotMatch(code, /WindowMover\.MoveAndResize/)
  assert.doesNotMatch(xaml, /LocationChanged="DialogueWindow_LocationChanged"/)
  assert.match(xaml, /MinWidth="220"/)
  assert.match(xaml, /MaxHeight="900"/)
})

test('uses the dialogue visual language for the pet menu and collapsed target tree', () => {
  const pet = readFileSync(new URL('../pet-helper/MainWindow.xaml', import.meta.url), 'utf8')
  const target = readFileSync(new URL('../pet-helper/TargetWindow.xaml', import.meta.url), 'utf8')
  const code = readFileSync(new URL('../pet-helper/TargetWindow.xaml.cs', import.meta.url), 'utf8')

  assert.match(pet, /x:Key="PetContextMenu"/)
  assert.match(pet, /x:Key="PetContextMenuItem"/)
  assert.match(pet, /#14000000/)
  assert.match(target, /x:Name="WorkspaceTree"/)
  assert.match(target, /x:Key="WorkspaceAccordion"/)
  assert.match(target, /x:Name="UngroupedExpander"/)
  assert.match(target, /#14000000/)
  assert.match(code, /ShowTargetTree\(\)/)
  assert.doesNotMatch(code, /ShowLevelTwo\(/)
})
