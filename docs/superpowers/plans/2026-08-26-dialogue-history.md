# 桌宠对话历史与最后回复显示 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 让桌宠气泡显示选定会话的最后一条回复（完整文本、可滚动、生成中有"正在生成回复…"占位），输入框顶部"历史"按钮展开最近 20 条对话历史；同时修复"发送后回复不显示"的缺陷。

**Architecture:** 插件端新增纯函数提取器 `dialogue-history.ts`，从 `agent.session.events` 提取脱敏对话（用户文本消息 + 助手最终 text，剔除 reasoning/工具），协议升级 v5（新增 `reply` / `request-history` / `conversation-history`，`conversation-config` 携带 `defaultSessionId`）。Helper 端输入框与回复气泡放进垂直堆叠容器，历史为全窗口覆盖层；沿用现有 LayoutTransform 缩放与气泡互斥机制。

**Tech Stack:** TypeScript、Node.js 内置 test、C# WPF、xunit、JSON Lines 协议 v5。

**Spec:** `docs/superpowers/specs/2026-08-26-dialogue-history-design.md`

---

## 文件结构

| 文件 | 责任 |
| --- | --- |
| `src/dialogue-history.ts` (新建) | 从 session 事件日志提取脱敏对话历史（纯函数） |
| `src/protocol.ts` | 协议 v5：`reply`/`request-history`/`conversation-history`、`conversation-config` 加 `defaultSessionId`、上限调整 |
| `src/dsh-dialogue-types.ts` | `DshAgent` 增加可选 `session.events` 读取 |
| `src/dialogue-controller.ts` | turn/end 发 `reply`、`requestHistory()`、会话切换带 `defaultSessionId` |
| `src/index.ts` | 路由 `request-history` 到控制器 |
| `src/helper-process.ts` | 转发 `request-history` 消息（与 `input` 同样处理） |
| `pet-helper/ProtocolMessage.cs` | v5 消息类（`ReplyMessage`、`HistoryMessage`、`HistoryItem`、`ConversationConfigMessage` 加字段） |
| `pet-helper/ProtocolReader.cs` | v5 解析与校验 |
| `pet-helper/App.xaml.cs` | v5 序列化、`HistoryRequested` 事件转发、新消息路由 |
| `pet-helper/ConversationState.cs` | `ReplyText`/`ReplyPending`/历史状态与会话切换清空 |
| `pet-helper/MainWindow.xaml` | 历史按钮、回复气泡、历史覆盖层 |
| `pet-helper/MainWindow.xaml.cs` | 渲染与互斥逻辑、`HistoryRequested` 事件 |
| `test/dialogue-history.test.mjs` (新建) | 提取器单元测试 |
| `test/protocol.test.mjs` | v5 协议校验 |
| `test/dialogue-controller.test.mjs` | 控制器触发逻辑 |
| `test/index.test.mjs` | 路由 |
| `test/helper-process.test.mjs` | 转发 |
| `pet-helper.Tests/ConversationStateTests.cs` | 回复/历史状态 |
| `pet-helper.Tests/ProtocolReaderTests.cs` | v5 解析 |
| `test/wpf-layout.test.mjs` | 布局静态断言 |
| `docs/项目需求书.md` | 更新对话显示边界描述 |

---

### Task 1: TS 对话历史提取器

**Files:**
- Create: `src/dialogue-history.ts`
- Test: `test/dialogue-history.test.mjs`

- [ ] **Step 1: 在 protocol.ts 添加历史常量**

在 `src/protocol.ts` 的 `PROTOCOL_VERSION` 定义后追加（版本号 Task 2 再升）：

```ts
export const HISTORY_LIMIT = 20
export const HISTORY_MESSAGE_MAX_CHARS = 2000
export const REPLY_MAX_CHARS = 8000
```

Run: `npm run build`
Expected: 构建通过（常量导出不影响现有代码）。

- [ ] **Step 2: 写失败测试**

创建 `test/dialogue-history.test.mjs`：

```js
import assert from 'node:assert/strict'
import test from 'node:test'

import { extractDialogueHistory } from '../lib/dialogue-history.js'

test('extracts only user text and final assistant text from the event log', () => {
  const events = [
    { type: 'user/message', seq: 1, data: { content: [{ type: 'text', text: '你好' }], source: { kind: 'user' } } },
    { type: 'assistant/message', seq: 2, data: { message: { content: [
      { type: 'reasoning', text: '隐藏的思考过程' },
      { type: 'text', text: '你好！' },
    ] } } },
    { type: 'tool/call', seq: 3, data: { name: 'bash', arguments: '秘密参数' } },
  ]

  assert.deepEqual(extractDialogueHistory(events), [
    { role: 'user', text: '你好' },
    { role: 'assistant', text: '你好！' },
  ])
})

test('skips tool-sourced user messages and empty text', () => {
  const events = [
    { type: 'user/message', seq: 1, data: { content: [{ type: 'text', text: '工具结果' }], source: { kind: 'tool', callId: 'c1' } } },
    { type: 'user/message', seq: 2, data: { content: [{ type: 'text', text: '真实用户' }], source: { kind: 'user' } } },
    { type: 'assistant/message', seq: 3, data: { message: { content: [{ type: 'text', text: '' }] } } },
  ]

  assert.deepEqual(extractDialogueHistory(events), [
    { role: 'user', text: '真实用户' },
  ])
})

test('keeps only the latest 20 messages and crops each text to 2000 chars', () => {
  const events = []
  for (let i = 0; i < 25; i++) {
    events.push({ type: 'user/message', seq: i, data: { content: [{ type: 'text', text: `m${i}` }], source: { kind: 'user' } } })
  }
  events.push({ type: 'assistant/message', seq: 25, data: { message: { content: [{ type: 'text', text: 'a'.repeat(3000) + 'tail' }] } } })

  const history = extractDialogueHistory(events)

  assert.equal(history.length, 20)
  assert.equal(history[0].role, 'user')
  assert.equal(history[0].text, 'm6')
  assert.equal(history[19].role, 'assistant')
  assert.equal(history[19].text.length, 2000)
  assert.equal(history[19].text.endsWith('tail'), true)
})

test('ignores malformed events without throwing', () => {
  const events = [null, 'junk', { type: 42 }, { type: 'user/message', data: { content: 'not-array' } }]

  assert.deepEqual(extractDialogueHistory(events), [])
})
```

- [ ] **Step 3: 运行测试确认失败**

Run: `npm run build; node --test --test-isolation=none test/dialogue-history.test.mjs`
Expected: FAIL，`ERR_MODULE_NOT_FOUND`（`../lib/dialogue-history.js` 不存在）。

- [ ] **Step 4: 实现提取器**

创建 `src/dialogue-history.ts`：

```ts
import { HISTORY_LIMIT, HISTORY_MESSAGE_MAX_CHARS, type HistoryMessage } from './protocol.js'

export function extractDialogueHistory(events: readonly unknown[]): HistoryMessage[] {
  const messages: HistoryMessage[] = []
  for (const raw of events) {
    const event = readRecord(raw)
    if (event === undefined) continue
    const type = event.type
    if (typeof type !== 'string') continue

    if (type === 'user/message') {
      const data = readRecord(event.data)
      const source = readRecord(data?.source)
      if (source?.kind !== 'user') continue
      const text = readTextBlocks(data?.content)
      if (text === '') continue
      messages.push({ role: 'user', text: retainTail(text, HISTORY_MESSAGE_MAX_CHARS) })
    } else if (type === 'assistant/message') {
      const data = readRecord(event.data)
      const message = readRecord(data?.message)
      const text = readTextBlocks(message?.content)
      if (text === '') continue
      messages.push({ role: 'assistant', text: retainTail(text, HISTORY_MESSAGE_MAX_CHARS) })
    }
  }
  return messages.slice(-HISTORY_LIMIT)
}

function readTextBlocks(content: unknown): string {
  if (!Array.isArray(content)) return ''
  let text = ''
  for (const block of content) {
    const record = readRecord(block)
    if (record?.type !== 'text' || typeof record.text !== 'string') continue
    text += record.text
  }
  return text
}

function readRecord(value: unknown): Record<string, unknown> | undefined {
  return value !== null && typeof value === 'object' && !Array.isArray(value)
    ? value as Record<string, unknown>
    : undefined
}

function retainTail(value: string, maxChars: number): string {
  return value.length <= maxChars ? value : value.slice(-maxChars)
}
```

- [ ] **Step 5: 运行测试确认通过**

Run: `npm run build; node --test --test-isolation=none test/dialogue-history.test.mjs`
Expected: 4 个测试全部 PASS。

- [ ] **Step 6: 提交**

```bash
git add src/dialogue-history.ts test/dialogue-history.test.mjs
git commit -m "feat: extract dialogue history from session events"
```

---

### Task 2: TS 协议 v5

**Files:**
- Modify: `src/protocol.ts`
- Test: `test/protocol.test.mjs`

- [ ] **Step 1: 更新协议常量与类型**

在 `src/protocol.ts` 中（`HISTORY_LIMIT` 等常量已在 Task 1 添加，此处只升版本号）：

```ts
export const PROTOCOL_VERSION = 5 as const
```

替换 `maxLineLength` 定义：

```ts
const maxLineLength = 65_536
```

替换消息类型定义（原 `HelperInputMessage` 后新增）：

```ts
export type HelperLifecycleMessageKind = 'ready' | 'closed'
export type HelperMessageKind = HelperLifecycleMessageKind | 'input' | 'request-history'
export type HistoryRole = 'user' | 'assistant'
export type HistoryMessageEntry = { role: HistoryRole, text: string }
export type InputStatus = 'queued' | 'sent' | 'no-default-session' | 'session-unavailable' | 'rejected'
export type ClearPreviewReason = 'disabled' | 'next-input' | 'cancelled' | 'closed' | 'session-unavailable'
export type HostMessageKind = 'hello' | 'config' | 'state' | 'shutdown' | 'conversation-config' | 'input-status' | 'reply-preview' | 'clear-preview' | 'reply' | 'conversation-history'

export type HelperInputMessage = {
  version: typeof PROTOCOL_VERSION
  kind: 'input'
  requestId: number
  text: string
}

export type HelperHistoryRequest = {
  version: typeof PROTOCOL_VERSION
  kind: 'request-history'
  requestId: number
}

export type HelperMessage = HelperLifecycleMessage | HelperInputMessage | HelperHistoryRequest

export type HostMessage =
  | { version: typeof PROTOCOL_VERSION, kind: 'hello' | 'shutdown' }
  | { version: typeof PROTOCOL_VERSION, kind: 'config', scale: 0.75 | 1 | 1.25 | 1.5, reducedMotion: boolean }
  | { version: typeof PROTOCOL_VERSION, kind: 'state', state: State, activities: readonly Activity[], label: string, sequence: number }
  | { version: typeof PROTOCOL_VERSION, kind: 'conversation-config', previewEnabled: boolean, previewMaxChars: number, defaultSessionId: string | null }
  | { version: typeof PROTOCOL_VERSION, kind: 'input-status', requestId: number, status: InputStatus }
  | { version: typeof PROTOCOL_VERSION, kind: 'reply-preview', requestId: number, text: string, completed: boolean }
  | { version: typeof PROTOCOL_VERSION, kind: 'clear-preview', requestId: number, reason: ClearPreviewReason }
  | { version: typeof PROTOCOL_VERSION, kind: 'reply', requestId: number, text: string, completed: boolean }
  | { version: typeof PROTOCOL_VERSION, kind: 'conversation-history', requestId: number, available: boolean, messages: readonly HistoryMessageEntry[] }
```

同步更新 `HostOutboundMessage`（同样 9 种 kind，去掉 version 字段）。更新 `hostKinds` 集合与 `inputStatuses`/`clearPreviewReasons` 保持原样：

```ts
const hostKinds = new Set<HostMessageKind>(['hello', 'config', 'state', 'shutdown', 'conversation-config', 'input-status', 'reply-preview', 'clear-preview', 'reply', 'conversation-history'])
```

- [ ] **Step 2: 更新解析与校验函数**

在 `parseHelperMessage` 中，把 `request-history` 分支加在 `input` 分支前：

```ts
  if (message.kind === 'ready' || message.kind === 'closed') {
    assertExactKeys(message, ['version', 'kind'], 'helper message')
    return { version: PROTOCOL_VERSION, kind: message.kind }
  }
  if (message.kind === 'request-history') {
    assertExactKeys(message, ['version', 'kind', 'requestId'], 'helper message')
    assertPositiveSafeInteger(message.requestId, 'helper message requestId')
    return { version: PROTOCOL_VERSION, kind: 'request-history', requestId: message.requestId }
  }

  assertExactKeys(message, ['version', 'kind', 'requestId', 'text'], 'helper message')
```

`isHelperMessageKind` 更新：

```ts
function isHelperMessageKind(value: string): value is HelperMessageKind {
  return value === 'input' || value === 'request-history' || helperLifecycleKinds.has(value as HelperLifecycleMessageKind)
}
```

在 `validateHostMessage` 的 `conversation-config` 分支更新：

```ts
    case 'conversation-config':
      assertExactKeys(value, ['version', 'kind', 'previewEnabled', 'previewMaxChars', 'defaultSessionId'], 'host message', ['kind', 'previewEnabled', 'previewMaxChars', 'defaultSessionId'])
      if (typeof value.previewEnabled !== 'boolean') throw new Error('host message has an invalid previewEnabled')
      if (!isPreviewMaxChars(value.previewMaxChars)) throw new Error('host message has an invalid previewMaxChars')
      if (value.defaultSessionId !== null && (typeof value.defaultSessionId !== 'string' || value.defaultSessionId.length === 0)) {
        throw new Error('host message has an invalid defaultSessionId')
      }
      return { kind: 'conversation-config', previewEnabled: value.previewEnabled, previewMaxChars: value.previewMaxChars, defaultSessionId: value.defaultSessionId as string | null }
```

新增两个分支（放在 `clear-preview` 分支后、`default:` 前）：

```ts
    case 'reply':
      assertExactKeys(value, ['version', 'kind', 'requestId', 'text', 'completed'], 'host message', ['kind', 'requestId', 'text', 'completed'])
      assertPositiveSafeInteger(value.requestId, 'host message requestId')
      if (typeof value.text !== 'string' || value.text.length === 0 || value.text.length > REPLY_MAX_CHARS) {
        throw new Error('host message has an invalid reply text')
      }
      if (typeof value.completed !== 'boolean') throw new Error('host message has an invalid completed')
      return { kind: 'reply', requestId: value.requestId, text: value.text, completed: value.completed }
    case 'conversation-history':
      assertExactKeys(value, ['version', 'kind', 'requestId', 'available', 'messages'], 'host message', ['kind', 'requestId', 'available', 'messages'])
      assertPositiveSafeInteger(value.requestId, 'host message requestId')
      if (typeof value.available !== 'boolean') throw new Error('host message has an invalid available')
      if (!isHistoryMessages(value.messages)) throw new Error('host message has invalid history messages')
      return { kind: 'conversation-history', requestId: value.requestId, available: value.available, messages: [...value.messages as HistoryMessageEntry[]] }
```

新增辅助函数：

```ts
function isHistoryMessages(value: unknown): value is readonly HistoryMessageEntry[] {
  if (!Array.isArray(value) || value.length > HISTORY_LIMIT) return false
  return value.every((entry) => {
    if (entry === null || typeof entry !== 'object' || Array.isArray(entry)) return false
    const record = entry as Record<string, unknown>
    return (record.role === 'user' || record.role === 'assistant')
      && typeof record.text === 'string'
      && record.text.length > 0
      && record.text.length <= HISTORY_MESSAGE_MAX_CHARS
  })
}
```

`legacyOutboundMessage` 中 `conversation-config` 也需要显式载荷（保持抛错不变，因为 `hello`/`shutdown` 外都需要显式载荷——无需改动）。

- [ ] **Step 3: 更新现有协议测试到 v5**

在 `test/protocol.test.mjs` 中把所有字面量 `version: 4` 替换为 `version: 5`（`replace_all`），并把所有 `conversation-config` 构造改为携带 `defaultSessionId`：

```js
{ kind: 'conversation-config', previewEnabled: true, previewMaxChars: 480, defaultSessionId: null }
```

- [ ] **Step 4: 新增 v5 测试**

在 `test/protocol.test.mjs` 末尾追加：

```js
test('round-trips a request-history helper message', () => {
  const parsed = parseHelperMessage('{"version":5,"kind":"request-history","requestId":9}\n')
  assert.deepEqual(parsed, { version: 5, kind: 'request-history', requestId: 9 })
})

test('round-trips a reply host message with the extended limit', () => {
  const text = 'a'.repeat(8000)
  const line = encodeHostMessage({ kind: 'reply', requestId: 3, text, completed: true })
  const parsed = parseHostMessage(line)
  assert.deepEqual(parsed, { version: 5, kind: 'reply', requestId: 3, text, completed: true })
})

test('rejects an over-limit reply text', () => {
  assert.throws(() => encodeHostMessage({ kind: 'reply', requestId: 3, text: 'a'.repeat(8001), completed: true }))
})

test('round-trips a conversation-history message with bounded entries', () => {
  const messages = [{ role: 'user', text: 'hi' }, { role: 'assistant', text: 'hello' }]
  const parsed = parseHostMessage(encodeHostMessage({ kind: 'conversation-history', requestId: 4, available: true, messages }))
  assert.deepEqual(parsed, { version: 5, kind: 'conversation-history', requestId: 4, available: true, messages })
})

test('rejects history entries beyond the limit or with unknown roles', () => {
  const tooMany = Array.from({ length: 21 }, (_, i) => ({ role: 'user', text: `m${i}` }))
  assert.throws(() => encodeHostMessage({ kind: 'conversation-history', requestId: 4, available: true, messages: tooMany }))
  assert.throws(() => encodeHostMessage({ kind: 'conversation-history', requestId: 4, available: true, messages: [{ role: 'system', text: 'x' }] }))
  assert.throws(() => encodeHostMessage({ kind: 'conversation-history', requestId: 4, available: true, messages: [{ role: 'user', text: '' }] }))
})

test('requires defaultSessionId on conversation-config', () => {
  assert.throws(() => encodeHostMessage({ kind: 'conversation-config', previewEnabled: true, previewMaxChars: 480 }))
  const parsed = parseHostMessage(encodeHostMessage({ kind: 'conversation-config', previewEnabled: true, previewMaxChars: 480, defaultSessionId: 's-1' }))
  assert.equal(parsed.defaultSessionId, 's-1')
})
```

- [ ] **Step 5: 运行测试**

Run: `npm run build; node --test --test-isolation=none test/protocol.test.mjs test/dialogue-history.test.mjs`
Expected: 全部 PASS。

- [ ] **Step 6: 提交**

```bash
git add src/protocol.ts test/protocol.test.mjs src/dialogue-history.ts test/dialogue-history.test.mjs
git commit -m "feat: upgrade dialogue protocol to v5 with reply and history"
```

---

### Task 3: TS 控制器集成

**Files:**
- Modify: `src/dsh-dialogue-types.ts`
- Modify: `src/dialogue-controller.ts`
- Test: `test/dialogue-controller.test.mjs`

- [ ] **Step 1: 扩展 DshAgent 类型**

在 `src/dsh-dialogue-types.ts` 中：

```ts
export type DshAgent = {
  status: string
  followup(message: UserMessage): void | Promise<void>
  session?: { events: readonly unknown[] }
}
```

- [ ] **Step 2: 写失败测试**

在 `test/dialogue-controller.test.mjs` 的 `createDsh` 中让 `agent` 支持 `session`（现有夹具不传则无 session，测试需要时传入）：

```js
test('publishes the final reply text after a turn ends', async () => {
  const sent = []
  const followups = []
  const events = [
    { type: 'user/message', data: { content: [{ type: 'text', text: 'hi' }], source: { kind: 'user' } } },
    { type: 'assistant/message', data: { message: { content: [{ type: 'reasoning', text: 'hidden' }, { type: 'text', text: '答复' }] } } },
  ]
  const dsh = createDsh({ agent: { status: 'idle', followup: (message) => followups.push(message), session: { events } } })
  const controller = new DialogueController(dsh.context, (message) => sent.push(message))

  await controller.acceptInput({ requestId: 3, text: 'hi' })
  controller.observeEvent('s-1', { type: 'user/message', data: { id: followups[0].id } })
  controller.observeEvent('s-1', { type: 'turn/start', data: { turn: 4 } })
  controller.observeEvent('s-1', { type: 'turn/end', data: { turn: 4 } })

  assert.deepEqual(sent.filter((message) => message.kind === 'reply'), [
    { kind: 'reply', requestId: 3, text: '答复', completed: true },
  ])
})

test('answers a history request with the extracted dialogue', async () => {
  const sent = []
  const events = [
    { type: 'user/message', data: { content: [{ type: 'text', text: 'hi' }], source: { kind: 'user' } } },
    { type: 'assistant/message', data: { message: { content: [{ type: 'text', text: 'hello' }] } } },
  ]
  const dsh = createDsh({ agent: { status: 'idle', followup: () => {}, session: { events } } })
  const controller = new DialogueController(dsh.context, (message) => sent.push(message))

  await controller.acceptInput({ requestId: 5, text: 'hi' })
  controller.requestHistory(8)

  assert.deepEqual(sent.filter((message) => message.kind === 'conversation-history'), [
    { kind: 'conversation-history', requestId: 8, available: true, messages: [{ role: 'user', text: 'hi' }, { role: 'assistant', text: 'hello' }] },
  ])
})

test('reports an unavailable session in a history answer', async () => {
  const sent = []
  const dsh = createDsh({ resumeFails: true })
  const controller = new DialogueController(dsh.context, (message) => sent.push(message))

  controller.requestHistory(8)

  assert.deepEqual(sent.filter((message) => message.kind === 'conversation-history'), [
    { kind: 'conversation-history', requestId: 8, available: false, messages: [] },
  ])
})

test('publishes the default session id with the conversation config', () => {
  const sent = []
  const dsh = createDsh()
  const controller = new DialogueController(dsh.context, (message) => sent.push(message))

  controller.publishConversationConfig()

  assert.deepEqual(sent.filter((message) => message.kind === 'conversation-config'), [
    { kind: 'conversation-config', previewEnabled: true, previewMaxChars: 80, defaultSessionId: 's-1' },
  ])
})
```

- [ ] **Step 3: 运行测试确认失败**

Run: `npm run build; node --test --test-isolation=none test/dialogue-controller.test.mjs`
Expected: 新 4 个测试 FAIL（`requestHistory` 不存在、`reply` 未发送、`conversation-config` 缺 `defaultSessionId`），其余 PASS。

- [ ] **Step 4: 实现控制器逻辑**

在 `src/dialogue-controller.ts` 中：

`publishConversationConfig` 与 `settingsChanged` 的 `conversation-config` 发送处增加 `defaultSessionId`：

```ts
    this.send({
      kind: 'conversation-config',
      previewEnabled: settings.previewEnabled,
      previewMaxChars: settings.previewMaxChars,
      defaultSessionId: settings.defaultSessionId,
    })
```

`observeEvent` 的 `turn/end` 分支末尾追加（在 `this.removeRequest(request)` 之后）：

```ts
    if (event.type === 'turn/end') {
      if (request.previewEnabled && request.preview.length > 0) {
        this.send({ kind: 'reply-preview', requestId: request.requestId, text: request.preview, completed: true })
      }
      this.requestsByTurn.delete(key)
      this.removeRequest(request)
      this.publishReply(sessionId, request.requestId)
      return
    }
```

新增公开方法 `requestHistory` 与私有 `publishReply`（放在 `sessionUnavailable` 之前）：

```ts
  public requestHistory(requestId: number): void {
    const settings = this.ctx.settings.get()
    const sessionId = this.currentInputSessionId ?? settings.defaultSessionId
    if (sessionId === null || sessionId === undefined) {
      this.send({ kind: 'conversation-history', requestId, available: false, messages: [] })
      return
    }
    let agent = this.ctx.agents.get(sessionId)
    if (agent === undefined) {
      this.send({ kind: 'conversation-history', requestId, available: false, messages: [] })
      return
    }
    try {
      const messages = extractDialogueHistory(agent.session?.events ?? [])
      this.send({ kind: 'conversation-history', requestId, available: true, messages })
    } catch {
      this.send({ kind: 'conversation-history', requestId, available: false, messages: [] })
    }
  }

  private publishReply(sessionId: string, requestId: number): void {
    try {
      const agent = this.ctx.agents.get(sessionId)
      const events = agent?.session?.events
      if (events === undefined) return
      const history = extractDialogueHistory(events)
      for (let index = history.length - 1; index >= 0; index--) {
        if (history[index].role !== 'assistant') continue
        this.send({ kind: 'reply', requestId, text: history[index].text, completed: true })
        return
      }
    } catch {
      // Best effort: a failed reply read never breaks the event pipeline.
    }
  }
```

文件头部 import 增加：

```ts
import { extractDialogueHistory } from './dialogue-history.js'
```

- [ ] **Step 5: 运行测试**

Run: `npm run build; node --test --test-isolation=none test/dialogue-controller.test.mjs`
Expected: 全部 PASS。

- [ ] **Step 6: 提交**

```bash
git add src/dsh-dialogue-types.ts src/dialogue-controller.ts test/dialogue-controller.test.mjs
git commit -m "feat: publish final reply and dialogue history from the controller"
```

---

### Task 4: TS 路由与 Helper 转发

**Files:**
- Modify: `src/index.ts`
- Modify: `src/helper-process.ts`
- Test: `test/index.test.mjs`
- Test: `test/helper-process.test.mjs`

- [ ] **Step 1: 写失败测试**

在 `test/index.test.mjs` 追加：

```js
test('routes a request-history helper message to the controller', () => {
  const calls = []
  const controller = {
    acceptInput: () => calls.push('input'),
    requestHistory: (requestId) => calls.push(['history', requestId]),
    helperClosed: () => calls.push('closed'),
  }

  routeHelperMessage({ version: 5, kind: 'request-history', requestId: 6 }, controller)

  assert.deepEqual(calls, [['history', 6]])
})
```

- [ ] **Step 2: 运行测试确认失败**

Run: `npm run build; node --test --test-isolation=none test/index.test.mjs`
Expected: 新测试 FAIL（路由把 request-history 当 lifecycle 消息处理）。

- [ ] **Step 3: 实现路由**

在 `src/index.ts` 的 `routeHelperMessage` 中：

```ts
export function routeHelperMessage(
  message: HelperProcessMessage,
  controller: Pick<DialogueController, 'acceptInput' | 'requestHistory' | 'helperClosed'> | undefined,
): Promise<void> {
  if (message.kind === 'input' || message.kind === 'request-history') {
    try {
      const handled = message.kind === 'input'
        ? controller?.acceptInput(message)
        : controller?.requestHistory(message.requestId)
      return Promise.resolve(handled).catch(() => {})
    } catch {
      return Promise.resolve()
    }
  }

  controller?.helperClosed()
  return Promise.resolve()
}
```

- [ ] **Step 4: 更新 HelperProcessMessage 类型与转发**

在 `src/helper-process.ts` 中：

```ts
export type HelperProcessMessage = HelperInputMessage | HelperHistoryRequest | (Pick<HelperMessage, 'version'> & { kind: 'closed' })
```

import 增加 `HelperHistoryRequest`。转发条件（原 `message.kind === 'input'`）改为：

```ts
        if ((message.kind === 'input' || message.kind === 'request-history') && message.requestId > this.lastInputRequestId) {
```

- [ ] **Step 5: 运行测试**

Run: `npm run build; node --test --test-isolation=none test/index.test.mjs test/helper-process.test.mjs test/dialogue-controller.test.mjs`
Expected: 全部 PASS。（`helper-process.test.mjs` 若含 `version: 4` 字面量，同步改为 `5`。）

- [ ] **Step 6: 提交**

```bash
git add src/index.ts src/helper-process.ts test/index.test.mjs test/helper-process.test.mjs
git commit -m "feat: route history requests through the helper process"
```

---

### Task 5: C# 协议 v5

**Files:**
- Modify: `pet-helper/ProtocolMessage.cs`
- Modify: `pet-helper/ProtocolReader.cs`
- Modify: `pet-helper/App.xaml.cs`
- Test: `pet-helper.Tests/ProtocolReaderTests.cs`

- [ ] **Step 1: 更新消息类**

`pet-helper/ProtocolMessage.cs` 全文替换：

```csharp
using System.Collections.Immutable;

namespace PetHelper;

public abstract record ProtocolMessage(int Version, string Kind);

public sealed record HelloMessage() : ProtocolMessage(5, "hello");

public sealed record ShutdownMessage() : ProtocolMessage(5, "shutdown");

public sealed record ConfigMessage(double Scale, bool ReducedMotion) : ProtocolMessage(5, "config");

public sealed record StateMessage(string State, ImmutableArray<string> Activities, string Label, long Sequence) : ProtocolMessage(5, "state");

public sealed record ConversationConfigMessage(bool PreviewEnabled, int PreviewMaxChars, string? DefaultSessionId) : ProtocolMessage(5, "conversation-config");

public sealed record InputStatusMessage(long RequestId, string Status) : ProtocolMessage(5, "input-status");

public sealed record ReplyPreviewMessage(long RequestId, string Text, bool Completed) : ProtocolMessage(5, "reply-preview");

public sealed record ClearPreviewMessage(long RequestId, string Reason) : ProtocolMessage(5, "clear-preview");

public sealed record ReplyMessage(long RequestId, string Text, bool Completed) : ProtocolMessage(5, "reply");

public sealed record HistoryItem(string Role, string Text);

public sealed record HistoryMessage(long RequestId, bool Available, ImmutableArray<HistoryItem> Messages) : ProtocolMessage(5, "conversation-history");
```

- [ ] **Step 2: 更新解析器**

`pet-helper/ProtocolReader.cs` 修改：

```csharp
    private const int MaxLineLength = 65536;
    private const int MaxTextLength = 2000;
    private const int MaxReplyLength = 8000;
    private const int MaxHistoryMessages = 20;
    private const int MinPreviewChars = 80;
```

`Parse` 的 switch 增加分支：

```csharp
                    "conversation-config" => ParseConversationConfig(root),
                    "input-status" => ParseInputStatus(root),
                    "reply-preview" => ParseReplyPreview(root),
                    "clear-preview" => ParseClearPreview(root),
                    "reply" => ParseReply(root),
                    "conversation-history" => ParseHistory(root),
                    _ => null,
```

`ParseConversationConfig` 替换：

```csharp
    private static ConversationConfigMessage? ParseConversationConfig(JsonElement root)
    {
        if (!HasExactlyProperties(root, "version", "kind", "previewEnabled", "previewMaxChars", "defaultSessionId")
            || !root.TryGetProperty("previewEnabled", out var previewEnabled)
            || !root.TryGetProperty("previewMaxChars", out var previewMaxChars)
            || !root.TryGetProperty("defaultSessionId", out var defaultSessionId)
            || previewEnabled.ValueKind is not JsonValueKind.True and not JsonValueKind.False
            || previewMaxChars.ValueKind != JsonValueKind.Number
            || !previewMaxChars.TryGetInt32(out var maxChars)
            || maxChars is < MinPreviewChars or > MaxTextLength
            || (defaultSessionId.ValueKind != JsonValueKind.Null
                && (defaultSessionId.ValueKind != JsonValueKind.String
                    || defaultSessionId.GetString() is not { Length: > 0 })))
        {
            return null;
        }

        return new ConversationConfigMessage(
            previewEnabled.GetBoolean(),
            maxChars,
            defaultSessionId.ValueKind == JsonValueKind.Null ? null : defaultSessionId.GetString());
    }
```

新增两个解析方法（放在 `ParseClearPreview` 之后）：

```csharp
    private static ReplyMessage? ParseReply(JsonElement root)
    {
        if (!HasExactlyProperties(root, "version", "kind", "requestId", "text", "completed")
            || !TryGetRequestId(root, out var requestId)
            || !root.TryGetProperty("text", out var text)
            || !root.TryGetProperty("completed", out var completed)
            || text.ValueKind != JsonValueKind.String
            || text.GetString() is not { } replyText
            || replyText.Length is 0 or > MaxReplyLength
            || completed.ValueKind is not JsonValueKind.True and not JsonValueKind.False)
        {
            return null;
        }

        return new ReplyMessage(requestId, replyText, completed.GetBoolean());
    }

    private static HistoryMessage? ParseHistory(JsonElement root)
    {
        if (!HasExactlyProperties(root, "version", "kind", "requestId", "available", "messages")
            || !TryGetRequestId(root, out var requestId)
            || !root.TryGetProperty("available", out var available)
            || !root.TryGetProperty("messages", out var messages)
            || available.ValueKind is not JsonValueKind.True and not JsonValueKind.False
            || messages.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var items = new List<HistoryItem>();
        foreach (var entry in messages.EnumerateArray())
        {
            if (entry.ValueKind != JsonValueKind.Object
                || !entry.TryGetProperty("role", out var role)
                || !entry.TryGetProperty("text", out var text)
                || role.ValueKind != JsonValueKind.String
                || role.GetString() is not ("user" or "assistant")
                || text.ValueKind != JsonValueKind.String
                || text.GetString() is not { Length: > 0 and <= MaxTextLength } entryText)
            {
                return null;
            }

            items.Add(new HistoryItem(role.GetString()!, entryText));
            if (items.Count > MaxHistoryMessages) return null;
        }

        return new HistoryMessage(requestId, available.GetBoolean(), items.ToImmutableArray());
    }
```

`HasVersionFour` 改名为 `HasVersionFive` 并检查 `value == 5`，`Parse` 中调用处同步改名。

- [ ] **Step 3: 更新 App.xaml.cs（仅版本与路由）**

`SerializeHelperMessage`、`WriteInput` 的 `version = 4` 改为 `version = 5`；`ReadProtocolLoop` 的 switch 中 `ConversationConfigMessage or InputStatusMessage or ReplyPreviewMessage or ClearPreviewMessage` 改为：

```csharp
                    case ConversationConfigMessage or InputStatusMessage or ReplyPreviewMessage or ClearPreviewMessage or ReplyMessage or HistoryMessage:
```

（`HistoryRequested` 事件接线在 Task 7 中完成，此时事件尚不存在。）

- [ ] **Step 4: 更新并新增 C# 测试**

`pet-helper.Tests/ProtocolReaderTests.cs` 中把 `version: 4` 相关断言改为 `5`，`conversation-config` 解析增加 `defaultSessionId`；追加：

```csharp
    [Fact]
    public void Parses_reply_message()
    {
        var message = ProtocolReader.Parse("{\"version\":5,\"kind\":\"reply\",\"requestId\":7,\"text\":\"final\",\"completed\":true}");

        var reply = Assert.IsType<ReplyMessage>(message);
        Assert.Equal(7, reply.RequestId);
        Assert.Equal("final", reply.Text);
        Assert.True(reply.Completed);
    }

    [Fact]
    public void Parses_conversation_history_message()
    {
        var message = ProtocolReader.Parse("{\"version\":5,\"kind\":\"conversation-history\",\"requestId\":8,\"available\":true,\"messages\":[{\"role\":\"user\",\"text\":\"hi\"},{\"role\":\"assistant\",\"text\":\"hello\"}]}");

        var history = Assert.IsType<HistoryMessage>(message);
        Assert.True(history.Available);
        Assert.Equal(2, history.Messages.Length);
        Assert.Equal("user", history.Messages[0].Role);
        Assert.Equal("hello", history.Messages[1].Text);
    }

    [Fact]
    public void Rejects_over_limit_reply_and_history_entries()
    {
        var longReply = "{\"version\":5,\"kind\":\"reply\",\"requestId\":1,\"text\":\"" + new string('a', 8001) + "\",\"completed\":true}";
        Assert.Null(ProtocolReader.Parse(longReply));

        var tooMany = string.Join(",", Enumerable.Range(0, 21).Select(i => $"{{\"role\":\"user\",\"text\":\"m{i}\"}}"));
        var history = $"{{\"version\":5,\"kind\":\"conversation-history\",\"requestId\":1,\"available\":true,\"messages\":[{tooMany}]}}";
        Assert.Null(ProtocolReader.Parse(history));
    }
```

- [ ] **Step 5: 运行 C# 测试**

Run: `dotnet test pet-helper.Tests\PetHelper.Tests.csproj --no-restore`
Expected: 全部 PASS（沙箱命名管道受阻时使用 full-access 重试）。

- [ ] **Step 6: 提交**

```bash
git add pet-helper/ProtocolMessage.cs pet-helper/ProtocolReader.cs pet-helper/App.xaml.cs pet-helper.Tests/ProtocolReaderTests.cs
git commit -m "feat: parse v5 reply and history messages in the helper"
```

---

### Task 6: C# ConversationState 回复与历史状态

**Files:**
- Modify: `pet-helper/ConversationState.cs`
- Test: `pet-helper.Tests/ConversationStateTests.cs`

- [ ] **Step 1: 写失败测试**

在 `pet-helper.Tests/ConversationStateTests.cs` 追加：

```csharp
    [Fact]
    public void Marks_the_reply_pending_after_sending_and_resolves_on_reply()
    {
        var state = new ConversationState(previewEnabled: false, previewMaxChars: 80);

        state.Apply(new InputStatusMessage(4, "sent"));
        Assert.True(state.ReplyPending);
        Assert.Equal(string.Empty, state.ReplyText);

        state.Apply(new ReplyMessage(4, "最终回复", true));
        Assert.False(state.ReplyPending);
        Assert.Equal("最终回复", state.ReplyText);
    }

    [Fact]
    public void Clears_reply_pending_on_rejection()
    {
        var state = new ConversationState(previewEnabled: false, previewMaxChars: 80);
        state.Apply(new InputStatusMessage(4, "sent"));

        state.Apply(new InputStatusMessage(4, "rejected"));

        Assert.False(state.ReplyPending);
        Assert.Equal(string.Empty, state.ReplyText);
    }

    [Fact]
    public void Stores_history_messages_and_availability()
    {
        var state = new ConversationState(previewEnabled: false, previewMaxChars: 80);
        var messages = ImmutableArray.Create(new HistoryItem("user", "hi"), new HistoryItem("assistant", "hello"));

        state.Apply(new HistoryMessage(9, true, messages));

        Assert.True(state.HistoryAvailable);
        Assert.Equal(2, state.HistoryMessages.Length);
    }

    [Fact]
    public void Clears_reply_and_history_when_the_default_session_changes()
    {
        var state = new ConversationState(previewEnabled: false, previewMaxChars: 80);
        state.Apply(new ConversationConfigMessage(false, 80, "s-1"));
        state.Apply(new InputStatusMessage(4, "sent"));
        state.Apply(new ReplyMessage(4, "回复", true));
        state.Apply(new HistoryMessage(9, true, ImmutableArray.Create(new HistoryItem("user", "hi"))));

        state.Apply(new ConversationConfigMessage(false, 80, "s-2"));

        Assert.Equal(string.Empty, state.ReplyText);
        Assert.False(state.ReplyPending);
        Assert.Equal(0, state.HistoryMessages.Length);
    }
```

- [ ] **Step 2: 运行确认失败**

Run: `dotnet test pet-helper.Tests\PetHelper.Tests.csproj --no-restore`
Expected: 新测试 FAIL（属性不存在 / 构造签名不匹配），现有测试因 `ConversationConfigMessage` 新参数编译失败——先把现有测试调用改为 `new ConversationConfigMessage(false, 80, null)`。

- [ ] **Step 3: 实现状态**

`pet-helper/ConversationState.cs` 修改：

```csharp
public sealed class ConversationState
{
    private const int MaxPreviewChars = 2000;

    private string? lastDefaultSessionId;

    public ConversationState(bool previewEnabled, int previewMaxChars)
    {
        PreviewEnabled = previewEnabled;
        PreviewMaxChars = ValidatePreviewMaxChars(previewMaxChars);
    }

    public bool PreviewEnabled { get; private set; }

    public int PreviewMaxChars { get; private set; }

    public long RequestId { get; private set; }

    public string StatusText { get; private set; } = string.Empty;

    public string PreviewText { get; private set; } = string.Empty;

    public string ReplyText { get; private set; } = string.Empty;

    public bool ReplyPending { get; private set; }

    public ImmutableArray<HistoryItem> HistoryMessages { get; private set; } = [];

    public bool HistoryAvailable { get; private set; }

    public void Apply(ProtocolMessage message)
    {
        switch (message)
        {
            case ConversationConfigMessage config:
                PreviewEnabled = config.PreviewEnabled;
                PreviewMaxChars = ValidatePreviewMaxChars(config.PreviewMaxChars);
                PreviewText = PreviewEnabled ? KeepLatest(PreviewText) : string.Empty;
                if (config.DefaultSessionId != lastDefaultSessionId)
                {
                    lastDefaultSessionId = config.DefaultSessionId;
                    ReplyText = string.Empty;
                    ReplyPending = false;
                    HistoryMessages = [];
                    HistoryAvailable = false;
                }
                break;
            case InputStatusMessage status:
                if (status.RequestId < RequestId)
                {
                    break;
                }

                var isNewerRequest = status.RequestId > RequestId;
                RequestId = status.RequestId;
                if (isNewerRequest)
                {
                    PreviewText = string.Empty;
                }
                StatusText = StatusTextFor(status.Status);
                if (status.Status is "sent" or "queued")
                {
                    ReplyPending = true;
                    ReplyText = string.Empty;
                }
                else
                {
                    ReplyPending = false;
                }
                break;
            case ReplyMessage reply when IsCurrentOrFirst(reply.RequestId):
                SetFirstRequestId(reply.RequestId);
                ReplyText = reply.Text;
                ReplyPending = false;
                break;
            case HistoryMessage history:
                HistoryMessages = history.Messages;
                HistoryAvailable = history.Available;
                break;
            case ReplyPreviewMessage preview when PreviewEnabled && IsCurrentOrFirst(preview.RequestId):
                SetFirstRequestId(preview.RequestId);
                PreviewText = KeepLatest(preview.Text);
                break;
            case ClearPreviewMessage clear when IsCurrentOrFirst(clear.RequestId):
                SetFirstRequestId(clear.RequestId);
                PreviewText = string.Empty;
                break;
        }
    }
```

其余方法（`ClearLocalInput`、`BeginInput`、`IsCurrentOrFirst`、`SetFirstRequestId`、`KeepLatest`、`ValidatePreviewMaxChars`、`StatusTextFor`）保持不变；文件头补 `using System.Collections.Immutable;`。

注意：`BeginInput` 也应清 `ReplyPending = false;`（新输入开始，旧回复占位清除）。

- [ ] **Step 4: 运行 C# 测试**

Run: `dotnet test pet-helper.Tests\PetHelper.Tests.csproj --no-restore`
Expected: 全部 PASS。

- [ ] **Step 5: 提交**

```bash
git add pet-helper/ConversationState.cs pet-helper.Tests/ConversationStateTests.cs
git commit -m "feat: track reply and history state in conversation state"
```

---

### Task 7: WPF UI 布局

**Files:**
- Modify: `pet-helper/MainWindow.xaml`
- Modify: `pet-helper/MainWindow.xaml.cs`
- Test: `test/wpf-layout.test.mjs`

- [ ] **Step 1: 写失败测试**

在 `test/wpf-layout.test.mjs` 追加：

```js
test('renders the history button, reply bubble and history overlay', () => {
  const xaml = readFileSync(new URL('../pet-helper/MainWindow.xaml', import.meta.url), 'utf8')

  assert.match(xaml, /HistoryButton/)
  assert.match(xaml, /x:Name="ReplyBubble"/)
  assert.match(xaml, /x:Name="HistoryPanel"/)
  assert.match(xaml, /x:Name="HistoryList"/)
  assert.match(xaml, /HistoryRequested/)
})

test('scales the dialogue stack with the window', () => {
  const xaml = readFileSync(new URL('../pet-helper/MainWindow.xaml', import.meta.url), 'utf8')
  const code = readFileSync(new URL('../pet-helper/MainWindow.xaml.cs', import.meta.url), 'utf8')

  assert.match(xaml, /x:Name="DialogueStack"/)
  assert.match(code, /DialogueStack\.LayoutTransform\s*=\s*bubbleScale/)
})
```

- [ ] **Step 2: 运行确认失败**

Run: `node --test --test-isolation=none test/wpf-layout.test.mjs`
Expected: 新测试 FAIL。

- [ ] **Step 3: 更新 XAML**

`pet-helper/MainWindow.xaml` 的 Grid 内容改为（把原 `InputBubble` 移入 `DialogueStack`，新增回复气泡与历史覆盖层）：

```xml
  <Grid MouseLeftButtonDown="Pet_MouseLeftButtonDown">
    <StackPanel x:Name="DialogueStack" HorizontalAlignment="Center" VerticalAlignment="Top"
                Margin="8,4,8,0" Panel.ZIndex="3">
      <Border x:Name="InputBubble" Visibility="Collapsed" Background="#F2254050"
              BorderBrush="#B3FFFFFF" BorderThickness="1" CornerRadius="12" Padding="10">
        <StackPanel Width="184">
          <DockPanel LastChildFill="False">
            <Button DockPanel.Dock="Left" Content="历史" Click="HistoryButton_Click"
                    Padding="6,0" Margin="0,0,6,0" ToolTip="查看对话历史" />
            <Button DockPanel.Dock="Right" Content="×" Click="CloseInputButton_Click"
                    ToolTip="关闭输入" Padding="5,0" />
            <TextBlock Foreground="White" FontWeight="SemiBold" Text="发送到已选会话"
                       HorizontalAlignment="Center" />
          </DockPanel>
          <TextBox x:Name="InputTextBox" Margin="0,6,0,4" MinHeight="36" MaxHeight="72"
                   AcceptsReturn="True" TextWrapping="Wrap" VerticalScrollBarVisibility="Auto"
                   KeyDown="InputTextBox_KeyDown" />
          <Button x:Name="SendButton" Content="发送" HorizontalAlignment="Right"
                  Padding="10,2" Click="SendButton_Click" />
          <TextBlock x:Name="ConversationStatusLabel" Foreground="White" Margin="0,4,0,0"
                     TextWrapping="Wrap" />
        </StackPanel>
      </Border>
      <Border x:Name="ReplyBubble" Visibility="Collapsed" Background="#E6203A4A"
              BorderBrush="#B3FFFFFF" BorderThickness="1" CornerRadius="12" Padding="10"
              Margin="0,4,0,0">
        <ScrollViewer MaxHeight="76" VerticalScrollBarVisibility="Auto">
          <TextBlock x:Name="ReplyTextBlock" Foreground="White" TextWrapping="Wrap" />
        </ScrollViewer>
      </Border>
    </StackPanel>
    <Border x:Name="PreviewBubble" Visibility="Collapsed" Background="#E6254050"
            BorderBrush="#B3FFFFFF" BorderThickness="1" CornerRadius="12" Padding="10"
            HorizontalAlignment="Center" VerticalAlignment="Top" Margin="8,4,8,0"
            Panel.ZIndex="2">
      <ScrollViewer MaxHeight="92" VerticalScrollBarVisibility="Auto">
        <TextBlock x:Name="PreviewText" Foreground="White" TextWrapping="Wrap" />
      </ScrollViewer>
    </Border>
    <Image x:Name="PetImage" Stretch="Uniform" VerticalAlignment="Bottom" />
    <Border x:Name="StateBubble" Visibility="Collapsed" Background="#E62B4B5F"
            CornerRadius="12" Padding="12,6" HorizontalAlignment="Center"
            VerticalAlignment="Top" Margin="10,106,10,0" Panel.ZIndex="1">
      <TextBlock x:Name="StateLabel" Foreground="White" FontWeight="SemiBold" TextWrapping="Wrap" />
    </Border>
    <Border x:Name="HistoryPanel" Visibility="Collapsed" Background="#F21B2A35"
            BorderBrush="#B3FFFFFF" BorderThickness="1" CornerRadius="12" Padding="10"
            Margin="8,8,8,8" Panel.ZIndex="10">
      <Grid>
        <Grid.RowDefinitions>
          <RowDefinition Height="Auto" />
          <RowDefinition Height="*" />
        </Grid.RowDefinitions>
        <DockPanel Grid.Row="0">
          <TextBlock DockPanel.Dock="Left" Text="对话历史" Foreground="White" FontWeight="SemiBold" />
          <Button DockPanel.Dock="Right" Content="×" Click="CloseHistoryButton_Click"
                  Padding="5,0" />
          <TextBlock x:Name="HistoryStatus" Foreground="#CCFFFFFF" Text="加载中…"
                     HorizontalAlignment="Right" VerticalAlignment="Center" />
        </DockPanel>
        <ScrollViewer Grid.Row="1" x:Name="HistoryScroll" Margin="0,6,0,0"
                      VerticalScrollBarVisibility="Auto">
          <StackPanel x:Name="HistoryList" />
        </ScrollViewer>
      </Grid>
    </Border>
  </Grid>
```

- [ ] **Step 4: 更新 xaml.cs**

`pet-helper/MainWindow.xaml.cs` 修改：

字段增加历史请求计数器与事件：

```csharp
    private long historyRequestId;

    public event EventHandler<HistoryRequestedEventArgs>? HistoryRequested;
```

`ApplyState` 中气泡缩放部分改为（`DialogueStack` 取代三个单独气泡的 LayoutTransform；`PreviewBubble`/`StateBubble` 保持）:

```csharp
        var bubbleScale = new ScaleTransform(scale, scale);
        DialogueStack.LayoutTransform = bubbleScale;
        PreviewBubble.LayoutTransform = bubbleScale;
        StateBubble.LayoutTransform = bubbleScale;
        DialogueStack.Margin = ScaleMargin(InputBubbleMargin, scale);
        PreviewBubble.Margin = ScaleMargin(InputBubbleMargin, scale);
        StateBubble.Margin = ScaleMargin(StateBubbleMargin, scale);
        HistoryPanel.LayoutTransform = bubbleScale;
        HistoryPanel.Margin = ScaleMargin(HistoryPanelMargin, scale);
```

常量：

```csharp
    private static readonly Thickness HistoryPanelMargin = new(8d, 8d, 8d, 8d);
```

`UpdateStateBubbleVisibility` 扩展：

```csharp
    private void UpdateStateBubbleVisibility() =>
        StateBubble.Visibility = lastDisplayState.State == "idle"
            || InputBubble.Visibility == Visibility.Visible
            || ReplyBubble.Visibility == Visibility.Visible
            || PreviewBubble.Visibility == Visibility.Visible
            || HistoryPanel.Visibility == Visibility.Visible
            ? Visibility.Collapsed
            : Visibility.Visible;
```

`ApplyConversationMessage` 末尾追加渲染：

```csharp
        ReplyTextBlock.Text = conversationState.ReplyPending && conversationState.ReplyText.Length == 0
            ? "正在生成回复…"
            : conversationState.ReplyText;
        ReplyBubble.Visibility = conversationState.ReplyPending || conversationState.ReplyText.Length != 0
            ? Visibility.Visible
            : Visibility.Collapsed;
        if (message is HistoryMessage)
        {
            RenderHistory(conversationState);
        }
        UpdateStateBubbleVisibility();
```

`ShowInputBubble` 后追加历史按钮处理：

```csharp
    private void HistoryButton_Click(object sender, RoutedEventArgs e)
    {
        historyRequestId++;
        HistoryRequested?.Invoke(this, new HistoryRequestedEventArgs(historyRequestId));
        HistoryPanel.Visibility = Visibility.Visible;
        HistoryStatus.Text = "加载中…";
        HistoryList.Children.Clear();
        HistoryStatus.Visibility = Visibility.Visible;
        HistoryScroll.Visibility = Visibility.Collapsed;
        UpdateStateBubbleVisibility();
    }

    private void CloseHistoryButton_Click(object sender, RoutedEventArgs e)
    {
        HistoryPanel.Visibility = Visibility.Collapsed;
        UpdateStateBubbleVisibility();
    }

    private void RenderHistory(ConversationState state)
    {
        HistoryList.Children.Clear();
        HistoryScroll.Visibility = Visibility.Collapsed;
        HistoryStatus.Visibility = Visibility.Visible;
        if (!state.HistoryAvailable)
        {
            HistoryStatus.Text = "会话不可用";
            return;
        }
        if (state.HistoryMessages.Length == 0)
        {
            HistoryStatus.Text = "暂无对话历史";
            return;
        }

        HistoryStatus.Visibility = Visibility.Collapsed;
        HistoryScroll.Visibility = Visibility.Visible;
        foreach (var item in state.HistoryMessages)
        {
            var isUser = item.Role == "user";
            var bubble = new Border
            {
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(8, 4, 8, 4),
                Margin = new Thickness(0, 2, 0, 2),
                MaxWidth = 150,
                HorizontalAlignment = isUser ? HorizontalAlignment.Right : HorizontalAlignment.Left,
                Background = new SolidColorBrush(Color.FromArgb(210, isUser ? (byte)46 : (byte)64, isUser ? (byte)92 : (byte)68, isUser ? (byte)122 : (byte)84)),
            };
            bubble.Child = new TextBlock
            {
                Text = item.Text,
                TextWrapping = TextWrapping.Wrap,
                Foreground = Brushes.White,
            };
            HistoryList.Children.Add(bubble);
        }
    }
```

`BeginInput` 相关：`SubmitInput` 中现有 `conversationState.BeginInput(nextRequestId)` 已会清 reply（Task 6 中实现）。

文件末尾追加事件参数类，并更新 `App.xaml.cs` 的事件接线（此时事件已存在）：

```csharp
public sealed class HistoryRequestedEventArgs(long requestId) : EventArgs
{
    public long RequestId { get; } = requestId;
}
```

`pet-helper/App.xaml.cs` 修改：

```csharp
        window.InputSubmitted += (_, input) => WriteInput(input);
        window.HistoryRequested += (_, request) => WriteHistoryRequest(request);
```

```csharp
    private static void WriteHistoryRequest(HistoryRequestedEventArgs request)
    {
        Console.Out.WriteLine(JsonSerializer.Serialize(new { version = 5, kind = "request-history", requestId = request.RequestId }));
        Console.Out.Flush();
    }
```

- [ ] **Step 5: 运行布局测试与 C# 测试**

Run: `node --test --test-isolation=none test/wpf-layout.test.mjs`
Run: `dotnet test pet-helper.Tests\PetHelper.Tests.csproj --no-restore`
Expected: 全部 PASS。

- [ ] **Step 6: 提交**

```bash
git add pet-helper/MainWindow.xaml pet-helper/MainWindow.xaml.cs test/wpf-layout.test.mjs
git commit -m "feat: render reply bubble and history overlay in the pet window"
```

---

### Task 8: 全量验证与发布

**Files:**
- Modify: `package.json`、`package-lock.json`（版本 0.1.19）
- Modify: `docs/项目需求书.md`
- Verify: 全部测试

- [ ] **Step 1: 运行全部 Node 测试**

Run: `npm run build; node --test --test-isolation=none test/protocol.test.mjs test/dialogue-settings.test.mjs test/client-settings-model.test.mjs test/companion-reducer.test.mjs test/dsh-event-adapter.test.mjs test/companion-bridge.test.mjs test/dialogue-controller.test.mjs test/dialogue-history.test.mjs test/index.test.mjs test/wpf-layout.test.mjs test/package-layout.test.mjs`
Expected: 全部 PASS。

- [ ] **Step 2: 运行全部 C# 测试与发布握手测试**

Run: `dotnet test pet-helper.Tests\PetHelper.Tests.csproj --no-restore`（沙箱受阻时 full-access）
Run: `npm run build:helper`
Run: `node --test --test-isolation=none test/packaged-helper.test.mjs`（full-access）
Expected: 全部 PASS。

- [ ] **Step 3: 更新需求书边界描述**

`docs/项目需求书.md` 第 7 节"安全与隐私要求"与第 3 节"本期不包含"中，把"独立聊天窗口、文本输入、会话管理"相关条目更新为：桌宠会话框仅显示用户自己的文本消息与助手最终文本回复（不显示 reasoning、工具细节、文件路径与凭据），数据由 DSH 会话日志脱敏提取。

- [ ] **Step 4: 升版本并打包**

`package.json` / `package-lock.json` 版本改为 `0.1.19`；运行：

```powershell
npm pack --cache '<workspace>\.npm-cache-tmp'
```

复制 `dsh-png-pet-0.1.19.tgz` 到 `C:\dsh-packages\`。

- [ ] **Step 5: 安装并验证**

用户退出桌宠后：

```powershell
dsh plugin --profile web remove dsh-png-pet
dsh plugin --profile web add C:\dsh-packages\dsh-png-pet-0.1.19.tgz
dsh plugin --profile web list
```

用户重启 DSH 后手工验收：发送消息 → "正在生成回复…" → 最终回复显示；点击"历史" → 最近 20 条（用户右对齐/助手左对齐）；无历史 → "暂无对话历史"；75% 缩放布局正常。

- [ ] **Step 6: 提交**

```bash
git add package.json package-lock.json docs/项目需求书.md
git commit -m "chore: release 0.1.19 with dialogue reply and history"
```
