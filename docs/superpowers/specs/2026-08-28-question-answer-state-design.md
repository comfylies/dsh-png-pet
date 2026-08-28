# 「对话提问等待用户回答」状态设计

## 背景与目标

桌宠目前唯一的「等待用户」状态是 `waiting`，且只绑定工具审批（`approval/asked`，标题「等待你的操作」）。当 DSH 的 Agent 在对话中向用户提问并等待回答时，桌宠没有可辨识的状态，用户看不出它在等你回答。

本设计新增一个**用户可见**的「等你回答…」状态，让用户能区分三种等待：**等审批**、**等回答一个提问**、**完全空闲**。

## 现状与差距（事实确认）

| 场景 | Harness 会话事件 | 桌宠当前呈现 |
|---|---|---|
| ① 工具审批等待 | `approval/asked` | `waiting` / 「等待你的操作」 ✅ |
| ② Agent 提问工具停在半路等回答 | 只有**未闭合的 `tool/call`**，**不产生任何提问/回答会话事件** | `active` / 「工作中…」 ⚠️（被当成在干活） |
| ③ Agent 文字提问后正常收尾等用户下一条 | `assistant/message` → `turn/end(completed)` | `success` → `idle` ❌ |

关键事实：`ask_user_question` 走 `ctx.userQuestions.ask()`，是 UI-backed 服务，**服务层不往会话追加事件**；`SessionEventMap`（`@deepseek-ai/dsh-session-reference`）里也没有任何 question / exchange 类事件。因此在事件流上「提问等待回答」与「正在工作」对桌宠来说是同一件事。

## 关键：信号来源

唯一可靠信号是**识别 `tool/call` 的工具名**。当 `tool/call` 的 `data.name` 属于提问工具时，它不再是「执行中的工作」，而是「等回答的提问」，应映射为新的等待状态而非 `work-start`。

- 目标工具名：`ask_user_question`（来自 `@deepseek-ai/dsh-tool-ask-user`）。
- 实现建议：用**常量表**匹配工具名（注意命名空间前缀，按实际 `tool/call.data.name` 为准），不要把字符串散落在逻辑里。

## 状态模型改动

### TypeScript（src）

**`src/companion-reducer.ts`**
- `ReducibleState` 新增 `'question'`。
- 视为**排他等待态**：`exclusive = 'question'`，`terminal = false`；进入时清空 `thinking` / `responding` / `workingCount`。
- **退出规则**（关键）：当该会话后续收到非 `question` 的活动事件（如 `tool/result`、`assistant/chunk`、`assistant/message`），必须清除 `exclusive` 并恢复 `thinking`/`responding`，否则会卡在 `question`。参考现有 `success`/`error` 的排他守卫，为 `question` 增加「仅被后续活动清除」的转移。
- 优先级：`question` 属提示型排他态，应低于活动态（`working`/`responding`/`thinking`），高于 `success`；与 `waiting`（审批）同档或略低，避免二者互相抢显。`present()` 的 rank 需给出明确数值。

**`src/dsh-event-adapter.ts`**
- `tool/call` 分支先判工具名；命中提问工具 → `kind: 'question'`，否则维持 `work-start`。
- 对应 `tool/result`（可选）用于退出 `question` 时恢复为 `thinking`（Agent 继续处理回答）。

**`src/protocol.ts`**
- `displayLabels` 新增 `question: '等你回答…'`（`State` 类型随之扩展，排他态无 `activities`）。
- 确认 `isCanonicalActivities` / `labelForPresentation` 对新排他态放行（参考现有 `waiting` 处理）。

### Helper / 动画

**`pet-helper/PetDisplayState.cs`**
- `AnimationKey` 新增 `("question", "等你回答…")` 分支。方案甲映射到 `PetAnimationKey.Waiting`；方案乙映射到新增的 `Question` 键。
- `From()` 校验需接受新状态与标题（保持「标题与状态严格匹配、否则视为 Disconnected」的既有语义）。
- 气泡文案：`state == "question"` 时显示「等你回答…」。

**`pet-helper/Assets/pet-animations.json`**
- 方案甲：`question` 复用 `waiting` 的动作（回退 `idle`）。
- 方案乙：新增 `question` 动作键（含独立动画目录，回退 `waiting` → `idle`）。

## 与审批 `waiting` 的区分

- **方案甲（推荐，改动小）**：新增 `question` 状态，但**复用 `Waiting` 动画**，仅靠气泡标题区分（「等你回答…」 vs 「等待你的操作」）。
- **方案乙（视觉完全独立）**：新增 `question` 状态 + **独立动画键/资源**，与审批、空闲三种等待视觉完全分开。

## 边界情况

- **提问被取消 / abort**：`ask_user_question` 以错误或工具返回结束，必须正确退出 `question` 回到 `error`/`idle`，不能卡在「等你回答…」。
- **场景③ 文字提问收尾等回复**：Harness 端无可靠信号（无法可靠区分「问完收尾等回复」与「干完收尾」），**默认不做**（维持 `success`→`idle`）。如确需做，只能靠约定推断（如 `assistant/message` 以问号结尾等），属低可靠性方案，不建议。
- **多 Session / 子代理**：`question` 纳入现有优先级与 `includeSubagents` 逻辑，沿用 `CompanionReducer` 已有骨架。

## 协议与 Helper 校验

- `labelForPresentation` / `isCanonicalActivities` 需对 `question` 排他态（无 `activities`）放行，并拒绝未知/混合状态。
- C# `PetDisplayState.From()` 与 `AnimationKey` 需要同步感知新状态，保持「标签不匹配 → Disconnected」的既有约束。

## 验收

- Agent 调用 `ask_user_question` 时，桌宠气泡显示「等你回答…」（`question` 状态）。
- 用户回答后，桌宠离开 `question`，恢复 `thinking`/`working`/`responding`。
- 提问被取消时，正确落到 `error`/`idle`，不残留 `question`。
- 三种等待（等审批 / 等你回答 / 空闲）在气泡上可区分。
- TypeScript 与 C# 都拒绝未知状态、未知活动标签与标签不匹配载荷。
- 事件适配器、日志与 JSON Lines 载荷中不包含提问/回答文本。

## 待决策（交由 Codex 落地时确认）

1. 方案甲（复用 `Waiting` 动画，仅标题区分）还是方案乙（独立 `question` 动画）？
2. 场景③（文字提问收尾等回复）是否实现？默认**不做**。
