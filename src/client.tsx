import {
  createElement,
  useState,
  useSyncExternalStore,
  type ChangeEvent,
  type ReactElement,
} from 'react'

import { projectSessionOptions, type DialogueSettings } from './client-settings-model.js'

const SETTINGS_NAMESPACE = 'dsh-png-pet'
const DEFAULT_SETTINGS: DialogueSettings = {
  defaultSessionId: null,
  previewEnabled: false,
  previewMaxChars: 480,
}
const SESSION_LIST_UNAVAILABLE = '会话列表尚未就绪。'
const SELECTED_SESSION_UNAVAILABLE = '所选会话已不可用。'
const SETTINGS_WRITE_FAILED = '无法保存桌宠设置。'

type SnapshotSource<T> = {
  getSnapshot(): T
  subscribe(listener: () => void): () => void
}

type SessionRow = {
  id: string
  displayTitle: string
}

type SessionListSnapshot = {
  ids: readonly string[] | undefined
  byId: Readonly<Record<string, SessionRow | undefined>> | undefined
}

type SettingsSnapshot<T> = {
  status: 'loading' | 'ready' | 'unavailable'
  value: T | undefined
}

type SettingsScope<T> = SnapshotSource<SettingsSnapshot<T>> & {
  set(field: string, value: unknown): Promise<void>
}

type SettingsScopeService = {
  bind<T>(spec: { namespace: string }): SettingsScope<T>
}

type ClientContext = {
  sessions: {
    list: SnapshotSource<SessionListSnapshot>
  }
  get(service: 'settingsScope'): SettingsScopeService
  slots: {
    inject(name: 'settings.section', factory: () => unknown): void
    register(options: {
      name: 'settings.section'
      id: string
      order: number
      label: string
    }, component: () => ReactElement): unknown
  }
}

// connection and remote are the built-in settingsScope transport dependencies, not plugin Remote APIs.
export const inject = ['sessions', 'settingsScope', 'connection', 'remote', 'slots']

export function apply(ctx: ClientContext): void {
  const settings = bindDialogueSettings(ctx)

  ctx.slots.inject('settings.section', () => ctx.slots.register({
    name: 'settings.section',
    id: SETTINGS_NAMESPACE,
    order: 100,
    label: '桌宠',
  }, () => createElement(DesktopPetSettingsSection, { sessions: ctx.sessions.list, settings })))
}

export function bindDialogueSettings(ctx: Pick<ClientContext, 'get'>): SettingsScope<DialogueSettings> {
  return ctx.get('settingsScope').bind<DialogueSettings>({ namespace: SETTINGS_NAMESPACE })
}

export async function writeDialogueSetting(
  settings: Pick<SettingsScope<DialogueSettings>, 'set'>,
  field: keyof DialogueSettings,
  value: string | boolean | number | null,
): Promise<boolean> {
  if (field === 'previewMaxChars' && (
    typeof value !== 'number'
    || !Number.isInteger(value)
    || value < 80
    || value > 2000
  )) return false

  try {
    await settings.set(field, value)
    return true
  } catch {
    return false
  }
}

export function projectDialogueSettingsView(
  sessionSnapshot: SessionListSnapshot,
  settingsSnapshot: SettingsSnapshot<DialogueSettings>,
  writeFailed: boolean,
): {
  options: ReturnType<typeof projectSessionOptions>
  value: DialogueSettings
  listUnavailable: boolean
  selectedSessionUnavailable: boolean
  error: string | undefined
} {
  const sessionRows = (sessionSnapshot.ids ?? []).flatMap((id) => {
    const row = sessionSnapshot.byId?.[id]
    return row === undefined ? [] : [{ id: row.id, displayTitle: row.displayTitle }]
  })
  const options = projectSessionOptions(sessionRows)
  const value = settingsSnapshot.value ?? DEFAULT_SETTINGS
  const listUnavailable = sessionSnapshot.ids === undefined || sessionSnapshot.byId === undefined
  const selectedSessionUnavailable = value.defaultSessionId !== null
    && !options.some((option) => option.id === value.defaultSessionId)
  const error = listUnavailable
    ? SESSION_LIST_UNAVAILABLE
    : selectedSessionUnavailable
      ? SELECTED_SESSION_UNAVAILABLE
      : writeFailed || settingsSnapshot.status === 'unavailable'
        ? SETTINGS_WRITE_FAILED
        : undefined

  return { options, value, listUnavailable, selectedSessionUnavailable, error }
}

function DesktopPetSettingsSection({
  sessions,
  settings,
}: {
  sessions: ClientContext['sessions']['list']
  settings: SettingsScope<DialogueSettings>
}): ReactElement {
  const sessionSnapshot = useSyncExternalStore(sessions.subscribe, sessions.getSnapshot)
  const settingsSnapshot = useSyncExternalStore(settings.subscribe, settings.getSnapshot)
  const [writeFailed, setWriteFailed] = useState(false)
  const { options, value, listUnavailable, selectedSessionUnavailable, error } = projectDialogueSettingsView(
    sessionSnapshot,
    settingsSnapshot,
    writeFailed,
  )

  async function update(field: keyof DialogueSettings, nextValue: string | boolean | number | null): Promise<void> {
    setWriteFailed(!await writeDialogueSetting(settings, field, nextValue))
  }

  function onPreviewLengthChange(event: ChangeEvent<HTMLInputElement>): void {
    const nextValue = Number(event.currentTarget.value)
    if (!Number.isInteger(nextValue) || nextValue < 80 || nextValue > 2000) {
      setWriteFailed(true)
      return
    }
    void update('previewMaxChars', nextValue)
  }

  return createElement('section', { 'aria-label': '桌宠设置' },
    createElement('label', null,
      '默认会话',
      createElement('select', {
        value: selectedSessionUnavailable ? '' : (value.defaultSessionId ?? ''),
        disabled: listUnavailable,
        onChange: (event: ChangeEvent<HTMLSelectElement>) => {
          void update('defaultSessionId', event.currentTarget.value || null)
        },
      },
      createElement('option', { value: '' }, '未选择'),
      ...options.map((option) => createElement('option', { key: option.id, value: option.id }, option.title))),
    createElement('label', null,
      createElement('input', {
        type: 'checkbox',
        checked: value.previewEnabled,
        onChange: (event: ChangeEvent<HTMLInputElement>) => {
          void update('previewEnabled', event.currentTarget.checked)
        },
      }),
      '显示回复预览'),
    createElement('label', null,
      '预览长度',
      createElement('input', {
        type: 'number',
        min: 80,
        max: 2000,
        step: 1,
        value: value.previewMaxChars,
        onChange: onPreviewLengthChange,
      })),
    error === undefined ? null : createElement('p', { role: 'alert' }, error)))
}
