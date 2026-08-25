import z from '@deepseek-ai/schemastery'

export type DialogueSettings = {
  defaultSessionId: string | null
  previewEnabled: boolean
  previewMaxChars: number
}

export const dialogueSettingsDefaults: Readonly<DialogueSettings> = Object.freeze({
  defaultSessionId: null,
  previewEnabled: false,
  previewMaxChars: 480,
})

export const dialogueSettingsSchema = z.transform(
  z.object({
    defaultSessionId: z.union([z.string().min(1), z.const(null)]).default(null),
    previewEnabled: z.boolean().default(false),
    previewMaxChars: z.number().step(1).min(80).max(2000).default(480),
  }),
  (settings) => ({
    defaultSessionId: settings.defaultSessionId ?? null,
    previewEnabled: settings.previewEnabled ?? false,
    previewMaxChars: settings.previewMaxChars ?? 480,
  }),
)

export function validateDialogueSettings(value: unknown): DialogueSettings {
  return dialogueSettingsSchema(value as never)
}
