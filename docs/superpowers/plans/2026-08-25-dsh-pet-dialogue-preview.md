# DSH 桌宠对话与流式预览 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 让用户从桌宠输入消息并投递到 DSH 设置中选择的会话；在用户开启预览后，在桌宠上显示该请求的流式可见回复。

**Architecture:** Node Host 注册持久设置、处理 Helper 输入，并以消息 ID 和随后出现的 DSH turn 关联桌宠请求。DSH Web 客户端模块复用 `ctx.sessions.list` 的本机会话摘要，只把 `id` 和 `displayTitle` 用于选择器；WPF 仅通过 JSON Lines 接收受限配置、状态和预览，不接触会话标识或标题。

**Tech Stack:** TypeScript/Node.js、DSH Cordis Host/Web API、JSON Lines v4、C# .NET 10 WPF、Node test、xUnit。

---

## 文件结构

| 文件 | 责任 |
| --- | --- |
| `src/protocol.ts` | JSON Lines v4 的严格双向消息与字符上限。 |
| `src/helper-process.ts` | Helper 生命周期及已验证的 `input` 回调。 |
| `src/dialogue-settings.ts` | Host 设置 schema、默认会话标识、预览开关与长度验证。 |
| `src/dialogue-controller.ts` | 输入投递、请求—turn 关联、文本预览缓冲和清理。 |
| `src/dsh-dialogue-types.ts` | 当前 DSH 的最小公开接口适配层，隔离 `Agent`、settings 和 session 事件结构。 |
| `src/client.tsx` | DSH Web 的 `settings.section`，复用 `ctx.sessions.list` 并写入标准设置 API。 |
| `src/client-settings-model.ts` | 仅投影 `id`、`displayTitle` 的纯函数与设置页状态模型。 |
| `pet-helper/ProtocolMessage.cs`、`pet-helper/ProtocolReader.cs` | v4 Host→Helper 消息模型和严格解析。 |
| `pet-helper/ConversationState.cs` | 输入/状态/预览缓冲的无 UI 状态机。 |
| `pet-helper/MainWindow.xaml`、`pet-helper/MainWindow.xaml.cs` | 输入气泡、可滚动预览气泡与提交事件。 |
| `pet-helper/App.xaml.cs` | 订阅提交事件、写出 v4 `input`，派发对话消息。 |
| `docs/security-debt.md` | 已接受的非阻断性安全收紧项及后续关闭条件。 |

### Task 1: 建立发布依赖、设置模型与安全债记录

**Files:**

- Modify: `package.json`
- Modify: `package-lock.json`
- Create: `src/dialogue-settings.ts`
- Create: `src/client-settings-model.ts`
- Create: `test/dialogue-settings.test.mjs`
- Create: `docs/security-debt.md`

- [ ] **Step 1: 写入设置与本机会话投影的失败测试。**

```js
import assert from 'node:assert/strict'
import test from 'node:test'
import { projectSessionOptions, validateDialogueSettings } from '../lib/client-settings-model.js'

test('projects only a session id and its DSH display title', () => {
  const options = projectSessionOptions([{ id: 's-1', displayTitle: '重构桌宠', cwd: 'C:\\private' }])
  assert.deepEqual(options, [{ id: 's-1', title: '重构桌宠' }])
  assert.equal(JSON.stringify(options).includes('private'), false)
})

test('accepts preview bounds and rejects values outside 80 through 2000', () => {
  assert.deepEqual(validateDialogueSettings({ defaultSessionId: 's-1', previewEnabled: true, previewMaxChars: 480 }), { defaultSessionId: 's-1', previewEnabled: true, previewMaxChars: 480 })
  assert.throws(() => validateDialogueSettings({ defaultSessionId: null, previewEnabled: false, previewMaxChars: 79 }), /previewMaxChars/)
})
```

- [ ] **Step 2: 运行测试，确认当前模块不存在。**

Run: `npm run build; node --test test/dialogue-settings.test.mjs`

Expected: FAIL，提示 `client-settings-model.js` 不存在。

- [ ] **Step 3: 添加最小设置模型和 Host schema。**

```ts
export type DialogueSettings = {
  defaultSessionId: string | null
  previewEnabled: boolean
  previewMaxChars: number
}

export function projectSessionOptions(rows: readonly { id: string, displayTitle: string }[]) {
  return rows.map(({ id, displayTitle }) => ({ id, title: displayTitle }))
}

export function validateDialogueSettings(value: DialogueSettings): DialogueSettings {
  if (value.defaultSessionId !== null && (typeof value.defaultSessionId !== 'string' || value.defaultSessionId.length === 0)) throw new Error('defaultSessionId')
  if (typeof value.previewEnabled !== 'boolean') throw new Error('previewEnabled')
  if (!Number.isInteger(value.previewMaxChars) || value.previewMaxChars < 80 || value.previewMaxChars > 2000) throw new Error('previewMaxChars')
  return { ...value }
}
```

`src/dialogue-settings.ts` 用同一约束注册 `dsh-png-pet` DSH 设置命名空间，默认值为 `{ defaultSessionId: null, previewEnabled: false, previewMaxChars: 480 }`。`package.json` 增加 `zod`、所需 DSH Client 类型依赖、React/React 类型和 `./client` export；`dsh.client` 声明 Web 平台并注入 `sessions`、`settingsScope`、`slots`、`connection`。将 `src/client.tsx` 纳入 TypeScript 编译，并确保其编译产物被 `files` 收录。

`docs/security-debt.md` 首项记录：本机 DSH Web 直接持有会话 ID 与标题以支持现有列表选择；关闭条件是上游发布可用的第三方 Remote 投影后，恢复 Host 侧安全别名投影。该文档不得记录任何真实会话数据。

- [ ] **Step 4: 运行设置测试。**

Run: `npm test -- --test-name-pattern="projects only|accepts preview"`

Expected: PASS，且测试输出不含 `cwd` 字段。

- [ ] **Step 5: 提交设置基础。**

```powershell
git add package.json package-lock.json src/dialogue-settings.ts src/client-settings-model.ts test/dialogue-settings.test.mjs docs/security-debt.md
git commit -m "feat: add pet dialogue settings model"
```

### Task 2: 将协议升级为 JSON Lines v4

**Files:**

- Modify: `src/protocol.ts`
- Modify: `test/protocol.test.mjs`
- Modify: `test/fixtures/fake-helper.mjs`
- Modify: `test/helper-process.test.mjs`

- [ ] **Step 1: 先添加 v4 边界测试。**

```js
test('accepts a bounded helper input and encodes a bounded reply preview', () => {
  assert.deepEqual(parseHelperMessage('{"version":4,"kind":"input","requestId":7,"text":"hello"}'), { version: 4, kind: 'input', requestId: 7, text: 'hello' })
  assert.equal(encodeHostMessage({ kind: 'reply-preview', requestId: 7, text: 'ok', completed: false }), '{"version":4,"kind":"reply-preview","requestId":7,"text":"ok","completed":false}\n')
})

test('rejects helper input with an unknown key or more than 2000 characters', () => {
  assert.throws(() => parseHelperMessage('{"version":4,"kind":"input","requestId":1,"text":"x","extra":true}'), /fields/)
  assert.throws(() => parseHelperMessage(JSON.stringify({ version: 4, kind: 'input', requestId: 1, text: 'x'.repeat(2001) })), /text/)
})
```

- [ ] **Step 2: 运行协议测试，确认 v3 实现失败。**

Run: `npm run build; node --test test/protocol.test.mjs test/helper-process.test.mjs`

Expected: FAIL，提示版本或 `input` kind 不受支持。

- [ ] **Step 3: 实现严格 v4 联合类型。**

将 `PROTOCOL_VERSION` 改为 `4`，行上限改为 `4096`。Helper 入站消息是 `ready`、`closed` 或 `{ kind: 'input', requestId, text }`；`requestId` 为正安全整数，`text` 为去首尾空白后 1–2000 字符。Host 出站增加：

```ts
{ kind: 'conversation-config', previewEnabled: boolean, previewMaxChars: number }
{ kind: 'input-status', requestId: number, status: 'queued' | 'sent' | 'no-default-session' | 'session-unavailable' | 'rejected' }
{ kind: 'reply-preview', requestId: number, text: string, completed: boolean }
{ kind: 'clear-preview', requestId: number, reason: 'disabled' | 'next-input' | 'cancelled' | 'closed' | 'session-unavailable' }
```

每个 kind 继续执行精确字段、版本、布尔值、枚举、正安全整数和 2000 字符文本校验。`reply-preview` 仅接受不空的截断文本。更新 fake Helper，使其以 v4 `ready` 握手并把接收行交给测试；`HelperProcessOptions` 新增 `onMessage(message)`，只在成功解析且非生命周期消息时调用。

- [ ] **Step 4: 运行协议与子进程测试。**

Run: `npm test -- --test-name-pattern="bounded|helper input|typed v4|starts a helper"`

Expected: PASS。

- [ ] **Step 5: 提交协议升级。**

```powershell
git add src/protocol.ts src/helper-process.ts test/protocol.test.mjs test/helper-process.test.mjs test/fixtures/fake-helper.mjs
git commit -m "feat: add dialogue protocol v4"
```

### Task 3: 实现 Host 输入投递与请求—turn 关联

**Files:**

- Create: `src/dsh-dialogue-types.ts`
- Create: `src/dialogue-controller.ts`
- Create: `test/dialogue-controller.test.mjs`
- Modify: `src/index.ts`
- Modify: `test/index.test.mjs`

- [ ] **Step 1: 写入控制器失败测试。**

```js
test('maps its generated user message id to the next turn and forwards only text deltas', () => {
  const sent = []
  const controller = new DialogueController(fakeDsh('s-1'), (message) => sent.push(message))
  controller.acceptInput({ requestId: 3, text: 'test input' })
  controller.observeEvent('s-1', { type: 'user/message', data: { id: 'message-3' } })
  controller.observeEvent('s-1', { type: 'turn/start', data: { turn: 12 } })
  controller.observeEvent('s-1', { type: 'assistant/chunk', data: { turn: 12, chunk: { type: 'text-delta', text: 'answer' } } })
  assert.deepEqual(sent.at(-1), { kind: 'reply-preview', requestId: 3, text: 'answer', completed: false })
})

test('drops reasoning, tool deltas and unmatched turns', () => {
  const sent = []
  const controller = new DialogueController(fakeDsh('s-1'), (message) => sent.push(message))
  controller.observeEvent('s-1', { type: 'assistant/chunk', data: { turn: 9, chunk: { type: 'reasoning-delta', text: 'hidden' } } })
  assert.equal(sent.length, 0)
})
```

- [ ] **Step 2: 运行控制器测试，确认模块不存在。**

Run: `npm run build; node --test test/dialogue-controller.test.mjs`

Expected: FAIL，提示 `dialogue-controller.js` 不存在。

- [ ] **Step 3: 实现最小 DSH 适配和控制器。**

`dsh-dialogue-types.ts` 只声明所用表面：`agents.get(id)`、已恢复 Agent 的 `followup(message)`、`createUserMessage()`、settings 读写、以及 `session/event` 的 `user/message`、`turn/start`、`assistant/chunk`、`turn/end` 形状。

`DialogueController.acceptInput()` 读取已校验设置；无默认会话时发送 `no-default-session`。它先调用 `ctx.agents.get(defaultSessionId)`；若未 live，则调用 `ctx.agents.resume({ resumeSessionId: defaultSessionId })` 并使用返回 handle 的 Agent。找不到或恢复失败时清空设置并发送 `session-unavailable`。成功时用 `createUserMessage({ content: [{ type: 'text', text }], source: { kind: 'user' } })` 构造一次消息，将其新 ID 映射到 Helper `requestId`，再调用 `agent.followup(message)`；`agent.status === 'running'` 时先发 `queued`，否则先发 `sent`。不记录 `text`。

观察到映射消息的 `user/message` 后，将下一条 `turn/start` 的 `turn` 绑定到该 `requestId`。仅预览开启时，对该 turn 的 `assistant/chunk` 中 `{ type: 'text-delta' }` 追加文本并保留最后 `previewMaxChars` 字符；`reasoning-delta`、`tool-call-delta`、block 和未知形状直接忽略。匹配 turn 的 `turn/end` 发送 `reply-preview` 的 `completed: true`，随后删除关联。关闭预览、下一输入、失效会话、Helper `closed` 或插件 dispose 时发送 `clear-preview` 并清空所有内存关联。

`index.ts` 在 Helper 的 `onMessage` 中只分发 `input` 给控制器，在现有两个 session observer 中先让控制器观察事件，再让状态桥处理；Helper ready 后立即发送 `conversation-config`。

- [ ] **Step 4: 运行 Host 单元测试。**

Run: `npm test -- --test-name-pattern="maps its generated|drops reasoning|registers both DSH observers"`

Expected: PASS；测试断言消息 kind、请求号和长度，不把输入或回复打印到控制台。

- [ ] **Step 5: 提交 Host 控制器。**

```powershell
git add src/dsh-dialogue-types.ts src/dialogue-controller.ts src/index.ts test/dialogue-controller.test.mjs test/index.test.mjs
git commit -m "feat: route pet input to selected DSH session"
```

### Task 4: 实现 DSH Web 设置页

**Files:**

- Create: `src/client.tsx`
- Create: `test/client-settings-model.test.mjs`
- Modify: `package.json`
- Modify: `test/package-layout.test.mjs`

- [ ] **Step 1: 写入浏览器模型失败测试。**

```js
test('uses the DSH session display title and never reads cwd', () => {
  const accessed = []
  const row = new Proxy({ id: 's-1', displayTitle: '现有会话', cwd: 'C:\\private' }, { get(target, key) { accessed.push(key); return target[key] } })
  assert.deepEqual(projectSessionOptions([row]), [{ id: 's-1', title: '现有会话' }])
  assert.equal(accessed.includes('cwd'), false)
})
```

- [ ] **Step 2: 运行测试，确认投影尚未覆盖 Proxy 行。**

Run: `npm run build; node --test test/client-settings-model.test.mjs`

Expected: FAIL，直到 `projectSessionOptions()` 接受最小 `id/displayTitle` 投影。

- [ ] **Step 3: 实现设置 section。**

`src/client.tsx` 以 DSH Web 的 `settings.section` 插槽注册“桌宠”页面。订阅 `ctx.sessions.list`，从 `getSnapshot().ids` 和 `byId[id]` 只构造 `{ id, displayTitle }` 行后调用 `projectSessionOptions()`；不得读取 `cwd` 或调用打开会话、加载历史的 API。

页面含一个 `<select>`（选项文本为 `title`，值为 `id`）、回复预览复选框以及 80–2000 的整数输入。改变任一字段时调用 DSH 既有 settings update API，并使用返回 revision 处理冲突；列表未就绪、选择已消失或写入失败时保留固定错误文案并刷新本地 settings snapshot。注册 Web 入口后，使 `package.json` 的 `./client` export、`dsh.client` 平台声明和打包文件都能让 DSH web profile 加载该模块；不添加任何 Remote/typert 生成物。

- [ ] **Step 4: 运行客户端模型和打包布局测试。**

Run: `npm test -- --test-name-pattern="session display title|package layout"`

Expected: PASS。

- [ ] **Step 5: 提交 Web 设置页。**

```powershell
git add src/client.tsx src/client-settings-model.ts test/client-settings-model.test.mjs package.json package-lock.json test/package-layout.test.mjs
git commit -m "feat: add DSH web dialogue settings"
```

### Task 5: 实现 WPF v4 对话状态与输入气泡

**Files:**

- Modify: `pet-helper/ProtocolMessage.cs`
- Modify: `pet-helper/ProtocolReader.cs`
- Modify: `pet-helper.Tests/ProtocolReaderTests.cs`
- Create: `pet-helper/ConversationState.cs`
- Create: `pet-helper.Tests/ConversationStateTests.cs`
- Modify: `pet-helper/MainWindow.xaml`
- Modify: `pet-helper/MainWindow.xaml.cs`
- Modify: `pet-helper/App.xaml.cs`
- Modify: `test/wpf-layout.test.mjs`

- [ ] **Step 1: 写入 C# 协议和缓冲失败测试。**

```csharp
[Fact]
public void Parses_v4_reply_preview_and_keeps_the_latest_configured_characters()
{
    var message = ProtocolReader.Parse("{\"version\":4,\"kind\":\"reply-preview\",\"requestId\":7,\"text\":\"abcdef\",\"completed\":false}");
    var state = new ConversationState(previewEnabled: true, previewMaxChars: 4);
    state.Apply(Assert.IsType<ReplyPreviewMessage>(message));
    Assert.Equal("cdef", state.PreviewText);
}

[Fact]
public void Clears_preview_when_disabled()
{
    var state = new ConversationState(previewEnabled: true, previewMaxChars: 80);
    state.Apply(new ReplyPreviewMessage(1, "visible", false));
    state.Apply(new ConversationConfigMessage(false, 80));
    Assert.Equal(string.Empty, state.PreviewText);
}
```

- [ ] **Step 2: 运行测试，确认 v3 阅读器不接受消息。**

Run: `dotnet test pet-helper.Tests\PetHelper.Tests.csproj --no-restore --filter "FullyQualifiedName~ProtocolReaderTests|FullyQualifiedName~ConversationStateTests"`

Expected: FAIL，提示缺少 v4 对话消息或 `ConversationState`。

- [ ] **Step 3: 实现 C# 严格协议、状态机和输入 UI。**

将所有 `ProtocolMessage` records 改为版本 4，并添加 `ConversationConfigMessage`、`InputStatusMessage`、`ReplyPreviewMessage`、`ClearPreviewMessage`。`ProtocolReader` 的行上限为 4096，并对每个新增 kind 执行与 TypeScript 完全相同的字段集合、枚举、整数和 2000 字符校验；无效消息返回 `null`，由现有断开路径处理。

`ConversationState` 接收有效消息，维护当前 request、固定状态文本、预览开关、字符上限和最近 N 字符。它绝不写文件或控制台。`MainWindow.xaml` 在图像上方加可折叠输入气泡与预览区域：TextBox、发送/关闭按钮、状态文本、有限高度 ScrollViewer。Enter 调用提交、Shift+Enter 换行、Esc/关闭清空；左键按下只有在未命中编辑控件时才 `DragMove()`。`MainWindow` 发出 `InputSubmitted(requestId, text)`；`App` 订阅该事件，序列化 `{"version":4,"kind":"input",...}` 后立即 Flush，且不记录文本。`ReadProtocolLoop` 派发新消息给窗口状态机。

- [ ] **Step 4: 运行 C# 测试与 XAML 布局测试。**

Run: `dotnet test pet-helper.Tests\PetHelper.Tests.csproj --no-restore; npm test -- --test-name-pattern="input bubble|state bubble"`

Expected: PASS。

- [ ] **Step 5: 提交 WPF 对话气泡。**

```powershell
git add pet-helper/ProtocolMessage.cs pet-helper/ProtocolReader.cs pet-helper/ConversationState.cs pet-helper/MainWindow.xaml pet-helper/MainWindow.xaml.cs pet-helper/App.xaml.cs pet-helper.Tests/ProtocolReaderTests.cs pet-helper.Tests/ConversationStateTests.cs test/wpf-layout.test.mjs
git commit -m "feat: add pet dialogue input and preview bubble"
```

### Task 6: 覆盖清理、回归与安全债验证

**Files:**

- Modify: `test/dialogue-controller.test.mjs`
- Modify: `test/helper-process.test.mjs`
- Modify: `pet-helper.Tests/ConversationStateTests.cs`
- Modify: `docs/security-debt.md`

- [ ] **Step 1: 添加清理与不泄露失败测试。**

```js
test('clears the preview on disabled, next input, session loss and helper close', () => {
  const sent = []
  const controller = new DialogueController(fakeDsh('s-1'), (message) => sent.push(message))
  controller.disablePreview()
  controller.acceptInput({ requestId: 2, text: 'later' })
  controller.sessionUnavailable('s-1')
  controller.helperClosed()
  assert.deepEqual(sent.filter((message) => message.kind === 'clear-preview').map((message) => message.reason), ['disabled', 'next-input', 'session-unavailable', 'closed'])
})
```

- [ ] **Step 2: 运行测试，确认缺少所有清理路径时失败。**

Run: `npm run build; node --test test/dialogue-controller.test.mjs`

Expected: FAIL，直到每种原因都发送一次 `clear-preview`。

- [ ] **Step 3: 完成清理与日志断言。**

在控制器、HelperProcess 停止钩子和 WPF 输入取消路径调用相应清理方法。对 fake Helper 集成测试捕获 stdout/stderr，断言用户输入和回复样例都不出现；仅断言消息 kind、请求号和长度。更新 `docs/security-debt.md`，记录验收结果和“上游 Remote 修复后改回 Host 投影”的关闭条件，不加入真实数据。

- [ ] **Step 4: 执行完整验证。**

Run:

```powershell
npm test
dotnet test pet-helper.Tests\PetHelper.Tests.csproj --no-restore
```

Expected: 两套测试均通过。

- [ ] **Step 5: 提交回归覆盖。**

```powershell
git add test/dialogue-controller.test.mjs test/helper-process.test.mjs pet-helper.Tests/ConversationStateTests.cs docs/security-debt.md
git commit -m "test: cover dialogue preview cleanup"
```

### Task 7: 构建、打包、安装与人工验证

**Files:**

- Modify: `package.json`
- Modify: `package-lock.json`
- Modify: `README.md`

- [ ] **Step 1: 更新版本与使用说明。**

将包版本从 `0.1.2` 递增为 `0.1.3`，同步 lockfile。README 添加：在 DSH 设置的“桌宠”页选择默认会话和预览长度；点击桌宠输入并发送；预览默认关闭；会话标题仅显示于本机 DSH Web。

- [ ] **Step 2: 在发布前让用户关闭桌宠。**

向用户说明 `runtime/bin/win32-x64/pet-helper.exe` 必须先通过右键菜单关闭；未获同意时不终止该进程。

- [ ] **Step 3: 构建、测试、打包。**

Run:

```powershell
npm test
dotnet test pet-helper.Tests\PetHelper.Tests.csproj --no-restore
npm run build:helper
npm run test:package
npm pack
```

Expected: 所有命令通过，生成 `dsh-png-pet-0.1.3.tgz`。

- [ ] **Step 4: 安装到 web profile。**

```powershell
New-Item -ItemType Directory -Force C:\dsh-packages
Copy-Item .\dsh-png-pet-0.1.3.tgz C:\dsh-packages\
dsh plugin --profile web remove dsh-png-pet
dsh plugin --profile web add C:\dsh-packages\dsh-png-pet-0.1.3.tgz
dsh plugin --profile web list
```

Expected: 列表显示 `dsh-png-pet@0.1.3`。

- [ ] **Step 5: 重启 Harness 并执行人工验收。**

重启已打开的 DSH Web Harness 后，依次确认：设置页列出会话标题；选择默认会话；预览默认关闭；点击桌宠可输入；发送后 DSH 收到消息；开启预览后只出现同一请求的可见文本；预览长度立即生效；取消输入、关闭预览和会话失效会清空气泡。

- [ ] **Step 6: 提交发布元数据。**

```powershell
git add package.json package-lock.json README.md
git commit -m "chore: release dialogue preview 0.1.3"
```
