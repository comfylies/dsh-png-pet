import {
  createElement,
  useState,
  useSyncExternalStore,
  type ChangeEvent,
  type ReactElement,
  type ReactNode,
} from 'react'

import { projectSessionOptions, projectWorkspaceSessionTree, type DialogueSettings, type WorkspaceSessionTree } from './client-settings-model.js'

const SETTINGS_NAMESPACE = 'dsh-png-pet'
const DEFAULT_SETTINGS: DialogueSettings = {
  defaultSessionId: null, defaultWorkspaceId: null, previewEnabled: true, previewMaxChars: 2000,
  approvalSurface: 'web',
  randomChatEnabled: false, randomChatBrowseOnOpen: false, randomChatWorkspaceIds: [], randomChatMinIntervalMinutes: 8, randomChatMaxIntervalMinutes: 24, randomChatCustomPrompts: [], randomChatTestNonce: 0,
  scale: 1, reducedMotion: false, petPlacement: 'center', dialoguePlacement: 'near-pet', dialogueWidth: 320, dialogueHeight: 420,
}
const SESSION_LIST_UNAVAILABLE = '会话列表尚未就绪。'
const SELECTED_SESSION_UNAVAILABLE = '所选会话已不可用。'
const SETTINGS_WRITE_FAILED = '无法保存桌宠设置。'

const cardStyle = { border: '1px solid rgba(15, 23, 42, 0.14)', borderRadius: 16, overflow: 'hidden', background: 'transparent' }
const selectedCardStyle = { ...cardStyle, border: '1px solid currentColor' }
const cardHeaderStyle = { display: 'flex', alignItems: 'center', justifyContent: 'space-between', gap: 12, width: '100%', padding: '16px 18px', border: 0, background: 'transparent', color: 'inherit', font: 'inherit', fontWeight: 650, textAlign: 'left' as const, cursor: 'pointer' }
const cardBodyStyle = { borderTop: '1px solid rgba(15, 23, 42, 0.12)', padding: 12 }
const optionButtonStyle = { display: 'flex', alignItems: 'center', justifyContent: 'space-between', gap: 12, width: '100%', padding: '11px 12px', border: 0, borderRadius: 10, background: 'transparent', color: 'inherit', font: 'inherit', textAlign: 'left' as const, cursor: 'pointer' }
const activeOptionStyle = { ...optionButtonStyle, background: 'rgba(15, 23, 42, 0.07)', fontWeight: 650 }

type SnapshotSource<T> = { getSnapshot(): T, subscribe(listener: () => void): () => void }
type SessionRow = { id: string, displayTitle: string }
type SessionListSnapshot = { ids: readonly string[] | undefined, byId: Readonly<Record<string, SessionRow | undefined>> | undefined }
type WorkspaceRow = { workspaceId: string, title: string, sessionIds: readonly string[] }
type WorkspaceListSnapshot = { items: readonly WorkspaceRow[] | undefined, archivedSessionIds: readonly string[] | undefined, baselinesReady: boolean | undefined }
type SettingsSnapshot<T> = { status: 'loading' | 'ready' | 'unavailable', value: T | undefined }
type SettingsScope<T> = SnapshotSource<SettingsSnapshot<T>> & { set(field: string, value: unknown): Promise<void> }
type SettingsScopeService = { bind<T>(spec: { namespace: string, decode?(section: unknown): T | undefined }): SettingsScope<T> }
type ClientContext = {
  sessions: { list: SnapshotSource<SessionListSnapshot> }
  workspaces: { list: SnapshotSource<WorkspaceListSnapshot> }
  get(service: 'settingsScope'): SettingsScopeService
  slots: {
    inject(name: 'settings.section', factory: () => unknown): void
    register(options: { name: 'settings.section', id: string, order: number, label: string }, component: () => ReactElement): unknown
  }
}

// connection and remote are the built-in settingsScope transport dependencies, not plugin Remote APIs.
export const inject = ['sessions', 'workspaces', 'settingsScope', 'connection', 'remote', 'slots']

export function apply(ctx: ClientContext): void {
  const settings = bindDialogueSettings(ctx)
  ctx.slots.inject('settings.section', () => ctx.slots.register({
    name: 'settings.section', id: SETTINGS_NAMESPACE, order: 100, label: '桌宠',
  }, () => createElement(DesktopPetSettingsSection, { sessions: ctx.sessions.list, workspaces: ctx.workspaces.list, settings })))
}

export function bindDialogueSettings(ctx: Pick<ClientContext, 'get'>): SettingsScope<DialogueSettings> {
  // The Host schema has a transform callback, which cannot run in the browser
  // after serialisation.  Supplying this narrow decoder lets the settings scope
  // accept the Host's already-resolved JSON view after every successful write.
  return ctx.get('settingsScope').bind<DialogueSettings>({ namespace: SETTINGS_NAMESPACE, decode: decodeDialogueSettings })
}

export function decodeDialogueSettings(section: unknown): DialogueSettings | undefined {
  if (section === null || typeof section !== 'object' || Array.isArray(section)) return undefined
  const value = section as Record<string, unknown>
  const defaultSessionId = nullableId(value.defaultSessionId)
  const defaultWorkspaceId = nullableId(value.defaultWorkspaceId)
  const previewEnabled = value.previewEnabled
  const previewMaxChars = value.previewMaxChars
  const approvalSurface = value.approvalSurface
  const randomChatEnabled = value.randomChatEnabled
  const randomChatBrowseOnOpen = value.randomChatBrowseOnOpen
  const randomChatWorkspaceIds = value.randomChatWorkspaceIds
  const randomChatMinIntervalMinutes = value.randomChatMinIntervalMinutes
  const randomChatMaxIntervalMinutes = value.randomChatMaxIntervalMinutes
  const randomChatCustomPrompts = value.randomChatCustomPrompts
  const randomChatTestNonce = value.randomChatTestNonce
  const scale = value.scale
  const reducedMotion = value.reducedMotion
  const petPlacement = value.petPlacement
  const dialoguePlacement = value.dialoguePlacement
  const dialogueWidth = value.dialogueWidth
  const dialogueHeight = value.dialogueHeight
  if (defaultSessionId === undefined || defaultWorkspaceId === undefined
    || typeof previewEnabled !== 'boolean' || !isIntegerIn(previewMaxChars, 80, 8000)
    || !isApprovalSurface(approvalSurface)
    || typeof randomChatEnabled !== 'boolean' || typeof randomChatBrowseOnOpen !== 'boolean'
    || !isWorkspaceIds(randomChatWorkspaceIds) || !isRandomChatIntervalMinutes(randomChatMinIntervalMinutes)
    || !isRandomChatIntervalMinutes(randomChatMaxIntervalMinutes) || randomChatMinIntervalMinutes > randomChatMaxIntervalMinutes
    || !isRandomChatCustomPrompts(randomChatCustomPrompts) || !isRandomChatTestNonce(randomChatTestNonce)
    || (scale !== 0.75 && scale !== 1 && scale !== 1.25 && scale !== 1.5)
    || typeof reducedMotion !== 'boolean' || !isPetPlacement(petPlacement) || !isDialoguePlacement(dialoguePlacement)
    || !isIntegerIn(dialogueWidth, 220, 4000) || !isIntegerIn(dialogueHeight, 240, 3000)) return undefined
  return { defaultSessionId, defaultWorkspaceId, previewEnabled, previewMaxChars, approvalSurface, randomChatEnabled, randomChatBrowseOnOpen, randomChatWorkspaceIds: [...randomChatWorkspaceIds], randomChatMinIntervalMinutes, randomChatMaxIntervalMinutes, randomChatCustomPrompts: [...randomChatCustomPrompts], randomChatTestNonce, scale, reducedMotion, petPlacement, dialoguePlacement, dialogueWidth, dialogueHeight }
}

export async function writeDialogueSetting(settings: Pick<SettingsScope<DialogueSettings>, 'set'>, field: keyof DialogueSettings, value: DialogueSettings[keyof DialogueSettings]): Promise<boolean> {
  if (field === 'previewMaxChars' && (typeof value !== 'number' || !Number.isInteger(value) || value < 80 || value > 8000)) return false
  if (field === 'approvalSurface' && !isApprovalSurface(value)) return false
  if (field === 'dialogueWidth' && (typeof value !== 'number' || !Number.isInteger(value) || value < 220 || value > 4000)) return false
  if (field === 'dialogueHeight' && (typeof value !== 'number' || !Number.isInteger(value) || value < 240 || value > 3000)) return false
  if ((field === 'randomChatMinIntervalMinutes' || field === 'randomChatMaxIntervalMinutes') && !isRandomChatIntervalMinutes(value)) return false
  if (field === 'randomChatCustomPrompts' && !isRandomChatCustomPrompts(value)) return false
  if (field === 'randomChatTestNonce' && !isRandomChatTestNonce(value)) return false
  if (field === 'scale' && value !== 0.75 && value !== 1 && value !== 1.25 && value !== 1.5) return false
  if (field === 'petPlacement' && !isPetPlacement(value)) return false
  if (field === 'dialoguePlacement' && !isDialoguePlacement(value)) return false
  if (field === 'randomChatWorkspaceIds' && !isWorkspaceIds(value)) return false
  try { await settings.set(field, value); return true } catch { return false }
}

export function projectDialogueSettingsView(sessionSnapshot: SessionListSnapshot, workspaceSnapshot: WorkspaceListSnapshot, settingsSnapshot: SettingsSnapshot<DialogueSettings>, writeFailed: boolean): {
  tree: WorkspaceSessionTree, value: DialogueSettings, listUnavailable: boolean, selectedSessionUnavailable: boolean, error: string | undefined
} {
  const sessionRows = (sessionSnapshot.ids ?? []).flatMap((id) => {
    const row = sessionSnapshot.byId?.[id]
    return row === undefined ? [] : [{ id: row.id, displayTitle: row.displayTitle }]
  })
  const options = projectSessionOptions(sessionRows)
  // Session metadata is sufficient to save a default.  Workspace readiness
  // only controls grouping, so an incomplete workspace baseline must never
  // disable the entire picker.
  const listUnavailable = sessionSnapshot.ids === undefined || sessionSnapshot.byId === undefined
  const tree = projectWorkspaceSessionTree(sessionRows, workspaceSnapshot.items ?? [], workspaceSnapshot.archivedSessionIds ?? [])
  const value = settingsSnapshot.value ?? DEFAULT_SETTINGS
  const selectedSessionUnavailable = value.defaultSessionId !== null && !options.some((option) => option.id === value.defaultSessionId)
  const error = listUnavailable ? SESSION_LIST_UNAVAILABLE : selectedSessionUnavailable ? SELECTED_SESSION_UNAVAILABLE : writeFailed || settingsSnapshot.status === 'unavailable' ? SETTINGS_WRITE_FAILED : undefined
  return { tree, value, listUnavailable, selectedSessionUnavailable, error }
}

function DesktopPetSettingsSection({ sessions, workspaces, settings }: { sessions: ClientContext['sessions']['list'], workspaces: ClientContext['workspaces']['list'], settings: SettingsScope<DialogueSettings> }): ReactElement {
  const sessionSnapshot = useSyncExternalStore((listener) => sessions.subscribe(listener), () => sessions.getSnapshot())
  const workspaceSnapshot = useSyncExternalStore((listener) => workspaces.subscribe(listener), () => workspaces.getSnapshot())
  const settingsSnapshot = useSyncExternalStore((listener) => settings.subscribe(listener), () => settings.getSnapshot())
  const [writeFailed, setWriteFailed] = useState(false)
  const [pendingDefaultSessionId, setPendingDefaultSessionId] = useState<string | null | undefined>(undefined)
  const { tree, value, listUnavailable, selectedSessionUnavailable, error } = projectDialogueSettingsView(sessionSnapshot, workspaceSnapshot, settingsSnapshot, writeFailed)
  const settingsUnavailable = settingsSnapshot.status === 'unavailable'
  async function update(field: keyof DialogueSettings, nextValue: DialogueSettings[keyof DialogueSettings]): Promise<void> { setWriteFailed(!await writeDialogueSetting(settings, field, nextValue)) }
  function onIntegerChange(field: 'previewMaxChars' | 'dialogueWidth' | 'dialogueHeight' | 'randomChatMinIntervalMinutes' | 'randomChatMaxIntervalMinutes', min: number, max: number, event: ChangeEvent<HTMLInputElement>): void {
    const nextValue = Number(event.currentTarget.value)
    if (!Number.isInteger(nextValue) || nextValue < min || nextValue > max) return setWriteFailed(true)
    void update(field, nextValue)
  }
  async function selectDefaultSession(session: { id: string, workspaceId: string | null } | null): Promise<void> {
    if (pendingDefaultSessionId !== undefined) return
    const sessionId = session?.id ?? null
    // A settings scope serializes writes.  Keep this to one scalar write so
    // repeated clicks cannot build a long queue or race a workspace hint.
    setPendingDefaultSessionId(sessionId)
    setWriteFailed(!await writeDialogueSetting(settings, 'defaultSessionId', sessionId))
    setPendingDefaultSessionId(undefined)
  }

  return createElement('section', { 'aria-label': '桌宠设置', style: { maxWidth: 920 } },
    createElement('h2', null, '会话'),
    createElement('p', null, '默认会话会优先于右键菜单中的临时选择；右键选择仅在本次桌宠运行期间有效。'),
    createElement(DefaultSessionPicker, { tree, selectedSessionId: pendingDefaultSessionId === undefined ? (selectedSessionUnavailable ? null : value.defaultSessionId) : pendingDefaultSessionId, disabled: listUnavailable || pendingDefaultSessionId !== undefined, onSelect: (session) => { void selectDefaultSession(session) } }),
    createElement('div', { style: gridStyle },
      createElement(SettingCard, { title: '实时回复', description: '将生成中的文字同步显示在桌宠会话栏。' },
        createElement('label', { style: inlineLabelStyle }, createElement('input', { type: 'checkbox', checked: value.previewEnabled, disabled: settingsUnavailable, onChange: (event: ChangeEvent<HTMLInputElement>) => { void update('previewEnabled', event.currentTarget.checked) } }), '实时流式回复'),
        createElement('label', { style: { display: 'block', marginTop: 12 } }, '流式长度 ', createElement('input', { type: 'number', min: 80, max: 8000, step: 1, value: value.previewMaxChars, disabled: !value.previewEnabled || settingsUnavailable, onChange: (event: ChangeEvent<HTMLInputElement>) => onIntegerChange('previewMaxChars', 80, 8000, event) }))),
      createElement(SettingCard, { title: '人物大小', description: '桌宠启动与重置时采用的默认大小。' }, createElement(ChoiceCards, { value: value.scale, disabled: settingsUnavailable, name: 'pet-scale', options: ([0.75, 1, 1.25, 1.5] as const).map((scale) => ({ value: scale, label: `${scale * 100}%` })), onChange: (scale) => { void update('scale', scale) } }))),
    createElement('h2', { style: sectionHeadingStyle }, '权限请求'),
    createElement('p', null, '选择未来的权限请求由哪里回答。选择 Web 时，桌宠的“等待你的操作”气泡可点击打开 DSH。'),
    createElement(SettingCard, { title: '请求批准的位置', description: '默认使用 DSH Web；桌宠模式只接管桌宠当前选中的会话。切换不会改变已经等待回答的请求。' },
      createElement(ChoiceCards, { value: value.approvalSurface, disabled: settingsUnavailable, name: 'approval-surface', options: [{ value: 'web', label: 'Web（默认）' }, { value: 'pet', label: '桌宠' }], onChange: (surface) => { void update('approvalSurface', surface) } })),
    createElement('h2', { style: sectionHeadingStyle }, '外观'),
    createElement('p', null, '这些默认值会在桌宠启动时恢复；当前拖拽位置不会被覆盖。'),
    createElement('div', { style: gridStyle },
      createElement(SettingCard, { title: '默认位置', description: '右键“重置位置”将回到这里。' }, createElement(ChoiceCards, { value: value.petPlacement, disabled: settingsUnavailable, name: 'pet-placement', options: placementOptions, onChange: (placement) => { void update('petPlacement', placement) } })),
      createElement(SettingCard, { title: '动态效果', description: '减少动画与视觉变化。' }, createElement('label', { style: inlineLabelStyle }, createElement('input', { type: 'checkbox', checked: value.reducedMotion, disabled: settingsUnavailable, onChange: (event: ChangeEvent<HTMLInputElement>) => { void update('reducedMotion', event.currentTarget.checked) } }), '减少动态效果'))),
    createElement('h2', { style: sectionHeadingStyle }, '会话栏'),
    createElement('p', null, '双击桌宠打开会话栏时使用。最小尺寸可避免窗口内容被遮挡。'),
    createElement('div', { style: gridStyle },
      createElement(SettingCard, { title: '默认打开位置', description: '可跟随桌宠，也可固定在屏幕边角。' }, createElement(ChoiceCards, { value: value.dialoguePlacement, disabled: settingsUnavailable, name: 'dialogue-placement', options: dialoguePlacementOptions, onChange: (placement) => { void update('dialoguePlacement', placement) } })),
      createElement(SettingCard, { title: '默认大小', description: '最小 220 × 240 px。' },
        createElement('label', { style: { display: 'block' } }, '宽度 ', createElement('input', { type: 'number', min: 220, max: 4000, step: 1, value: value.dialogueWidth, disabled: settingsUnavailable, onChange: (event: ChangeEvent<HTMLInputElement>) => onIntegerChange('dialogueWidth', 220, 4000, event) }), ' px'),
        createElement('label', { style: { display: 'block', marginTop: 12 } }, '高度 ', createElement('input', { type: 'number', min: 240, max: 3000, step: 1, value: value.dialogueHeight, disabled: settingsUnavailable, onChange: (event: ChangeEvent<HTMLInputElement>) => onIntegerChange('dialogueHeight', 240, 3000, event) }), ' px'))),
    createElement('h2', { style: sectionHeadingStyle }, '待机互动'),
    createElement('p', null, '随机聊聊默认关闭。桌宠只会在你启用功能、同意点击后联网并选择目标工作区后显示本地邀约气泡。'),
    createElement('div', { style: gridStyle },
      createElement(SettingCard, { title: '随机待机动画', description: '即将推出。' }, createElement('input', { type: 'checkbox', disabled: true })),
      createElement(SettingCard, { title: '随机聊聊', description: '桌宠会在随机时间用本地短句邀你继续聊；点击后才打开会话栏。' },
        createElement('label', { style: inlineLabelStyle }, createElement('input', { type: 'checkbox', checked: value.randomChatEnabled, disabled: settingsUnavailable, onChange: (event: ChangeEvent<HTMLInputElement>) => { void update('randomChatEnabled', event.currentTarget.checked) } }), '启用随机聊聊'),
        createElement('label', { style: { display: 'block', marginTop: 12 } }, '随机间隔 ', createElement('input', { type: 'number', min: 5, max: value.randomChatMaxIntervalMinutes, step: 1, value: value.randomChatMinIntervalMinutes, disabled: !value.randomChatEnabled || settingsUnavailable, onChange: (event: ChangeEvent<HTMLInputElement>) => onIntegerChange('randomChatMinIntervalMinutes', 5, value.randomChatMaxIntervalMinutes, event) }), ' 至 ', createElement('input', { type: 'number', min: value.randomChatMinIntervalMinutes, max: 1440, step: 1, value: value.randomChatMaxIntervalMinutes, disabled: !value.randomChatEnabled || settingsUnavailable, onChange: (event: ChangeEvent<HTMLInputElement>) => onIntegerChange('randomChatMaxIntervalMinutes', value.randomChatMinIntervalMinutes, 1440, event) }), ' 分钟'),
        createElement('p', { style: { margin: '8px 0 0', opacity: 0.72, lineHeight: 1.5 } }, '最短触发时间为 5 分钟；桌宠会从该范围内随机选择下一次邀约时间。'),
        createElement('label', { style: { display: 'block', marginTop: 12 } }, '自定义气泡文案（每行一条）', createElement('textarea', { rows: 4, defaultValue: value.randomChatCustomPrompts.join('\n'), placeholder: '例如：要不要休息一分钟，和我聊聊？', disabled: !value.randomChatEnabled || settingsUnavailable, onBlur: (event: ChangeEvent<HTMLTextAreaElement>) => { void update('randomChatCustomPrompts', parseRandomChatCustomPrompts(event.currentTarget.value)) } })),
        createElement('p', { style: { margin: '8px 0 0', opacity: 0.72, lineHeight: 1.5 } }, '自定义文案只在本地气泡中显示，不会发送给模型或联网服务。'),
        createElement('button', { type: 'button', style: { marginTop: 12 }, disabled: settingsUnavailable, onClick: () => { void update('randomChatTestNonce', value.randomChatTestNonce === 2_147_483_647 ? 1 : value.randomChatTestNonce + 1) } }, '立即显示测试气泡'),
        createElement('p', { style: { margin: '8px 0 0', opacity: 0.72, lineHeight: 1.5 } }, '仅显示当前随机气泡，不联网、不创建会话，也不会重置正常的随机计时。'),
        createElement('label', { style: { ...inlineLabelStyle, marginTop: 12 } }, createElement('input', { type: 'checkbox', checked: value.randomChatBrowseOnOpen, disabled: !value.randomChatEnabled || settingsUnavailable, onChange: (event: ChangeEvent<HTMLInputElement>) => { void update('randomChatBrowseOnOpen', event.currentTarget.checked) } }), '点击话题后允许联网检索'),
        createElement('p', { style: { margin: '10px 0 0', opacity: 0.72, lineHeight: 1.5 } }, '联网只在你点击带有“查阅”的气泡后发生；气泡本身不会在后台检索或显示未经验证的事实。'),
        createElement(RandomWorkspacePicker, { tree, selectedWorkspaceIds: value.randomChatWorkspaceIds, disabled: !value.randomChatEnabled || settingsUnavailable, onChange: (workspaceIds) => { void update('randomChatWorkspaceIds', workspaceIds) } }))),
    error === undefined ? null : createElement('p', { role: 'alert' }, error))
}

function SettingCard({ title, description, children }: { title: string, description: string, children?: ReactNode }): ReactElement {
  return createElement('section', { style: cardStyle },
    createElement('div', { style: { padding: '16px 18px 12px' } }, createElement('strong', null, title), createElement('p', { style: { margin: '8px 0 0', opacity: 0.72, lineHeight: 1.5 } }, description)),
    createElement('div', { style: cardBodyStyle }, children))
}

function DefaultSessionPicker({ tree, selectedSessionId, disabled, onSelect }: { tree: WorkspaceSessionTree, selectedSessionId: string | null, disabled: boolean, onSelect(session: { id: string, workspaceId: string | null } | null): void }): ReactElement {
  const [isOpen, setIsOpen] = useState(false)
  const [expanded, setExpanded] = useState<ReadonlySet<string>>(() => new Set())
  function toggle(key: string): void { setExpanded((current) => { const next = new Set(current); next.has(key) ? next.delete(key) : next.add(key); return next }) }
  const entries = [...tree.workspaces.map((workspace) => ({ key: `workspace:${workspace.id}`, id: workspace.id as string | null, title: workspace.title, sessions: workspace.sessions })), ...(tree.ungrouped.length === 0 ? [] : [{ key: 'ungrouped', id: null, title: '未分组会话', sessions: tree.ungrouped }])]
  const selectedTitle = selectedSessionId === null
    ? '未选择默认会话'
    : entries.flatMap((entry) => entry.sessions).find((session) => session.id === selectedSessionId)?.title ?? '已选择的会话'
  return createElement('section', { style: cardStyle },
    createElement('button', { type: 'button', disabled, style: cardHeaderStyle, 'aria-expanded': isOpen, onClick: () => setIsOpen((open) => !open) },
      createElement('span', null, `${isOpen ? '⌄' : '›'}  默认会话`),
      createElement('span', { style: { opacity: 0.68, fontWeight: 400 } }, selectedTitle)),
    !isOpen ? null : createElement('div', { role: 'tree', 'aria-label': '默认会话', style: { ...cardBodyStyle, display: 'grid', gap: 12 } },
      createElement('div', { style: selectedSessionId === null ? selectedCardStyle : cardStyle }, createElement('button', { type: 'button', disabled, style: selectedSessionId === null ? activeOptionStyle : optionButtonStyle, 'aria-pressed': selectedSessionId === null, onClick: () => onSelect(null) }, createElement('span', null, '未选择默认会话'), selectedSessionId === null ? createElement('span', { style: pillStyle }, '当前默认') : null)),
      ...entries.map((entry) => {
      const isExpanded = expanded.has(entry.key)
      const hasSelected = entry.sessions.some((session) => session.id === selectedSessionId)
      return createElement('section', { key: entry.key, role: 'treeitem', 'aria-expanded': isExpanded, style: hasSelected ? selectedCardStyle : cardStyle },
        createElement('button', { type: 'button', disabled, style: cardHeaderStyle, onClick: () => toggle(entry.key) }, createElement('span', null, `${isExpanded ? '⌄' : '›'}  ${entry.title}`), createElement('span', { style: { opacity: 0.62, fontWeight: 400 } }, `${entry.sessions.length} 个会话`)),
        !isExpanded ? null : createElement('div', { role: 'group', style: cardBodyStyle }, entry.sessions.length === 0 ? createElement('p', { style: { margin: 0, opacity: 0.7 } }, '暂无可选会话') : entry.sessions.map((session) => createElement('button', { key: session.id, type: 'button', disabled, style: session.id === selectedSessionId ? activeOptionStyle : optionButtonStyle, 'aria-pressed': session.id === selectedSessionId, onClick: () => onSelect({ id: session.id, workspaceId: entry.id }) }, createElement('span', null, session.title), session.id === selectedSessionId ? createElement('span', { style: pillStyle }, '当前默认') : null))))
      }))
  )
}

function RandomWorkspacePicker({ tree, selectedWorkspaceIds, disabled, onChange }: { tree: WorkspaceSessionTree, selectedWorkspaceIds: readonly string[], disabled: boolean, onChange(workspaceIds: string[]): void }): ReactElement {
  const [isOpen, setIsOpen] = useState(false)
  const selected = new Set(selectedWorkspaceIds)
  function toggle(workspaceId: string): void {
    const next = new Set(selected)
    next.has(workspaceId) ? next.delete(workspaceId) : next.add(workspaceId)
    onChange([...next])
  }
  return createElement('section', { style: { ...cardStyle, marginTop: 12 } },
    createElement('button', { type: 'button', disabled, style: cardHeaderStyle, 'aria-expanded': isOpen, onClick: () => setIsOpen((open) => !open) },
      createElement('span', null, `${isOpen ? '⌄' : '›'}  随机聊聊工作区`),
      createElement('span', { style: { opacity: 0.68, fontWeight: 400 } }, selected.size === 0 ? '未添加' : `已添加 ${selected.size} 个`)),
    !isOpen ? null : createElement('div', { role: 'group', 'aria-label': '随机聊聊工作区', style: { ...cardBodyStyle, display: 'grid', gap: 8 } },
      tree.workspaces.length === 0
        ? createElement('p', { style: { margin: 0, opacity: 0.7 } }, '暂无可添加的工作区')
        : tree.workspaces.map((workspace) => createElement('label', { key: workspace.id, style: selected.has(workspace.id) ? activeOptionStyle : optionButtonStyle },
          createElement('span', null, createElement('input', { type: 'checkbox', checked: selected.has(workspace.id), disabled, onChange: () => toggle(workspace.id) }), ' ', workspace.title),
          createElement('span', { style: { opacity: 0.62, fontWeight: 400 } }, `${workspace.sessions.length} 个会话`)))),
  )
}

function ChoiceCards<T extends string | number>({ value, disabled, name, options, onChange }: { value: T, disabled: boolean, name: string, options: readonly { value: T, label: string }[], onChange(value: T): void }): ReactElement {
  return createElement('div', { style: { display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(110px, 1fr))', gap: 8 } }, ...options.map((option) => createElement('label', { key: String(option.value), style: option.value === value ? selectedCardStyle : cardStyle }, createElement('span', { style: { display: 'flex', alignItems: 'center', gap: 8, padding: '10px 12px' } }, createElement('input', { type: 'radio', name, checked: option.value === value, disabled, onChange: () => onChange(option.value) }), option.label))))
}

const gridStyle = { display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(250px, 1fr))', gap: 16, marginTop: 16 }
const sectionHeadingStyle = { marginTop: 28 }
const inlineLabelStyle = { display: 'flex', alignItems: 'center', gap: 8 }
const pillStyle = { borderRadius: 999, padding: '3px 8px', background: '#111827', color: '#fff', fontSize: 12, whiteSpace: 'nowrap' as const }
const placementOptions: ReadonlyArray<{ value: DialogueSettings['petPlacement'], label: string }> = [
  { value: 'center', label: '屏幕中央' }, { value: 'top-left', label: '左上角' }, { value: 'top-right', label: '右上角' }, { value: 'bottom-left', label: '左下角' }, { value: 'bottom-right', label: '右下角' },]
const dialoguePlacementOptions: ReadonlyArray<{ value: DialogueSettings['dialoguePlacement'], label: string }> = [{ value: 'near-pet', label: '跟随人物' }, ...placementOptions]
function isPetPlacement(value: unknown): value is DialogueSettings['petPlacement'] { return placementOptions.some((option) => option.value === value) }
function isDialoguePlacement(value: unknown): value is DialogueSettings['dialoguePlacement'] { return dialoguePlacementOptions.some((option) => option.value === value) }
function isApprovalSurface(value: unknown): value is DialogueSettings['approvalSurface'] { return value === 'web' || value === 'pet' }
function isIntegerIn(value: unknown, min: number, max: number): value is number { return typeof value === 'number' && Number.isInteger(value) && value >= min && value <= max }

function isRandomChatIntervalMinutes(value: unknown): value is number { return isIntegerIn(value, 5, 1440) }

function isRandomChatCustomPrompts(value: unknown): value is string[] {
  return Array.isArray(value) && value.length <= 12 && value.every((prompt) => typeof prompt === 'string' && prompt.length > 0 && prompt.length <= 120 && !prompt.includes('\n') && !prompt.includes('\r')) && new Set(value).size === value.length
}

function parseRandomChatCustomPrompts(value: string): string[] {
  return value.split(/\r?\n/).map((prompt) => prompt.trim()).filter((prompt) => prompt.length > 0)
}

function isRandomChatTestNonce(value: unknown): value is number { return isIntegerIn(value, 0, 2_147_483_647) }
function nullableId(value: unknown): string | null | undefined { return value === null || (typeof value === 'string' && value.length > 0) ? value : undefined }
function isWorkspaceIds(value: unknown): value is string[] { return Array.isArray(value) && value.length <= 8 && value.every((id) => typeof id === 'string' && id.length > 0 && id.length <= 200) && new Set(value).size === value.length }
