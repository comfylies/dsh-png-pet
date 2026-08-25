# DSH PNG 桌宠第一阶段 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use `executing-plans` to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 构建可由 DSH 加载的 Windows 桌宠插件骨架，并验证它能启动、握手和关闭自包含 WPF Helper。

**Architecture:** npm 包通过 `cordis.patch.yml` 被 DSH 装载；`src/index.ts` 在插件生命周期内管理 `HelperProcess`。Helper 使用 stdin/stdout JSON Lines 与插件握手，并用透明 WPF 窗口呈现 A 风格占位图。

**Tech Stack:** Node.js 24、TypeScript、Node test runner、.NET 10 WPF、System.Text.Json、Git。

---

## 文件结构

- `package.json`、`tsconfig.json`、`cordis.patch.yml`：DSH 包元数据、编译及 bundle 配置。
- `src/protocol.ts`：所有 v1 JSON Lines 消息及校验。
- `src/helper-process.ts`：受控 Helper 子进程生命周期。
- `src/index.ts`：DSH 的 `apply()` 生命周期入口。
- `test/*.test.mjs`：协议、Helper 启停及包内容的 Node 测试。
- `pet-helper/`：WPF 项目、协议读取器、窗口与占位 PNG。
- `scripts/build-helper.ps1`：发布 WPF 自包含 exe 到 npm 运行时目录。

### Task 1: 建立插件与测试骨架

**Files:**
- Create: `.gitignore`, `package.json`, `tsconfig.json`, `cordis.patch.yml`, `src/index.ts`, `test/package-layout.test.mjs`

- [ ] **Step 1: 写失败的包布局测试**

```js
import test from 'node:test'
import assert from 'node:assert/strict'
import { existsSync } from 'node:fs'
test('DSH bundle declares its patch and entrypoint', () => {
  assert.equal(existsSync(new URL('../cordis.patch.yml', import.meta.url)), true)
  assert.equal(existsSync(new URL('../lib/index.js', import.meta.url)), true)
})
```

- [ ] **Step 2: 运行测试并确认失败**

Run: `node --test test/package-layout.test.mjs`

Expected: FAIL，因为 `lib/index.js` 尚不存在。

- [ ] **Step 3: 创建最小包配置与空入口**

```json
{"type":"module","main":"./lib/index.js","scripts":{"build":"tsc","test":"node --test"},"dsh":{"bundle":{"patch":"./cordis.patch.yml"}}}
```

```yaml
- insert:
  - id: dsh-png-pet
    name: dsh-png-pet
    config:
      enabled: true
```

- [ ] **Step 4: 构建并确认测试通过**

Run: `npm run build; npm test`

Expected: PASS。

### Task 2: 实现并测试协议边界

**Files:**
- Create: `src/protocol.ts`, `test/protocol.test.mjs`

- [ ] **Step 1: 写失败的协议校验测试**

```js
import { parseHelperMessage } from '../lib/protocol.js'
test('accepts a v1 ready message', () => {
  assert.deepEqual(parseHelperMessage('{"version":1,"kind":"ready"}'), { version: 1, kind: 'ready' })
})
test('rejects unknown helper message kind', () => {
  assert.throws(() => parseHelperMessage('{"version":1,"kind":"secret"}'), /kind/)
})
```

- [ ] **Step 2: 验证失败**

Run: `npm run build; node --test test/protocol.test.mjs`

Expected: FAIL，因为模块或导出不存在。

- [ ] **Step 3: 实现最小协议**

```ts
export const PROTOCOL_VERSION = 1
export type HelperMessage = { version: 1; kind: 'ready' | 'closed' }
export function parseHelperMessage(line: string): HelperMessage { /* reject non-object, bad version and unknown kind */ }
export function encodeHostMessage(kind: 'hello' | 'config' | 'state' | 'shutdown'): string { return JSON.stringify({ version: 1, kind }) + '\n' }
```

- [ ] **Step 4: 验证通过**

Run: `npm run build; node --test test/protocol.test.mjs`

Expected: PASS。

### Task 3: 实现 HelperProcess 生命周期

**Files:**
- Create: `src/helper-process.ts`, `test/helper-process.test.mjs`, `test/fixtures/fake-helper.mjs`
- Modify: `src/index.ts`

- [ ] **Step 1: 写失败的握手与关闭测试**

```js
const helper = new HelperProcess({ command: process.execPath, args: [fixture] })
await helper.start()
assert.equal(helper.isReady, true)
await helper.stop()
assert.equal(helper.exitCode, 0)
```

- [ ] **Step 2: 验证失败**

Run: `npm run build; node --test test/helper-process.test.mjs`

Expected: FAIL，因为 `HelperProcess` 尚不存在。

- [ ] **Step 3: 实现受控子进程**

```ts
const child = spawn(command, args, { stdio: ['pipe', 'pipe', 'pipe'], windowsHide: true })
// wait for only one validated ready line; stop() writes shutdown then kills only after timeout
```

- [ ] **Step 4: 将它接入 DSH 生命周期**

```ts
export const name = 'dsh-png-pet'
export function apply(ctx: { effect: (factory: () => () => void) => void }) {
  const helper = new HelperProcess()
  void helper.start()
  ctx.effect(() => () => { void helper.stop() })
}
```

- [ ] **Step 5: 验证通过**

Run: `npm run build; npm test`

Expected: PASS，且 fake Helper 没有残留进程。

### Task 4: 实现 WPF Helper 协议和占位窗口

**Files:**
- Create: `pet-helper/PetHelper.csproj`, `pet-helper/App.xaml`, `pet-helper/App.xaml.cs`, `pet-helper/MainWindow.xaml`, `pet-helper/MainWindow.xaml.cs`, `pet-helper/ProtocolReader.cs`, `pet-helper/ProtocolMessage.cs`, `pet-helper/Assets/placeholder-a.png`, `pet-helper.Tests/PetHelper.Tests.csproj`, `pet-helper.Tests/ProtocolReaderTests.cs`

- [ ] **Step 1: 写失败的 C# 协议测试**

```csharp
[Fact]
public void Parses_shutdown_command() {
  Assert.Equal("shutdown", ProtocolReader.Parse("{\"version\":1,\"kind\":\"shutdown\"}").Kind);
}
```

- [ ] **Step 2: 验证失败**

Run: `dotnet test pet-helper.Tests/PetHelper.Tests.csproj`

Expected: FAIL，因为项目和 `ProtocolReader` 尚不存在。

- [ ] **Step 3: 实现最小 Helper**

```csharp
Console.Out.WriteLine("{\"version\":1,\"kind\":\"ready\"}");
// Background reader accepts only version 1 shutdown, then dispatches window.Close and writes closed.
```

```xml
<Window WindowStyle="None" AllowsTransparency="True" Background="Transparent" Topmost="True" ShowInTaskbar="False"><Image Source="Assets/placeholder-a.png" /></Window>
```

- [ ] **Step 4: 验证测试通过和自包含发布**

Run: `dotnet test pet-helper.Tests/PetHelper.Tests.csproj; dotnet publish pet-helper/PetHelper.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true`

Expected: 测试 PASS，发布目录含 `PetHelper.exe`。

### Task 5: 打包、端到端验证与版本管理

**Files:**
- Create: `scripts/build-helper.ps1`, `test/packaging.test.mjs`, `README.md`
- Modify: `package.json`, `.gitignore`

- [ ] **Step 1: 写失败的打包测试**

```js
test('package contains the self-contained helper and placeholder asset', () => {
  assert.equal(existsSync('runtime/bin/win32-x64/pet-helper.exe'), true)
  assert.equal(existsSync('assets/placeholder-a.png'), true)
})
```

- [ ] **Step 2: 运行并确认失败**

Run: `node --test test/packaging.test.mjs`

Expected: FAIL，因为发布产物未复制。

- [ ] **Step 3: 编写发布复制脚本与 npm 打包清单**

```powershell
dotnet publish $project -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
Copy-Item "$publishDir/PetHelper.exe" "$PSScriptRoot/../runtime/bin/win32-x64/pet-helper.exe"
```

- [ ] **Step 4: 全量验证并提交**

Run: `npm ci; npm run build; npm test; powershell -NoProfile -ExecutionPolicy Bypass -File scripts/build-helper.ps1; npm pack --dry-run; git add .; git commit -m "feat: add DSH pet bootstrap"`

Expected: 所有自动化测试 PASS；npm 清单包含 `lib/`、`runtime/`、`assets/`、`cordis.patch.yml`；Git 提交成功。
