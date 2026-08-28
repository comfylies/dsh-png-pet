import { TARGET_PATH_MAX_CHARS, TARGET_TITLE_MAX_CHARS, type TargetSession, type TargetWorkspace } from './protocol.js'

/**
 * Minimal host-side shapes of the DSH API proxy objects this feature consumes.
 * Runtime values come from `ctx.apiProxy`; the structure is pinned by the
 * dsh-host-apiproxy contract (`workspace.list` / `session.list`).
 */
export type DshWorkspaceView = {
  workspaceId: string
  path: string
  title: string
  sessionIds: readonly string[]
}

export type DshSessionSummary = {
  sessionId: string
  updatedAt: number
  blank: boolean
  cwd?: string
  /** Projection block whose `values.title` carries the durable display title. */
  projections?: {
    values: Readonly<Record<string, unknown>>
  }
}

export type TargetData = {
  workspaces: readonly TargetWorkspace[]
  sessionsByWorkspace: Readonly<Record<string, readonly TargetSession[]>>
  ungrouped: readonly TargetSession[]
}

/**
 * Reduces the host's workspace/session views into the target card's data:
 * workspaces in registry order, each workspace's sessions (its accounted ids
 * that are still listed and not archived) in `updatedAt` descending order, and
 * the ungrouped bucket (listed sessions owned by no workspace), also descending.
 */
export function projectTargetData(
  workspaces: readonly DshWorkspaceView[],
  sessions: readonly DshSessionSummary[],
  archivedSessionIds: readonly string[],
): TargetData {
  const summariesById = new Map(sessions.map((session) => [session.sessionId, session]))
  const archived = new Set(archivedSessionIds)
  const grouped = new Set<string>()
  const sessionsByWorkspace: Record<string, readonly TargetSession[]> = {}

  for (const workspace of workspaces) {
    const members = workspace.sessionIds
      .map((id) => summariesById.get(id))
      .filter((session): session is DshSessionSummary => session !== undefined && !archived.has(session.sessionId))
      .sort((first, second) => second.updatedAt - first.updatedAt)
    sessionsByWorkspace[workspace.workspaceId] = members.map(toTargetSession)
    for (const member of members) grouped.add(member.sessionId)
  }

  const ungrouped = sessions
    .filter((session) => !grouped.has(session.sessionId) && !archived.has(session.sessionId))
    .sort((first, second) => second.updatedAt - first.updatedAt)
    .map(toTargetSession)

  return {
    workspaces: workspaces.map((workspace) => ({
      id: workspace.workspaceId,
      title: workspace.title,
      path: workspace.path.slice(0, TARGET_PATH_MAX_CHARS),
    })),
    sessionsByWorkspace,
    ungrouped,
  }
}

/**
 * Display title: the durable title projection, else the working-directory
 * basename, else empty (the card renders an empty title as "blank session").
 */
export function sessionTitle(session: DshSessionSummary): string {
  const projected = session.projections?.values.title
  if (typeof projected === 'string' && projected.length > 0) {
    return projected.slice(0, TARGET_TITLE_MAX_CHARS)
  }
  const base = directoryBasename(session.cwd)
  return base.length > 0 ? base.slice(0, TARGET_TITLE_MAX_CHARS) : ''
}

/** The workspace owning a session id, derived from `sessionIds` membership; null when ungrouped. */
export function workspaceOfSession(
  sessionId: string,
  workspaces: readonly DshWorkspaceView[],
): string | null {
  for (const workspace of workspaces) {
    if (workspace.sessionIds.includes(sessionId)) return workspace.workspaceId
  }
  return null
}

/**
 * connectWorkspace reuse decision: the workspace's existing blank session —
 * blank, not archived, cwd matching the workspace path, and accounted — or
 * undefined when a fresh session must be created instead.
 */
export function findBlankSession(
  workspace: DshWorkspaceView,
  sessions: readonly DshSessionSummary[],
  archivedSessionIds: readonly string[],
): DshSessionSummary | undefined {
  const archived = new Set(archivedSessionIds)
  return sessions.find((session) =>
    session.blank
    && !archived.has(session.sessionId)
    && session.cwd === workspace.path
    && workspace.sessionIds.includes(session.sessionId))
}

function toTargetSession(session: DshSessionSummary): TargetSession {
  return { id: session.sessionId, title: sessionTitle(session), blank: session.blank }
}

function directoryBasename(cwd: string | undefined): string {
  if (cwd === undefined || cwd.length === 0) return ''
  const cleaned = cwd.replace(/[/\\]+$/, '')
  const parts = cleaned.split(/[/\\]/)
  const last = parts.at(-1) ?? ''
  return last.length > 0 && !/^[A-Za-z]:$/.test(last) ? last : ''
}
