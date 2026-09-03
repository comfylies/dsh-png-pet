import z from '@deepseek-ai/schemastery'

/** The nine semantic anchors represented by the settings page's screen grid. */
export const petPlacements = ['top-left', 'top-center', 'top-right', 'middle-left', 'center', 'middle-right', 'bottom-left', 'bottom-center', 'bottom-right'] as const
export const dialoguePlacements = ['near-pet', ...petPlacements] as const

export type PetPlacement = typeof petPlacements[number]
export type DialoguePlacement = typeof dialoguePlacements[number]
export type ApprovalSurface = 'web' | 'pet'

export type DialogueSettings = {
  defaultSessionId: string | null
  /** Prompt/hint only: the session list's default landing workspace. Never the conversation target. */
  defaultWorkspaceId: string | null
  previewEnabled: boolean
  previewMaxChars: number
  /** The sole UI allowed to answer future DSH permission requests. */
  approvalSurface: ApprovalSurface
  /** Explicitly opt-in; without it the Helper never schedules a random invitation. */
  randomChatEnabled: boolean
  /** Separate consent for the model to use its configured web tools after a bubble click. */
  randomChatBrowseOnOpen: boolean
  /** Existing workspace IDs only; no paths or session text are stored here. */
  randomChatWorkspaceIds: string[]
  /** Random invitations are scheduled inside this user-selected inclusive range. */
  randomChatMinIntervalMinutes: number
  randomChatMaxIntervalMinutes: number
  /** Local-only bubble text supplied by the user; one display line per entry. */
  randomChatCustomPrompts: string[]
  /** Incremented only by the Harness test button; never included in persisted Helper configuration. */
  randomChatTestNonce: number
  scale: 0.75 | 1 | 1.25 | 1.5
  reducedMotion: boolean
  /** Opt-in local WPF window physics; never affects DSH sessions or requests. */
  physicsEnabled: boolean
  /** Linear 0–100 rebound slider sent only to the local Helper. */
  physicsBouncePercent: number
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
  approvalSurface: 'web',
  randomChatEnabled: false,
  randomChatBrowseOnOpen: false,
  randomChatWorkspaceIds: [],
  randomChatMinIntervalMinutes: 8,
  randomChatMaxIntervalMinutes: 24,
  randomChatCustomPrompts: [],
  randomChatTestNonce: 0,
  scale: 1,
  reducedMotion: false,
  physicsEnabled: false,
  physicsBouncePercent: 65,
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
    approvalSurface: z.union([z.const('web'), z.const('pet')]).default('web'),
    randomChatEnabled: z.boolean().default(false),
    randomChatBrowseOnOpen: z.boolean().default(false),
    randomChatWorkspaceIds: z.array(z.string().min(1).max(200)).max(8).default([]),
    randomChatMinIntervalMinutes: z.number().step(1).min(5).max(1440).default(8),
    randomChatMaxIntervalMinutes: z.number().step(1).min(5).max(1440).default(24),
    randomChatCustomPrompts: z.array(z.string().min(1).max(120)).max(12).default([]),
    randomChatTestNonce: z.number().step(1).min(0).max(2_147_483_647).default(0),
    scale: z.union([z.const(0.75), z.const(1), z.const(1.25), z.const(1.5)]).default(1),
    reducedMotion: z.boolean().default(false),
    physicsEnabled: z.boolean().default(false),
    physicsBouncePercent: z.number().step(1).min(0).max(100).default(65),
    petPlacement: z.union([z.const('top-left'), z.const('top-center'), z.const('top-right'), z.const('middle-left'), z.const('center'), z.const('middle-right'), z.const('bottom-left'), z.const('bottom-center'), z.const('bottom-right')]).default('center'),
    dialoguePlacement: z.union([z.const('near-pet'), z.const('top-left'), z.const('top-center'), z.const('top-right'), z.const('middle-left'), z.const('center'), z.const('middle-right'), z.const('bottom-left'), z.const('bottom-center'), z.const('bottom-right')]).default('near-pet'),
    dialogueWidth: z.number().step(1).min(220).max(4000).default(320),
    dialogueHeight: z.number().step(1).min(240).max(3000).default(420),
  }).default(dialogueSettingsDefaults),
  (settings) => {
    const [randomChatMinIntervalMinutes, randomChatMaxIntervalMinutes] = normalizeRandomChatInterval(
      settings.randomChatMinIntervalMinutes ?? 8,
      settings.randomChatMaxIntervalMinutes ?? 24,
    )
    return {
    defaultSessionId: settings.defaultSessionId ?? null,
    defaultWorkspaceId: settings.defaultWorkspaceId ?? null,
    previewEnabled: settings.previewEnabled ?? true,
    previewMaxChars: settings.previewMaxChars ?? 2000,
    approvalSurface: settings.approvalSurface ?? 'web',
    randomChatEnabled: settings.randomChatEnabled ?? false,
    randomChatBrowseOnOpen: settings.randomChatBrowseOnOpen ?? false,
    randomChatWorkspaceIds: uniqueWorkspaceIds(settings.randomChatWorkspaceIds ?? []),
    randomChatMinIntervalMinutes,
    randomChatMaxIntervalMinutes,
    randomChatCustomPrompts: uniqueRandomChatPrompts(settings.randomChatCustomPrompts ?? []),
    randomChatTestNonce: settings.randomChatTestNonce ?? 0,
    scale: settings.scale ?? 1,
    reducedMotion: settings.reducedMotion ?? false,
    physicsEnabled: settings.physicsEnabled ?? false,
    physicsBouncePercent: settings.physicsBouncePercent ?? 65,
    petPlacement: settings.petPlacement ?? 'center',
    dialoguePlacement: settings.dialoguePlacement ?? 'near-pet',
    dialogueWidth: settings.dialogueWidth ?? 320,
    dialogueHeight: settings.dialogueHeight ?? 420,
    }
  },
).default(dialogueSettingsDefaults)

function uniqueWorkspaceIds(ids: readonly string[]): string[] {
  if (new Set(ids).size !== ids.length) throw new Error('random chat workspace ids must be unique')
  return [...ids]
}

function normalizeRandomChatInterval(minimum: number, maximum: number): [number, number] {
  if (minimum > maximum) throw new Error('random chat minimum interval must not exceed maximum interval')
  return [minimum, maximum]
}

function uniqueRandomChatPrompts(prompts: readonly string[]): string[] {
  if (new Set(prompts).size !== prompts.length) throw new Error('random chat custom prompts must be unique')
  return [...prompts]
}

export function validateDialogueSettings(value: unknown): DialogueSettings {
  return dialogueSettingsSchema(value as never)
}
