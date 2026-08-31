import type { DshDialogueSettingsScope } from './dsh-dialogue-types.js'
import type { HelperTargetAnswerMessage, HelperTargetOpenMessage, HostOutboundMessage } from './protocol.js'
import { projectTargetData, workspaceOfSession, type DshSessionSummary, type DshWorkspaceView } from './target-selection.js'
import { createSession, createWorkspace, listSessions, listWorkspaces, type TargetApi } from './target-service.js'

type TargetSnapshot = {
  workspaces: readonly DshWorkspaceView[]
  sessions: readonly DshSessionSummary[]
  archivedSessionIds: readonly string[]
}

/**
 * Drives the target-selection card: loads workspace/session data on `target-open`,
 * answers picks as a temporary in-memory target, creates workspaces/sessions on
 * demand, and mirrors failures back to the card as a retryable error state. The
 * persisted settings default remains authoritative whenever it exists.
 */
export class TargetController {
  private lastRequestId?: number
  private lastSnapshot?: TargetSnapshot

  public constructor(
    private readonly api: TargetApi,
    private readonly settings: DshDialogueSettingsScope,
    private readonly send: (message: HostOutboundMessage) => void,
    private readonly selectTemporaryTarget: (sessionId: string, workspaceId: string | null) => void = () => {},
  ) {}

  public async open(message: HelperTargetOpenMessage): Promise<void> {
    this.lastRequestId = message.requestId
    try {
      this.lastSnapshot = await this.loadSnapshot()
      this.publish(this.lastRequestId)
    } catch {
      this.publishError(this.lastRequestId, '数据加载失败，请重试')
    }
  }

  public async answer(message: HelperTargetAnswerMessage): Promise<void> {
    const requestId = this.lastRequestId
    try {
      if (message.newWorkspace && message.path !== undefined) {
        await this.answerWorkspaceCreate(message.path)
        return
      }
      if (message.newBlank) {
        await this.answerNewBlank(message.requestId, message.workspaceId)
        return
      }
      if (message.sessionId !== null) {
        await this.answerSessionPick(message.sessionId, message.workspaceId)
      }
    } catch {
      if (requestId !== undefined) this.publishError(requestId, '操作失败，请重试')
    }
  }

  /** Registers the chosen directory, then re-publishes so the card can enter its second level. */
  private async answerWorkspaceCreate(path: string): Promise<void> {
    await createWorkspace(this.api, path)
    this.lastSnapshot = await this.loadSnapshot()
    if (this.lastRequestId !== undefined) this.publish(this.lastRequestId)
  }

  /** "+ 新对话": the host mints a session; its true workspace ownership is then derived from a fresh workspace.list (the attach result decides, never the UI's hint). */
  private async answerNewBlank(requestId: number, workspaceId: string | null): Promise<void> {
    const created = await createSession(this.api, workspaceId ?? undefined)
    this.lastSnapshot = await this.loadSnapshot()
    const derived = workspaceOfSession(created.sessionId, this.lastSnapshot.workspaces)
    this.selectTemporaryTarget(created.sessionId, derived)
  }

  /** Selecting an existing session: the owning workspace is derived fresh, never trusted from the UI. */
  private async answerSessionPick(sessionId: string, workspaceId: string | null): Promise<void> {
    const derived = workspaceId ?? await this.deriveOwnership(sessionId)
    this.selectTemporaryTarget(sessionId, derived)
  }

  private async deriveOwnership(sessionId: string): Promise<string | null> {
    if (this.lastSnapshot === undefined) {
      this.lastSnapshot = await this.loadSnapshot()
    }
    return workspaceOfSession(sessionId, this.lastSnapshot.workspaces)
  }

  private async loadSnapshot(): Promise<TargetSnapshot> {
    const [workspacesResponse, sessions] = await Promise.all([listWorkspaces(this.api), listSessions(this.api)])
    return { workspaces: workspacesResponse.items, sessions, archivedSessionIds: workspacesResponse.archivedSessionIds }
  }

  private publish(requestId: number): void {
    if (this.lastSnapshot === undefined) return
    const settings = this.settings.get()
    const data = projectTargetData(this.lastSnapshot.workspaces, this.lastSnapshot.sessions, this.lastSnapshot.archivedSessionIds)
    this.send({
      kind: 'target-request',
      requestId,
      workspaces: data.workspaces,
      sessionsByWorkspace: data.sessionsByWorkspace,
      ungrouped: data.ungrouped,
      defaultWorkspaceId: settings.defaultWorkspaceId,
      defaultSessionId: settings.defaultSessionId,
    })
  }

  private publishError(requestId: number, message: string): void {
    this.send({
      kind: 'target-request',
      requestId,
      workspaces: [],
      sessionsByWorkspace: {},
      ungrouped: [],
      defaultWorkspaceId: null,
      defaultSessionId: null,
      error: message,
    })
  }
}
