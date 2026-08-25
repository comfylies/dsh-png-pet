# DSH 组合活动气泡 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 将同一顶层 DSH 会话的思考和工具执行状态组合为固定、安全的桌宠气泡文案。

**Architecture:** 事件适配器仅将白名单事件转为脱敏的活动开始/结束事实；Reducer 按 Session 保存思考标记、进行中工具计数和独占状态，选择单个顶层候选。Host 将规范化活动数组编码为 v3 JSON Lines，Helper 用同一固定映射验证并显示标签。

**Tech Stack:** TypeScript/Node.js 22、node:test、C#/.NET 10 WPF、xUnit、PowerShell 打包脚本。

---

## 文件结构

- src/companion-reducer.ts：按 Session 维护组合活动、独占状态和跨 Session 选择规则。
- src/dsh-event-adapter.ts：将白名单工具开始/结束事件转为脱敏事实。
- src/companion-bridge.ts：把 Reducer 展示模型转为规范化 v3 Host 状态消息。
- src/protocol.ts：v3 JSON Lines 类型、固定标签派生和严格校验。
- test/companion-reducer.test.mjs、test/dsh-event-adapter.test.mjs、test/companion-bridge.test.mjs、test/protocol.test.mjs、test/helper-process.test.mjs、test/fixtures/fake-helper.mjs：Node 的红绿测试与 v3 握手夹具。
- pet-helper/ProtocolMessage.cs、pet-helper/ProtocolReader.cs、pet-helper/PetDisplayState.cs、pet-helper/App.xaml.cs：v3 Helper 协议、活动数组验证和气泡展示。
- pet-helper.Tests/ProtocolReaderTests.cs、pet-helper.Tests/PetDisplayStateTests.cs、test/packaged-helper.test.mjs：C# 与发布 exe 的端到端回归。
- package.json、package-lock.json、README.md：发布版本与 v3 使用说明。

### Task 1: 先用测试定义组合活动的 Reducer 规则

**Files:**
- Modify: test/companion-reducer.test.mjs
- Modify: src/companion-reducer.ts

- [ ] **Step 1: 写出会失败的组合活动和计数测试**

用以下测试替换旧的单一 working 断言；保留子 Agent、乱序、释放和终态相关覆盖，并让期望展示模型包含 activities：

~~~js
test('keeps thinking visible while a tool is running', () => {
  const reducer = new CompanionReducer()
  reducer.apply({ sessionId: 'root', seq: 1, isSubagent: false, kind: 'thinking' })

  assert.deepEqual(
    reducer.apply({ sessionId: 'root', seq: 2, isSubagent: false, kind: 'work-start' }),
    { state: 'active', activities: ['thinking', 'working'], sequence: 2, terminal: false },
  )
})

test('removes only the completed tool activity and clamps unmatched finishes', () => {
  const reducer = new CompanionReducer()
  reducer.apply({ sessionId: 'root', seq: 1, isSubagent: false, kind: 'thinking' })
  reducer.apply({ sessionId: 'root', seq: 2, isSubagent: false, kind: 'work-start' })
  reducer.apply({ sessionId: 'root', seq: 3, isSubagent: false, kind: 'work-start' })
  reducer.apply({ sessionId: 'root', seq: 4, isSubagent: false, kind: 'work-finish' })
  assert.deepEqual(reducer.current(), { state: 'active', activities: ['thinking', 'working'], sequence: 4, terminal: false })
  assert.deepEqual(
    reducer.apply({ sessionId: 'root', seq: 5, isSubagent: false, kind: 'work-finish' }),
    { state: 'active', activities: ['thinking'], sequence: 5, terminal: false },
  )
})
~~~

再增加：waiting 独占于组合活动之上；含 working 的候选优于仅 thinking；turn/end 清空活动；默认仍忽略子 Agent。

- [ ] **Step 2: 运行 Reducer 测试，确认其为红色**

Run: npm run build; node --test test/companion-reducer.test.mjs

Expected: FAIL，因为旧 SessionFact.kind 不接受 work-start/work-finish，且旧 Presentation 没有 activities。

- [ ] **Step 3: 以最小模型实现组合活动**

在 src/companion-reducer.ts 定义如下稳定边界：

~~~ts
export type Activity = 'thinking' | 'working'
export type ReducibleState = 'thinking' | 'work-start' | 'work-finish' | 'waiting' | 'success' | 'error' | 'idle'
export type PresentationState = 'active' | 'idle' | 'waiting' | 'success' | 'error'

export type Presentation = {
  state: PresentationState
  activities: readonly Activity[]
  sequence: number
  terminal: boolean
}
~~~

每个 Session 记录 seq、thinking、非负 workingCount 和可选独占状态。thinking 清除 waiting；work-start 增加计数且不清除思考；work-finish 用 Math.max(0, count - 1) 减少计数；waiting、success、error 为独占；success/error 清空活动。current() 的优先级必须为 waiting=5、error=4、活动含 working=3、仅 thinking=2、success=1、idle=0；并列取最大 seq。disposeTerminal 仅删除 success/error 记录。

- [ ] **Step 4: 运行 Reducer 测试，确认其为绿色**

Run: npm run build; node --test test/companion-reducer.test.mjs

Expected: PASS，组合测试、并行计数、优先级、子 Agent 和释放测试均通过。

- [ ] **Step 5: 提交纯 Reducer 改动**

~~~powershell
git add -- src/companion-reducer.ts test/companion-reducer.test.mjs
git commit -m "feat: reduce composite companion activities"
~~~

### Task 2: 将 DSH 工具事件保留为活动边界，并生成固定组合标签

**Files:**
- Modify: test/dsh-event-adapter.test.mjs
- Modify: test/companion-bridge.test.mjs
- Modify: test/index.test.mjs
- Modify: src/dsh-event-adapter.ts
- Modify: src/companion-bridge.ts

- [ ] **Step 1: 写会失败的适配器与桥接测试**

~~~js
assert.deepEqual(
  adaptSessionEvent(topLevelSession, { type: 'tool/call', seq: 7, data: { arguments: { ignored: true } } }),
  { sessionId: 'root', seq: 7, isSubagent: false, kind: 'work-start' },
)
assert.deepEqual(
  adaptSessionEvent(topLevelSession, { type: 'tool/result', seq: 8, data: { ignored: true } }),
  { sessionId: 'root', seq: 8, isSubagent: false, kind: 'work-finish' },
)

const sent = []
const bridge = new CompanionBridge((message) => sent.push(message))
bridge.apply({ sessionId: 'root', seq: 1, isSubagent: false, kind: 'thinking' })
bridge.apply({ sessionId: 'root', seq: 2, isSubagent: false, kind: 'work-start' })
assert.deepEqual(sent.at(-1), {
  kind: 'state', state: 'active', activities: ['thinking', 'working'], label: '思考中/工作中', sequence: 2,
})
~~~

- [ ] **Step 2: 运行定向测试，确认其为红色**

Run: npm run build; node --test test/dsh-event-adapter.test.mjs test/companion-bridge.test.mjs test/index.test.mjs

Expected: FAIL，因为适配器仍产生单状态，桥接尚未发送 activities。

- [ ] **Step 3: 实现白名单活动映射和桥接标签派生**

在 src/dsh-event-adapter.ts 将 tool/call、tool/code-dispatch-start 映射为 work-start，将 tool/result、tool/code-dispatch 映射为 work-finish；普通白名单事件映射 thinking，终态映射保持不变。除 turn/end.data.reason.kind 外，绝不读取 event.data。

在 src/companion-bridge.ts 从协议模块导入 labelForPresentation，并发送：

~~~ts
this.send({
  kind: 'state',
  state: presentation.state,
  activities: [...presentation.activities],
  label: labelForPresentation(presentation.state, presentation.activities),
  sequence: presentation.sequence,
})
~~~

samePresentation 必须逐项比较 activities，避免组合内容改变时误去重。

- [ ] **Step 4: 运行定向测试，确认其为绿色**

Run: npm run build; node --test test/dsh-event-adapter.test.mjs test/companion-bridge.test.mjs test/index.test.mjs

Expected: PASS，工具开始/结束不转发 data，能输出固定的思考中/工作中。

- [ ] **Step 5: 提交事件和桥接改动**

~~~powershell
git add -- src/dsh-event-adapter.ts src/companion-bridge.ts test/dsh-event-adapter.test.mjs test/companion-bridge.test.mjs test/index.test.mjs
git commit -m "feat: bridge composite tool activities"
~~~

### Task 3: 将 Host/Helper 协议同步升级到 v3

**Files:**
- Modify: test/protocol.test.mjs
- Modify: test/helper-process.test.mjs
- Modify: test/fixtures/fake-helper.mjs
- Modify: test/packaged-helper.test.mjs
- Modify: src/protocol.ts
- Modify: pet-helper/ProtocolMessage.cs
- Modify: pet-helper/ProtocolReader.cs
- Modify: pet-helper/PetDisplayState.cs
- Modify: pet-helper/App.xaml.cs
- Modify: pet-helper.Tests/ProtocolReaderTests.cs
- Modify: pet-helper.Tests/PetDisplayStateTests.cs

- [ ] **Step 1: 写 v3 协议的失败测试**

Node 测试精确断言：

~~~js
assert.equal(
  encodeHostMessage({ kind: 'state', state: 'active', activities: ['thinking', 'working'], label: '思考中/工作中', sequence: 42 }),
  '{"version":3,"kind":"state","state":"active","activities":["thinking","working"],"label":"思考中/工作中","sequence":42}\n',
)
assert.throws(
  () => parseHostMessage('{"version":3,"kind":"state","state":"active","activities":["working","thinking"],"label":"思考中/工作中","sequence":1}'),
  /activities/,
)
~~~

将 fake Helper 的 ready/closed 与 HelperProcess 期望改为 v3。发布 Helper 回归也改用 v3：收到 ready 后先写入 hello、config 和下面的状态行，等待 250ms 后再写入 shutdown，并断言 Helper 在等待期间没有退出。

~~~js
child.stdin.write('{"version":3,"kind":"state","state":"active","activities":["thinking","working"],"label":"思考中/工作中","sequence":1}\\n')
~~~

C# 增加 Parses_a_composite_active_state，并拒绝 v2、重复活动、乱序活动、active 错配标签和额外字段。

- [ ] **Step 2: 运行 Node 与 C# 协议测试，确认其为红色**

Run: npm run build; node --test test/protocol.test.mjs test/helper-process.test.mjs; node --test test/packaged-helper.test.mjs; dotnet test pet-helper.Tests\PetHelper.Tests.csproj --no-restore --filter "FullyQualifiedName~ProtocolReaderTests|FullyQualifiedName~PetDisplayStateTests"

Expected: FAIL，因为实现仍只接受 v2 单一 state。

- [ ] **Step 3: 实现 v3 的严格、安全协议**

在 src/protocol.ts 设 PROTOCOL_VERSION = 3，公开：

~~~ts
export type Activity = 'thinking' | 'working'
export type State = 'active' | 'idle' | 'waiting' | 'success' | 'error' | 'disconnected'
export type StateOutboundMessage = {
  kind: 'state'
  state: State
  activities: readonly Activity[]
  label: string
  sequence: number
}
~~~

labelForPresentation(active, [thinking, working]) 只以固定活动标签用 / 连接；独占状态必须使用空数组和固定标签。validateHostMessage 必须要求 version、kind、state、activities、label、sequence 六个字段：activities 仅对 active 非空、只含允许值、不重复、严格按 thinking/working 排序，label 必须完全匹配派生值。

C# 中将协议记录版本改为 3；ProtocolReader.ParseState 要求同样六字段，枚举活动数组并验证顺序与去重。PetDisplayState 对 active 只接纳思考、工作和组合三种固定标签，其他状态仍是一对一固定标签；App.SerializeHelperMessage 输出 v3。MainWindow 继续只对 idle 隐藏气泡，并保留 waiting 的醒目颜色。

- [ ] **Step 4: 运行 Node 与 C# 协议测试，确认其为绿色**

Run: npm run build; node --test test/protocol.test.mjs test/helper-process.test.mjs; dotnet test pet-helper.Tests\PetHelper.Tests.csproj --no-restore --filter "FullyQualifiedName~ProtocolReaderTests|FullyQualifiedName~PetDisplayStateTests"; npm run build:helper; npm run test:package

Expected: PASS，v3 组合状态可编码/解析；v2、非规范数组和自由文本均被拒绝。

- [ ] **Step 5: 提交协议同步改动**

~~~powershell
git add -- src/protocol.ts test/protocol.test.mjs test/helper-process.test.mjs test/fixtures/fake-helper.mjs test/packaged-helper.test.mjs pet-helper/ProtocolMessage.cs pet-helper/ProtocolReader.cs pet-helper/PetDisplayState.cs pet-helper/App.xaml.cs pet-helper.Tests/ProtocolReaderTests.cs pet-helper.Tests/PetDisplayStateTests.cs
git commit -m "feat: support composite activity protocol"
~~~

### Task 4: 制作可安装包

**Files:**
- Modify: package.json
- Modify: package-lock.json
- Modify: README.md

- [ ] **Step 1: 更新发布元数据和说明**

将 package.json 与 package-lock.json 的根包版本从 0.1.1 同步改为 0.1.2。README.md 加入：气泡可显示固定思考中/工作中，协议为 v3，所有状态文本均由插件固定映射生成而非 DSH 会话内容。

- [ ] **Step 2: 全量验证并打包**

Run:

~~~powershell
npm test
dotnet test pet-helper.Tests\PetHelper.Tests.csproj --no-restore
npm run build:helper
npm run test:package
npm pack
~~~

Expected: 所有 Node 与 C# 测试通过，生成 dsh-png-pet-0.1.2.tgz；宣布完成前记录每项实际通过结果。

- [ ] **Step 3: 提交发布准备改动**

~~~powershell
git add -- package.json package-lock.json README.md
git commit -m "chore: prepare composite activity release"
~~~

### Task 5: 经用户同意更新正在运行的 DSH

**Files:**
- No repository file changes.

- [ ] **Step 1: 取得运行中 Harness 重装授权**

说明新协议需要重启 DSH web Harness；只在用户明确同意后继续。不得读取 DSH 日志、Session 或工具内容。

- [ ] **Step 2: 安装无空格路径中的新包并重启**

~~~powershell
New-Item -ItemType Directory -Force C:\dsh-packages
Copy-Item .\dsh-png-pet-0.1.2.tgz C:\dsh-packages\
dsh plugin --profile web remove dsh-png-pet
dsh plugin --profile web add C:\dsh-packages\dsh-png-pet-0.1.2.tgz
dsh plugin --profile web list
~~~

随后按已同意的方式重启 Harness，让用户触发一个明显较慢的工具调用，并确认气泡显示思考中/工作中。

- [ ] **Step 3: 报告安装结果**

报告包版本、自动化验证和用户可见的组合状态；不要报告、收集或转述 Session 内容。
