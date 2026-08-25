# DSH PNG 桌宠

Windows-only DeepSeek Harness（DSH）桌宠插件。DSH 负责会话和模型；此插件只启动本地 WPF 桌宠 Helper，并通过 stdin/stdout JSON Lines 通信。

## 开发环境

- Node.js 22.19 或更新版本
- .NET 10 SDK
- DeepSeek Harness CLI（仅用于实际集成验证）

```powershell
npm install
npm test
dotnet test pet-helper.Tests\PetHelper.Tests.csproj --no-restore
npm run build:helper
npm run test:package
```

构建结果位于 `runtime/bin/win32-x64/pet-helper.exe`，并以 .NET 自包含单文件形式发布。

## 本机桌宠交互

直接运行 `runtime/bin/win32-x64/pet-helper.exe` 后：

- 按住角色左键拖动可移动位置；位置会在下次启动时恢复。
- 右键可选择 75%、100%、125% 或 150% 缩放，也可重置大小和位置。
- 右键“隐藏”后，双击通知区域的 `DSH PNG 桌宠` 图标或选择“显示桌宠”可恢复。
- 右键“关闭桌宠”或托盘“退出桌宠”会彻底结束进程。
- 单击桌宠可输入消息并发送到“设置 → 桌宠”中选择的 DSH 会话；发送状态会显示在气泡中。
- 可在同一设置页开启回复预览，并将预览长度设置为 80–2000 个字符（默认关闭、480 字符）。

## 隐私边界

该插件不读取 API Key、不直接请求模型服务、不创建 HTTP 或 WebSocket 端口。桌宠输入与可选的模型回复预览仅经本地 stdin/stdout JSON Lines 暂存，不写入日志、文件或设置；预览只处理关联回合的普通文本增量。

v3 状态气泡仅支持固定组合`思考中/工作中`；活动文本来自插件固定枚举映射，绝不来自 DSH 会话正文。
