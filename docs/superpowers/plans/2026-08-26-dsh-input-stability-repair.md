# DSH 桌宠输入稳定性修复 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 让首次运行的桌宠能安全提交输入，不会使 DSH Harness 卸载插件或让 Helper 退出。

**Architecture:** 设置 schema 在根对象缺失时解析为完整默认配置；`DialogueController` 将输入路径中的意外错误转为固定 `rejected` 状态；入口路由消费残余的异步拒绝。Helper 启动时继承完整 Windows 环境并仅在必要时补齐 `WINDIR`。

**Tech Stack:** TypeScript、Node.js 内置 test、DeepSeek Harness SettingsProvider、Windows WPF Helper。

---

## 文件结构

| 文件 | 责任 |
| --- | --- |
| `src/dialogue-settings.ts` | 将缺失根设置归一为安全默认配置。 |
| `src/dialogue-controller.ts` | 将输入处理异常限制为固定的 `rejected` 协议状态。 |
| `src/index.ts` | 消费回调中的残余异步拒绝。 |
| `src/helper-process.ts` | 为 Helper 保留已验证的 Windows 环境补齐。 |
| `test/dialogue-settings.test.mjs` | 覆盖缺失设置分节。 |
| `test/dialogue-controller.test.mjs` | 覆盖损坏设置不会逃逸或卸载宿主。 |
| `test/index.test.mjs` | 覆盖路由对拒绝 Promise 的隔离。 |
| `test/helper-process.test.mjs` | 覆盖 `WINDIR` 补齐与保留规则。 |

### Task 1: 为空设置分节建立回归测试

**Files:**
- Modify: `test/dialogue-settings.test.mjs`
- Modify: `src/dialogue-settings.ts`

- [ ] **Step 1: 写出失败测试**

在 `exposes the dialogue settings as a serializable schemastery schema` 测试中，先断言：

```js
assert.deepEqual(dialogueSettingsSchema(undefined), {
  defaultSessionId: null,
  previewEnabled: false,
  previewMaxChars: 480,
})
```

- [ ] **Step 2: 验证测试失败**

运行：`npm run build; node --test test/dialogue-settings.test.mjs`

预期：测试失败，实际值为 `undefined`。

- [ ] **Step 3: 实现最小默认值修复**

将 schema 的输入对象包裹为根对象默认值，保留原有属性约束和 transform：

```ts
z.object({
  defaultSessionId: z.union([z.string().min(1), z.const(null)]).default(null),
  previewEnabled: z.boolean().default(false),
  previewMaxChars: z.number().step(1).min(80).max(2000).default(480),
}).default({})
```

- [ ] **Step 4: 验证测试转绿**

运行：`npm run build; node --test test/dialogue-settings.test.mjs`

预期：所有该文件测试通过。

### Task 2: 隔离控制器输入处理中的意外异常

**Files:**
- Modify: `test/dialogue-controller.test.mjs`
- Modify: `src/dialogue-controller.ts`

- [ ] **Step 1: 写出失败测试**

在 `createDsh` 中允许 `settings` 为 `undefined`，并新增测试：

```js
test('contains a missing settings snapshot as a fixed rejected input status', async () => {
  const sent = []
  const dsh = createDsh({ settings: undefined })
  const controller = new DialogueController(dsh.context, (message) => sent.push(message))

  await controller.acceptInput({ requestId: 1, text: 'input omitted' })

  assert.deepEqual(sent, [{ kind: 'input-status', requestId: 1, status: 'rejected' }])
})
```

- [ ] **Step 2: 验证测试失败**

运行：`npm run build; node --test test/dialogue-controller.test.mjs`

预期：测试因 `defaultSessionId` 的未处理读取而失败。

- [ ] **Step 3: 实现最小异常边界**

将现有 `acceptInput` 主体移入私有 `acceptInputUnsafe` 方法；公开方法只负责固定的异步边界：

```ts
public async acceptInput(input: HelperInputMessage): Promise<void> {
  try {
    await this.acceptInputUnsafe(input)
  } catch {
    this.currentInputRequestId = undefined
    this.currentInputSessionId = undefined
    this.clearAll('cancelled')
    this.send({ kind: 'input-status', requestId: input.requestId, status: 'rejected' })
  }
}
```

私有方法保留原有输入处理主体。catch 块不得记录异常、输入或会话信息。

- [ ] **Step 4: 验证测试转绿**

运行：`npm run build; node --test test/dialogue-controller.test.mjs`

预期：所有该文件测试通过，且无未处理拒绝。

### Task 3: 防止入口回调传播残余 Promise 拒绝

**Files:**
- Modify: `test/index.test.mjs`
- Modify: `src/index.ts`

- [ ] **Step 1: 写出失败测试**

新增测试，让模拟控制器的 `acceptInput` 返回拒绝 Promise，并断言调用路由后可以等待其完成而不抛错：

```js
test('contains a rejected helper input promise', async () => {
  const controller = {
    acceptInput: async () => { throw new Error('expected') },
    helperClosed: () => {},
  }

  await routeHelperMessage({ version: 4, kind: 'input', requestId: 1, text: 'input omitted' }, controller)
})
```

- [ ] **Step 2: 验证测试失败**

运行：`npm run build; node --test test/index.test.mjs`

预期：测试以模拟的拒绝失败。

- [ ] **Step 3: 实现最小路由边界**

令 `routeHelperMessage` 返回 `Promise<void>`；输入分支使用：

```ts
return Promise.resolve(controller?.acceptInput(message)).catch(() => {})
```

生命周期分支保持同步调用，并返回 `Promise.resolve()`。Helper 的现有回调继续忽略该已处理的 Promise。

- [ ] **Step 4: 验证测试转绿**

运行：`npm run build; node --test test/index.test.mjs`

预期：所有该文件测试通过。

### Task 4: 恢复 Windows 子进程环境兼容性

**Files:**
- Modify: `test/helper-process.test.mjs`
- Modify: `src/helper-process.ts`

- [ ] **Step 1: 写出失败测试**

恢复两项环境测试：

```js
test('adds WINDIR from SystemRoot only when the helper environment lacks it', () => {
  assert.deepEqual(withRequiredWindowsEnvironment({ SystemRoot: 'C:\\Windows', KEEP: 'value' }), {
    SystemRoot: 'C:\\Windows', WINDIR: 'C:\\Windows', KEEP: 'value',
  })
})

test('preserves an existing WINDIR and leaves environments without SystemRoot unchanged', () => {
  assert.deepEqual(withRequiredWindowsEnvironment({ SystemRoot: 'C:\\Windows', WINDIR: 'D:\\Windows' }), { SystemRoot: 'C:\\Windows', WINDIR: 'D:\\Windows' })
  assert.deepEqual(withRequiredWindowsEnvironment({ KEEP: 'value' }), { KEEP: 'value' })
})
```

- [ ] **Step 2: 验证测试失败**

运行：`npm run build; node --test test/helper-process.test.mjs`

预期：构建失败，因为 `withRequiredWindowsEnvironment` 未导出。

- [ ] **Step 3: 恢复最小实现**

恢复函数并在 `spawn` 选项中使用它：

```ts
export function withRequiredWindowsEnvironment(environment: NodeJS.ProcessEnv): NodeJS.ProcessEnv {
  if (environment.WINDIR !== undefined || !environment.SystemRoot) return { ...environment }
  return { ...environment, WINDIR: environment.SystemRoot }
}

env: withRequiredWindowsEnvironment(process.env),
```

- [ ] **Step 4: 验证测试转绿**

运行：`npm run build; node --test test/helper-process.test.mjs`

预期：所有该文件测试通过。

### Task 5: 完整验证与发布包检查

**Files:**
- Verify only: TypeScript、打包结果与 WPF 测试。

- [ ] **Step 1: 运行完整 Node 验证**

运行：`npm test; npm run test:package`

预期：所有 Node 测试与包布局/Helper 打包测试通过。

- [ ] **Step 2: 运行 WPF 单元测试**

运行：`dotnet test pet-helper.Tests\PetHelper.Tests.csproj --no-restore`

预期：通过；若仍被本机 `Microsoft SDKs` 目录权限阻止，记录完整错误，不将其伪报为代码测试失败。

- [ ] **Step 3: 审核变更范围**

运行：`git diff --check; git status --short`

预期：无空白错误；仅报告本修复涉及的文件及已有用户改动。

- [ ] **Step 4: 创建限定提交**

运行：`git add -- src/dialogue-settings.ts src/dialogue-controller.ts src/index.ts src/helper-process.ts test/dialogue-settings.test.mjs test/dialogue-controller.test.mjs test/index.test.mjs test/helper-process.test.mjs docs/superpowers/specs/2026-08-26-dsh-input-stability-design.md docs/superpowers/plans/2026-08-26-dsh-input-stability-repair.md; git commit -m "fix: stabilize pet dialogue input"`

预期：仅提交修复相关文件；如 `.git/index.lock` 仍被权限拒绝，保留工作区内容并报告该环境阻碍。
