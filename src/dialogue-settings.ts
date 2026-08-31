import z from '@deepseek-ai/schemastery'

export const petPlacements = ['center', 'top-left', 'top-right', 'bottom-left', 'bottom-right'] as const
export const dialoguePlacements = ['near-pet', ...petPlacements] as const

export type PetPlacement = typeof petPlacements[number]
export type DialoguePlacement = typeof dialoguePlacements[number]

export type DialogueSettings = {
  defaultSessionId: string | null
  /** Prompt/hint only: the session list's default landing workspace. Never the conversation target. */
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

export const dialogueSettingsDefaults: Readonly<DialogueSettings> = Object.freeze({
  defaultSessionId: null,
  defaultWorkspaceId: null,
  previewEnabled: true,
  previewMaxChars: 2000,
  scale: 1,
  reducedMotion: false,
  petPlacement: 'center',
  dialoguePlacement: 'near-pet',
  dialogueWidth: 320,
  dialogueHeight: 420,
})

export const dialogueSettingsSchema = z.transform(
  z.object({
    defaultSessionId: z.union([z.string().min(1), z.const(null)]).default(null),
    defaultWorkspaceId: z.union([z.string().min(1), z.const(null)]).default(null),
    previewEnabled: z.boolean().default(true),
    previewMaxChars: z.number().step(1).min(80).max(8000).default(2000),
    scale: z.union([z.const(0.75), z.const(1), z.const(1.25), z.const(1.5)]).default(1),
    reducedMotion: z.boolean().default(false),
    petPlacement: z.union([z.const('center'), z.const('top-left'), z.const('top-right'), z.const('bottom-left'), z.const('bottom-right')]).default('center'),
    dialoguePlacement: z.union([z.const('near-pet'), z.const('center'), z.const('top-left'), z.const('top-right'), z.const('bottom-left'), z.const('bottom-right')]).default('near-pet'),
    dialogueWidth: z.number().step(1).min(220).max(4000).default(320),
    dialogueHeight: z.number().step(1).min(240).max(3000).default(420),
  }).default(dialogueSettingsDefaults),
  (settings) => ({
    defaultSessionId: settings.defaultSessionId ?? null,
    defaultWorkspaceId: settings.defaultWorkspaceId ?? null,
    previewEnabled: settings.previewEnabled ?? true,
    previewMaxChars: settings.previewMaxChars ?? 2000,
    scale: settings.scale ?? 1,
    reducedMotion: settings.reducedMotion ?? false,
    petPlacement: settings.petPlacement ?? 'center',
    dialoguePlacement: settings.dialoguePlacement ?? 'near-pet',
    dialogueWidth: settings.dialogueWidth ?? 320,
    dialogueHeight: settings.dialogueHeight ?? 420,
  }),
).default(dialogueSettingsDefaults)

export function validateDialogueSettings(value: unknown): DialogueSettings {
  return dialogueSettingsSchema(value as never)
}
