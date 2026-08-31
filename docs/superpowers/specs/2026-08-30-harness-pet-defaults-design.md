# Harness「桌宠」默认设置设计

## 目标

在 Harness 的 `设置 / 桌宠` 中补齐桌宠的**已保存默认值**，视觉和交互完全复用 Harness 设置页：由 Harness 渲染左侧导航、排版、浅深色主题、输入控件和焦点态；插件不引入独立 CSS、卡片阴影、浮层或自定义图标系统。

本次可用设置：默认会话、回复显示、人物默认大小、减少动态效果、人物默认位置、双击打开的会话栏默认位置及默认大小。随机待机动画、随机文本仅展示为禁用的“即将推出”预留项，不保存开关，也不会让用户误以为已生效。

## 信息架构与页面文案

```text
设置 / 桌宠
管理桌宠的默认会话、外观和会话栏布局。

会话
  默认会话        [未选择 / 会话标题                         ▾]
  实时流式回复    [开关]
  流式长度        [ 2000 ] 个字符

外观
  人物大小        ( ) 75%  (●) 100%  ( ) 125%  ( ) 150%
  减少动态效果    [开关]
  默认位置        [屏幕中央                                  ▾]

会话栏
  默认打开位置    [跟随人物                                  ▾]
  默认宽度        [ 320 ] px       （最小 220 px）
  默认高度        [ 420 ] px       （最小 240 px）

待机互动
  随机播放待机动画  [开关：即将推出，禁用]
  随机显示文本      [开关：即将推出，禁用]
```

分组使用原生的 `section`、`h2`、`label`、`fieldset`、`select` 与 `input`，让 Harness 自己的全局样式接管控件。每项有一行简短说明；窄屏时单列排列，单选组可以自然换行。没有保存按钮：改变后立即写入 `settingsScope`，失败后保留 Harness 已确认的快照，并在对应分组显示固定错误“无法保存桌宠设置。”

## 默认值与用户当前布局的边界

为适配多显示器和 DPI，位置不保存绝对像素，而是语义化锚点：

| 设置字段 | 值 | 默认 |
| --- | --- | --- |
| `petPlacement` | `center`、`top-left`、`top-right`、`bottom-left`、`bottom-right` | `center` |
| `dialoguePlacement` | `near-pet`、`center`、`top-left`、`top-right`、`bottom-left`、`bottom-right` | `near-pet` |
| `dialogueWidth` | `220–4000` 的整数 DIP | `320` |
| `dialogueHeight` | `240–3000` 的整数 DIP | `420` |

Harness 设置只定义首次启动和“重置为默认布局”时使用的值。用户拖动人物、拖动/缩放会话栏后，Helper 继续保存当前布局到本地状态文件；这不会反写 Harness 的默认值，也不会被下一次设置同步强行覆盖。这样用户既能有可复现的默认布局，也不会丢失当前工作布局。

人物大小和减少动态效果不同：它们是全局偏好，设置提交后应立即通过 `config` JSON Lines 下发给已运行的 Helper；下次启动也从 Harness 的已提交设置恢复。

## 设置、协议与 Helper 设计

`DialogueSettings` 扩展为：

```ts
export type PetPlacement = 'center' | 'top-left' | 'top-right' | 'bottom-left' | 'bottom-right'
export type DialoguePlacement = 'near-pet' | PetPlacement

export type DialogueSettings = {
  defaultSessionId: string | null
  defaultWorkspaceId: string | null
  previewEnabled: boolean
  previewMaxChars: number
  scale: 0.75 | 1 | 1.25 | 1.5
  reducedMotion: boolean
  petPlacement: PetPlacement
  dialoguePlacement: DialoguePlacement
  dialogueWidth: number
  dialogueHeight: number
}
```

协议升级为 v7，并把全局界面默认值放入唯一的 Host → Helper `config` 消息；不传递会话正文、路径、提示词、API Key 或随机文本内容：

```ts
type Config = {
  kind: 'config'
  scale: 0.75 | 1 | 1.25 | 1.5
  reducedMotion: boolean
  petPlacement: PetPlacement
  dialoguePlacement: DialoguePlacement
  dialogueWidth: number
  dialogueHeight: number
}
```

Helper 收到配置时保存为 `defaultLayout`。`MainWindow` 在没有本地位置时按 `petPlacement` 放到主显示器工作区；`DialogueWindow.ShowDialogue()` 在没有本地状态时按 `dialoguePlacement` 放置，并把宽高限制到当前工作区及既有 `220 × 240` 最小值。现有本地状态仍优先，且恢复时继续进行多屏可见性校正。

## React 控件结构

```tsx
<section aria-label="桌宠设置">
  <h2>会话</h2>
  <label>默认会话<select /* Harness settingsScope */ /></label>
  <label><input type="checkbox" />实时流式回复</label>

  <h2>外观</h2>
  <fieldset aria-label="人物大小">{scaleOptions.map(renderRadio)}</fieldset>
  <label><input type="checkbox" />减少动态效果</label>
  <label>默认位置<select>{petPlacementOptions}</select></label>

  <h2>会话栏</h2>
  <label>默认打开位置<select>{dialoguePlacementOptions}</select></label>
  <label>默认宽度<input type="number" min="220" max="4000" /></label>
  <label>默认高度<input type="number" min="240" max="3000" /></label>

  <h2>待机互动</h2>
  <label><input type="checkbox" disabled />随机播放待机动画（即将推出）</label>
  <label><input type="checkbox" disabled />随机显示文本（即将推出）</label>
</section>
```

实现必须让输入组件以设置快照为准：初始加载禁用，保存失败回退为最后一个已确认值。会话选择继续只投影 DSH 的 `id` 和 `displayTitle`，不读取或渲染工作目录、正文或任何凭据。

## 测试与验收

- TypeScript schema 拒绝未知布局枚举、非整数尺寸与小于 `220 × 240` 的会话栏；Host 在 Helper ready 和每一次已提交设置变更后发送完整 `config`。
- v7 TypeScript 与 C# 协议解析器同时拒绝缺字段、额外字段和越界配置。
- C# 纯逻辑测试覆盖每个锚点、工作区夹紧、最小尺寸和已有本地布局优先。
- 客户端测试确认使用 `settingsScope`、会话隐私投影、禁用的“即将推出”控件，以及不使用自定义 CSS 或独立 UI 库。
- 人工验收：在浅/深色 Harness 主题和窄窗口中，页面控件的字体、颜色、圆角、悬停和键盘焦点与其余 Harness 设置项一致；拖动后的当前人物/会话栏布局不因修改默认值而跳动。
