import z from '@deepseek-ai/schemastery'

export type DialogueSettings = {
  defaultSessionId: string | null
  /** Prompt/hint only: the session list's default landing workspace. Never the conversation target. */
  defaultWorkspaceId: string | null
  previewEnabled: boolean
  previewMaxChars: number
}

export const dialogueSettingsDefaults: Readonly<DialogueSettings> = Object.freeze({
  defaultSessionId: null,
  defaultWorkspaceId: null,
  previewEnabled: true,
  previewMaxChars: 2000,
})

export const dialogueSettingsSchema = z.transform(
  z.object({
    defaultSessionId: z.union([z.string().min(1), z.const(null)]).default(null),
    defaultWorkspaceId: z.union([z.string().min(1), z.const(null)]).default(null),
    previewEnabled: z.boolean().default(true),
    previewMaxChars: z.number().step(1).min(80).max(8000).default(2000),
  }).default(dialogueSettingsDefaults),
  (settings) => ({
    defaultSessionId: settings.defaultSessionId ?? null,
    defaultWorkspaceId: settings.defaultWorkspaceId ?? null,
    previewEnabled: settings.previewEnabled ?? true,
    previewMaxChars: settings.previewMaxChars ?? 2000,
  }),
).default(dialogueSettingsDefaults)

export function validateDialogueSettings(value: unknown): DialogueSettings {
  return dialogueSettingsSchema(value as never)
}
