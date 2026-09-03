export type DialogueSettings = {
  defaultSessionId: string | null
  defaultWorkspaceId: string | null
  previewEnabled: boolean
  previewMaxChars: number
  approvalSurface: 'web' | 'pet'
  randomChatEnabled: boolean
  randomChatBrowseOnOpen: boolean
  randomChatWorkspaceIds: string[]
  randomChatMinIntervalMinutes: number
  randomChatMaxIntervalMinutes: number
  randomChatCustomPrompts: string[]
  randomChatTestNonce: number
  scale: 0.75 | 1 | 1.25 | 1.5
  reducedMotion: boolean
  physicsEnabled: boolean
  physicsBouncePercent: number
  petPlacement: 'top-left' | 'top-center' | 'top-right' | 'middle-left' | 'center' | 'middle-right' | 'bottom-left' | 'bottom-center' | 'bottom-right'
  dialoguePlacement: 'near-pet' | 'top-left' | 'top-center' | 'top-right' | 'middle-left' | 'center' | 'middle-right' | 'bottom-left' | 'bottom-center' | 'bottom-right'
  dialogueWidth: number
  dialogueHeight: number
}

export type SessionOption = {
  id: string
  title: string
}

export type WorkspaceSessionTree = {
  workspaces: readonly {
    id: string
    title: string
    sessions: readonly SessionOption[]
  }[]
  ungrouped: readonly SessionOption[]
}

export function projectSessionOptions(rows: readonly { id: string, displayTitle: string }[]): SessionOption[] {
  return rows.map(({ id, displayTitle }) => ({ id, title: displayTitle }))
}

/**
 * A privacy-safe projection of the client stores for the settings picker.  The
 * UI needs workspace names and membership only; it must not inspect workspace
 * paths, session cwd values, or conversation data.
 */
export function projectWorkspaceSessionTree(
  sessionRows: readonly { id: string, displayTitle: string }[],
  workspaceRows: readonly { workspaceId: string, title: string, sessionIds: readonly string[] }[],
  archivedSessionIds: readonly string[],
): WorkspaceSessionTree {
  const archived = new Set(archivedSessionIds)
  const byId = new Map(projectSessionOptions(sessionRows).map((session) => [session.id, session]))
  const grouped = new Set<string>()
  const workspaces = workspaceRows.map((workspace) => {
    const sessions = workspace.sessionIds.flatMap((sessionId) => {
      const session = byId.get(sessionId)
      if (session === undefined || archived.has(session.id)) return []
      grouped.add(session.id)
      return [session]
    })
    return { id: workspace.workspaceId, title: workspace.title, sessions }
  })
  const ungrouped = projectSessionOptions(sessionRows).filter((session) => !archived.has(session.id) && !grouped.has(session.id))
  return { workspaces, ungrouped }
}
