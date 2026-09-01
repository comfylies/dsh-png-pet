import type { DialogueController } from './dialogue-controller.js'
import type { DshDialogueSettingsScope } from './dsh-dialogue-types.js'
import type { HelperRandomChatOpenMessage, HostOutboundMessage } from './protocol.js'
import { createSession, listWorkspaces, type TargetApi } from './target-service.js'

/**
 * Converts one deliberate Helper bubble click into an isolated workspace
 * session.  It never schedules, searches, or creates a session by itself.
 */
export class RandomChatController {
  private nextRequestId = 1_000_000_000

  public constructor(
    private readonly api: TargetApi,
    private readonly settings: DshDialogueSettingsScope,
    private readonly dialogue: Pick<DialogueController, 'setRandomChatTarget' | 'clearRandomChatTarget' | 'startRandomChatTopic'>,
    private readonly send: (message: HostOutboundMessage) => void,
    private readonly choose: <T>(items: readonly T[]) => T = (items) => items[Math.floor(Math.random() * items.length)],
  ) {}

  public async open(message: HelperRandomChatOpenMessage): Promise<void> {
    const settings = this.settings.get()
    if (!settings.randomChatEnabled || !settings.randomChatBrowseOnOpen || settings.randomChatWorkspaceIds.length === 0) {
      this.send({ kind: 'random-chat-error', invitationId: message.invitationId, reason: 'not-configured' })
      return
    }

    try {
      const workspaceId = this.choose(settings.randomChatWorkspaceIds)
      const workspaces = await listWorkspaces(this.api)
      if (!workspaces.items.some((workspace) => workspace.workspaceId === workspaceId)) {
        this.send({ kind: 'random-chat-error', invitationId: message.invitationId, reason: 'not-configured' })
        return
      }
      const created = await createSession(this.api, workspaceId)
      this.dialogue.setRandomChatTarget(created.sessionId, workspaceId)
      this.send({ kind: 'random-chat-ready', invitationId: message.invitationId })
      this.nextRequestId++
      await this.dialogue.startRandomChatTopic(this.nextRequestId, message.topic)
    } catch {
      this.send({ kind: 'random-chat-error', invitationId: message.invitationId, reason: 'unavailable' })
    }
  }

  public dialogueClosed(): void {
    this.dialogue.clearRandomChatTarget()
  }
}
