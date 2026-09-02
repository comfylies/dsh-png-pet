# DSH PNG 桌宠：AI 快速上下文

## 项目是什么

`dsh-png-pet` 是一个 **Windows 10/11 x64 专用** 的 DeepSeek Harness（DSH）插件，而不是独立聊天客户端。

DSH 仍是会话、模型、凭据、Agent 和 Web UI 的唯一拥有者；本项目把真实 DSH 活动安全地映射成一个 WPF PNG 桌宠，并提供受限的会话对话窗口。

```text
DSH Host / Web profile
  └─ TypeScript 插件（src/）
      ├─ DSH 事件、会话、设置、目标选择、对话编排
      └─ stdin/stdout JSON Lines（唯一 IPC）
          └─ C# WPF Helper（pet-helper/，唯一可见 UI）
              ├─ 透明 PNG 桌宠、动画、状态气泡、托盘
              ├─ 对话窗口、目标选择卡、随机聊聊邀约
              └─ 仅保存窗口布局；不保存对话内容
```

当前源码版本为 `dsh-png-pet@0.1.55`，JSON Lines 协议为 **v11**（以 `package.json` 和 `src/protocol.ts` 为准）。已运行的 DSH Harness 必须重启才会加载新安装的包。

## 绝不能突破的边界

- 仅支持 Windows 10/11 x64；不要添加跨平台兼容层、独立 Electron/WebView/Tauri 应用或额外服务。
- TypeScript 与 Helper **只能**经父子进程 stdin/stdout JSON Lines 通信；禁止 HTTP、WebSocket、命名端口或其他监听端口。
- 不读取、持久化、记录、输出或转发 API Key、授权头、提示词、工具参数、工具结果、文件路径或 DSH 凭据。
- 对话输入、回复、预览和按需读取的最近历史仅可短暂存在于本地 JSON Lines/内存和 UI 中；不得写入文件、设置或日志，也不得发送到项目自身网络。
- 不展示 reasoning；历史只投影用户文本、助手可见文本，以及经限长处理的图片/工具占位。
- Helper 不联网、不读取 DSH 数据文件、不承担 Agent 逻辑。模型和工具调用一律由 DSH 提供的能力执行。
- 网页、DSH 事件与 Helper 输入都视为不可信。协议字段须严格白名单、校验版本/类型/长度；未知输入安全失败，不影响 DSH Host。

`docs/security-debt.md` 记录两项已接受但必须保留边界的兼容性债务：本地 Web 设置页直接处理会话 ID/标题，以及部分 DSH profile 不暴露第三方设置命名空间。不要用自建 Remote 或 HTTP 端点绕过它们。

## 当前能力与仍缺项目

已实现：

- Helper 生命周期与有序关闭；透明置顶窗口、拖动、位置恢复、缩放、隐藏/托盘恢复、右键菜单与状态气泡。
- 真实 DSH Session/Agent 事件适配、状态归约和多 Session 优先级；状态为 `waiting > error > working > thinking > success > idle`，默认不让子 Agent 抢占。
- 目录式 v3 PNG Clip 清单、完整 Clip 播放、状态转换、减少动态效果回退。
- DSH Web 设置页及会话对话：目标会话、流式回复、历史按需加载、停止、Markdown 完成态渲染、图片/文件附件和错误/中断收尾。
- 目标选择卡：工作区 → 会话、未分组会话、现有空白会话复用、创建会话、从选定目录注册工作区。
- 显式选择工作区、显式开启后才显示的随机聊聊邀约；点击后才创建隔离会话，可独立允许 DSH 配置的网页工具。

尚未完成或应先确认设计再做：

- 从菜单“打开 DSH”。
- “本次关闭”抑制当前 Host 生命周期内自动重启。
- Helper 崩溃的受限退避重启。
- 将 Helper 菜单的缩放/减少动态效果回写并持久化到 DSH 设置（当前设置页已能配置并下发）。
- 第二阶段“主动后台检索、生成事实性邀约”未获实现授权；`docs/第二阶段-主动联网随机聊聊设计.md` 只是未来设计，不能据此加入后台联网。

## 关键代码地图

| 目标 | 入口文件 |
| --- | --- |
| 插件装配、Helper 启动、DSH service 注入 | `src/index.ts` |
| JSON Lines 类型、v11 校验与安全上限 | `src/protocol.ts` |
| Helper 子进程启动/握手/发送/停止 | `src/helper-process.ts` |
| DSH 事件 → 安全事实 → 桌宠状态 | `src/dsh-event-adapter.ts`、`src/companion-reducer.ts`、`src/companion-bridge.ts` |
| 会话输入、流式回复、取消与历史投影 | `src/dialogue-controller.ts`、`src/dialogue-history.ts` |
| DSH 设置和 Web 设置页 | `src/dialogue-settings.ts`、`src/client-settings-model.ts`、`src/client.tsx` |
| 工作区/会话选择及 DSH API 包装 | `src/target-controller.ts`、`src/target-selection.ts`、`src/target-service.ts` |
| 随机聊聊的显式点击流程 | `src/random-chat-controller.ts` |
| WPF 应用生命周期与标准输入读取 | `pet-helper/App.xaml.cs`、`pet-helper/ProtocolReader.cs` |
| 桌宠窗口、状态、动画与托盘 | `pet-helper/MainWindow.xaml(.cs)`、`PetDisplayState.cs`、`PetStateAnimationCoordinator.cs`、`PetAnimationPlayer.cs`、`PetTrayIcon.cs` |
| 对话/目标选择窗口 | `pet-helper/DialogueWindow.xaml(.cs)`、`TargetWindow.xaml(.cs)` |
| 发布自包含 exe | `scripts/build-helper.ps1` |

扩展协议时必须在 **TypeScript 类型/解析器、C# `ProtocolMessage`/读取器、两端调用点和测试** 同步修改；先升级版本，再拒绝旧/未知版本，不能默默兼容错误载荷。

## 核心行为决策

- `defaultSessionId` 是唯一持久化的对话目标事实源；`defaultWorkspaceId` 仅为目标选择卡的派生落点提示，绝不能代替会话目标。
- 对话窗口是 IM 式的 user/assistant 同屏列表。选择会话立即按需加载该会话受限历史；流式增量按 60ms 节流，用户上滚时停止自动滚动。
- 取消调用 DSH `agent.cancel`；`turn/end` 必须映射为 `aborted`、`interrupted` 或 `failed`，且无文本结果不能渲染为空白完成消息。
- 图片经 `ctx.attachments.saveImage` 上传 DSH；文件附件仅作为受限提示输入。不要把附件内容、路径或原始 Base64 写到日志、设置或测试快照。
- 随机聊聊默认关闭；仅在 Helper 气泡被用户点击、已选工作区且独立联网同意开启后，才创建新会话并让 DSH 使用其已配置网页工具。它不自行调度、搜索或创建会话。

## 动画与形象资产

- 根清单：`pet-helper/Assets/pet-animations.json`；每个状态目录的 `animation.json` 声明 Clip。
- 帧目录格式：`pet-helper/Assets/Animations/<状态键>/<片段键>/`。缺失状态必须回退 `idle`。
- 所有帧必须是透明 PNG、同一画布大小、脚底基线一致。每个 v3 Clip 必须声明首帧头顶的归一化 `statusAnchor`。
- 主形象必须同时更新 `pet-helper/Assets/placeholder-a.png` 与 `assets/placeholder-a.png`，确认 SHA-256 相同且保留 alpha。
- 视频帧先运行 `scripts/remove-backgrounds.ps1`，只写到独立输出目录，绝不覆盖原图；二进制资源替换需用户明确授权。

## 开发、测试与发布

前置条件：Node.js `>=22.19`、.NET 10 SDK；真实集成验证还需 DSH。先执行 `git status --short`，绝不覆盖用户已有改动。

```powershell
npm test
dotnet test pet-helper.Tests\PetHelper.Tests.csproj --no-restore
npm run build:helper
npm run test:package
npm pack
```

测试优先：修改行为前先阅读对应 `docs/superpowers/specs/` 与 `docs/superpowers/plans/`，添加或调整最贴近模块的 Node/C# 测试，再实现并运行相关测试；变更协议、打包、资源或生命周期时运行完整验证序列。

发布前还要：

1. 同步递增 `package.json` 与 `package-lock.json` 的版本。
2. 确保 `runtime/bin/win32-x64/pet-helper.exe` 未被运行中的桌宠锁定。应请用户从右键菜单关闭；只有用户明确同意才能终止进程。
3. 因工作区路径含空格，将 `.tgz` 复制到 `C:\dsh-packages` 后安装。当前终端不保证全局 npm bin 在 PATH 中，调用 CLI 使用 `C:\Users\root\AppData\Roaming\npm\dsh.cmd`。

```powershell
New-Item -ItemType Directory -Force C:\dsh-packages
Copy-Item .\dsh-png-pet-<version>.tgz C:\dsh-packages\
& C:\Users\root\AppData\Roaming\npm\dsh.cmd plugin --profile web remove dsh-png-pet
& C:\Users\root\AppData\Roaming\npm\dsh.cmd plugin --profile web add C:\dsh-packages\dsh-png-pet-<version>.tgz
& C:\Users\root\AppData\Roaming\npm\dsh.cmd plugin --profile web list
```

`pet-helper.exe` 必须继续以 .NET 10 自包含单文件发布，并保留 `IncludeNativeLibrariesForSelfExtract=true`。不要删除项目中的 `NuGet.Config`、`Directory.Build.props` 或 `Directory.Build.targets`；它们规避了机器级失效的 NuGet fallback 配置。`App.xaml.cs` 也会从有效的 `SystemRoot` 补齐子进程所需的 `WINDIR`。

## 文档优先级

运行代码、测试、`package.json` 与 `src/protocol.ts` 是当前行为和版本的事实来源。`README.md` 是用户安装说明；`docs/项目需求书.md` 是产品/安全边界；`docs/superpowers/specs/` 和 `plans/` 是特性设计依据。`docs/项目进度总结.md`、`docs/实施计划.md` 等早期阶段文档含历史版本和已完成项，不应用来判断当前实现状态，修改它们前先核对源码。

Git 可能报告无法读取 `C:\Users\root\.config\git\ignore`；这是本机权限警告，不要为它修改项目文件。
