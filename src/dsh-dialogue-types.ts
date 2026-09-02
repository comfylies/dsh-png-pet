import type { SettingsProvider, SettingsScope } from '@deepseek-ai/dsh-settings'
import type { UserMessage } from '@deepseek-ai/dsh-llm'
import type { ImageAttachmentRef, ImageMediaType } from '@deepseek-ai/dsh-attachment'

import type { DialogueSettings } from './dialogue-settings.js'

export type DshAgent = {
  status: string
  followup(message: UserMessage): void | Promise<void>
  cancel(cause: { kind: 'user' }): void
  session?: { id?: string, events: readonly unknown[] }
}

export type DshResumedAgent = {
  agent: DshAgent
}

export type DshDialogueSettingsScope = Pick<SettingsScope<DialogueSettings>, 'get' | 'update' | 'watch'>

export type DshAgentOptions = {
  provider?: string
  model?: string
}

export type DshDialogueContext = {
  agents: {
    get(sessionId: string): DshAgent | undefined
    resume(options: { resumeSessionId: string, agentOptions?: DshAgentOptions }): DshResumedAgent | undefined | Promise<DshResumedAgent | undefined>
  }
  attachments?: {
    saveImage(input: { data: Uint8Array, mediaType: ImageMediaType, name?: string }): Promise<ImageAttachmentRef>
  }
  sessionQuery?: {
    readSession(sessionId: string): Promise<{ events: readonly unknown[] }>
  }
  /** The deployment's default provider/model selection; agents resumed without it have no model and fail prompt assembly. */
  agentDefaultModel?: {
    currentSelection(): { provider: string, model: string } | undefined
  }
  settings: DshDialogueSettingsScope
}

export type DshSettingsProvider = Pick<SettingsProvider, 'register'>

export type DshSessionEvent = {
  type: string
  data?: unknown
}
