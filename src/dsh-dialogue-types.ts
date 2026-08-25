import type { SettingsProvider, SettingsScope } from '@deepseek-ai/dsh-settings'

import type { DialogueSettings } from './dialogue-settings.js'

export type DshUserMessage = {
  id: string
}

export type DshAgent = {
  status: string
  followup(message: DshUserMessage): void | Promise<void>
}

export type DshDialogueSettingsScope = Pick<SettingsScope<DialogueSettings>, 'get' | 'update' | 'watch'>

export type DshDialogueContext = {
  agents: {
    get(sessionId: string): DshAgent | undefined
    resume(options: { resumeSessionId: string }): DshAgent | undefined | Promise<DshAgent | undefined>
  }
  settings: DshDialogueSettingsScope
  createUserMessage(message: {
    content: readonly [{ type: 'text', text: string }]
    source: { kind: 'user' }
  }): DshUserMessage
}

export type DshSettingsProvider = Pick<SettingsProvider, 'register'>

export type DshSessionEvent = {
  type: string
  data?: unknown
}
