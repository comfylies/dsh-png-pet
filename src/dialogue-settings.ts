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

export const dialogueSettingsSchema = {
  defaults: dialogueSettingsDefaults,
  parse: validateDialogueSettings,
} as const

export function validateDialogueSettings(value: unknown): DialogueSettings {
  if (value === null || typeof value !== 'object' || Array.isArray(value)) throw new Error('dialogueSettings')

  const settings = value as Record<string, unknown>
  const { defaultSessionId, previewEnabled, previewMaxChars } = settings
  if (defaultSessionId !== null && (typeof defaultSessionId !== 'string' || defaultSessionId.length === 0)) throw new Error('defaultSessionId')
  if (typeof previewEnabled !== 'boolean') throw new Error('previewEnabled')
  if (typeof previewMaxChars !== 'number' || !Number.isInteger(previewMaxChars) || previewMaxChars < 80 || previewMaxChars > 2000) throw new Error('previewMaxChars')

  return { defaultSessionId, previewEnabled, previewMaxChars }
}
