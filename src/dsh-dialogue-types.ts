import type { SettingsProvider, SettingsScope } from '@deepseek-ai/dsh-settings'
import type { UserMessage } from '@deepseek-ai/dsh-llm'

import type { DialogueSettings } from './dialogue-settings.js'

export type DshAgent = {
  status: string
  followup(message: UserMessage): void | Promise<void>
  session?: { events: readonly unknown[] }
}

export type DshResumedAgent = {
  agent: DshAgent
}

export type DshDialogueSettingsScope = Pick<SettingsScope<DialogueSettings>, 'get' | 'update' | 'watch'>

export type DshDialogueContext = {
  agents: {
    get(sessionId: string): DshAgent | undefined
    resume(options: { resumeSessionId: string }): DshResumedAgent | undefined | Promise<DshResumedAgent | undefined>
  }
  settings: DshDialogueSettingsScope
}

export type DshSettingsProvider = Pick<SettingsProvider, 'register'>

export type DshSessionEvent = {
  type: string
  data?: unknown
}
