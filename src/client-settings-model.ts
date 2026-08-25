export type DialogueSettings = {
  defaultSessionId: string | null
  previewEnabled: boolean
  previewMaxChars: number
}

export type SessionOption = {
  id: string
  title: string
}

export function projectSessionOptions(rows: readonly { id: string, displayTitle: string }[]): SessionOption[] {
  return rows.map(({ id, displayTitle }) => ({ id, title: displayTitle }))
}
