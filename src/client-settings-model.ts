import {
  type DialogueSettings,
  validateDialogueSettings,
} from './dialogue-settings.js'

export type SessionOption = {
  id: string
  title: string
}

export function projectSessionOptions(rows: readonly { id: string, displayTitle: string }[]): SessionOption[] {
  return rows.map(({ id, displayTitle }) => ({ id, title: displayTitle }))
}

export { type DialogueSettings, validateDialogueSettings }
