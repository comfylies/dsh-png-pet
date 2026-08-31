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

## 安装到 DSH Harness

先完成构建并生成 npm 包：

```powershell
npm run build:helper
npm pack
```

DSH CLI 对含空格的插件包路径解析不稳定。请将生成的 `dsh-png-pet-<version>.tgz` 复制到无空格路径后，再安装到 Web profile：

```powershell
New-Item -ItemType Directory -Force C:\dsh-packages
Copy-Item .\dsh-png-pet-<version>.tgz C:\dsh-packages\

# 更新前先从桌宠右键菜单选择“关闭桌宠”，避免 Windows 锁定 Helper。
& C:\Users\root\AppData\Roaming\npm\dsh.cmd plugin --profile web remove dsh-png-pet
& C:\Users\root\AppData\Roaming\npm\dsh.cmd plugin --profile web add C:\dsh-packages\dsh-png-pet-<version>.tgz
& C:\Users\root\AppData\Roaming\npm\dsh.cmd plugin --profile web list
```

确认列表显示目标版本后，重启 DSH Harness；已运行的 Harness 不会自动加载更新。`.tgz` 是本地安装产物，已由 `.gitignore` 排除，不应提交到仓库。如果不知道怎么安装，可以让 AI 帮忙。

## 双击启动桌宠

安装并启用插件后，可以直接双击项目根目录的 `启动 DSH 桌宠.vbs`。它会隐藏控制台并启动 `dsh web --no-open`，因此不会自动打开浏览器；桌宠会在 DSH 完成插件加载后出现。

启动器优先使用全局安装的 DSH：

```powershell
npm install -g @deepseek-ai/dsh
```

未检测到全局安装时，它会回退使用 `npx -y @deepseek-ai/dsh`。首次运行需要网络下载，日常使用仍建议全局安装以固定并加快启动。

如需在桌面创建一个真正的 Windows 快捷方式（`.lnk`），在项目根目录执行一次：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\new-dsh-pet-shortcut.ps1
```

脚本默认创建 `桌面\启动 DSH 桌宠.lnk`，如果同名快捷方式已存在会停止而不会覆盖。该快捷方式只负责启动 DSH；请从 DSH 的退出操作退出 Host，桌宠会随之有序关闭。

## 动画资源

- 动作清单位于 `pet-helper/Assets/pet-animations.json`。
- 用户提供的透明 PNG 帧放入 `pet-helper/Assets/Animations/<动作键>/`，并在清单中按顺序引用；动作在对应状态持续时只在本地循环播放。
- 每个动作可有 1–64 张帧；帧间隔由 Helper 按 `round(1000 / 帧数)` 毫秒独立计算（8 张为 125 ms）。单帧动作静态显示，不需要用重复图片补足 8 张。
- 每个包含帧的 v3 Clip 必须声明 `statusAnchor`，例如 `"statusAnchor": { "x": 0.5, "y": 0.12 }`。`x`、`y` 是 0–1 的归一化坐标，表示该 Clip 统一画布中角色头顶的中点；状态气泡会以该点为基准显示在头顶上方。
- 所有帧应导出到相同尺寸的透明画布，并以脚底作为统一基线。`statusAnchor` 只按 Clip 的第一帧头顶测量一次，状态气泡在整段动画中固定不动，因此呼吸、眨眼和轻微上下浮动不会让气泡抖动。旧版 v1 清单继续以默认锚点兼容读取；新增坐姿、趴姿或跳跃 Clip 时，只需按首帧的头部位置调整 `statusAnchor`。
- 缺少动作素材时会回退到 `idle`；开启减少动态效果时固定显示动作的第一帧。
- 仓库当前仅包含默认 `idle` 素材，尚未提供其他动作帧。
- 添加 PNG 或修改清单后，运行 `npm run build:helper` 和 `npm pack`，重新安装生成的插件包并重启 DSH Harness；源资源不会动态部署到已运行的 Harness。

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

协议 v5 的状态气泡只支持固定活动标签：`思考中…`、`工作中…`、`思考中/工作中` 和 `输出中…`；活动文本来自插件固定枚举映射，绝不来自 DSH 会话正文。仅 DSH 的用户可见文本块（`text-delta` 或 `text`）显示“输出中…”；思维链、工具块和未知形状仍显示“思考中…”。新的工具调用会立即切回工作状态。
