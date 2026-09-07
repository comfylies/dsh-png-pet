import type { DialogueSettings as HostDialogueSettings } from './dialogue-settings.js'

/**
 * The browser receives Host-resolved settings only.  A type-only import keeps
 * the client bundle free of the Host schema while preventing the two settings
 * views from drifting when a persisted preference is added.
 */
export type DialogueSettings = HostDialogueSettings

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
