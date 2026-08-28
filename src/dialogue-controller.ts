import { appendFileSync } from 'node:fs'
import { tmpdir } from 'node:os'
import { join } from 'node:path'

import { createUserMessage } from '@deepseek-ai/dsh-llm'
import type { ImageAttachmentRef } from '@deepseek-ai/dsh-attachment'

import { extractDialogueHistory, turnAssistantText } from './dialogue-history.js'
import type { DialogueSettings } from './dialogue-settings.js'
import type { DshDialogueContext, DshSessionEvent } from './dsh-dialogue-types.js'
import { REPLY_MAX_CHARS, type HelperInputMessage, type HostOutboundMessage } from './protocol.js'

// TEMP-DIAG: reply delivery diagnostics for the "stuck on generating" investigation.
// Records only error categories and step outcomes — never input text. Remove after verification.
const diagPath = join(tmpdir(), 'dsh-png-pet-reply-diag.log')

function diag(entry: string): void {
  try {
    appendFileSync(diagPath, `${new Date().toISOString()} ${entry}\n`)
  } catch {
    // diagnostics must never break the input pipeline
  }
}

type Request = {
  requestId: number
  sessionId: string
  previewEnabled: boolean
  previewMaxChars: number
  preview: string
}

export class DialogueController {
  private readonly requestsByMessageId = new Map<string, Request>()
  private readonly pendingTurnRequests = new Map<string, Request[]>()
  private readonly requestsByTurn = new Map<string, Request>()
  private readonly activeRequests = new Map<number, Request>()
  private lastSettings: DialogueSettings
  private currentInputRequestId?: number
  private currentInputSessionId?: string

  public constructor(
    private readonly ctx: DshDialogueContext,
    private readonly send: (message: HostOutboundMessage) => void,
  ) {
    this.lastSettings = ctx.settings.get()
  }

  public publishConversationConfig(): void {
    const settings = this.ctx.settings.get()
    this.lastSettings = settings
    this.send({
      kind: 'conversation-config',
      previewEnabled: settings.previewEnabled,
      previewMaxChars: settings.previewMaxChars,
      defaultSessionId: settings.defaultSessionId,
      defaultWorkspaceId: settings.defaultWorkspaceId,
    })
    if (!settings.previewEnabled) this.clearAll('disabled')
  }

  public settingsChanged(settings = this.ctx.settings.get(), previous = this.lastSettings): void {
    this.lastSettings = settings
    this.send({
      kind: 'conversation-config',
      previewEnabled: settings.previewEnabled,
      previewMaxChars: settings.previewMaxChars,
      defaultSessionId: settings.defaultSessionId,
      defaultWorkspaceId: settings.defaultWorkspaceId,
    })

    if (previous.previewEnabled && !settings.previewEnabled) {
      this.clearAll('disabled')
      return
    }
    if (previous.defaultSessionId !== settings.defaultSessionId) {
      if (this.currentInputSessionId === previous.defaultSessionId) {
        this.currentInputRequestId = undefined
        this.currentInputSessionId = undefined
      }
      this.clearAll('cancelled')
      return
    }

    for (const request of this.activeRequests.values()) {
      request.previewEnabled = settings.previewEnabled
      request.previewMaxChars = settings.previewMaxChars
      if (request.preview.length <= settings.previewMaxChars) continue
      request.preview = retainTail(request.preview, settings.previewMaxChars)
      if (request.previewEnabled && request.preview.length > 0) {
        this.send({ kind: 'reply-preview', requestId: request.requestId, text: request.preview, completed: false })
      }
    }
  }

  public async acceptInput(input: HelperInputMessage): Promise<void> {
    try {
      await this.acceptInputUnsafe(input)
    } catch {
      this.currentInputRequestId = undefined
      this.currentInputSessionId = undefined
      this.clearAll('cancelled')
      this.send({ kind: 'input-status', requestId: input.requestId, status: 'rejected' })
    }
  }

  private async acceptInputUnsafe(input: HelperInputMessage): Promise<void> {
    this.currentInputRequestId = input.requestId
    this.currentInputSessionId = undefined
    this.clearAll('next-input')
    const settings = this.ctx.settings.get()
    const sessionId = settings.defaultSessionId
    if (sessionId === null) {
      this.send({ kind: 'input-status', requestId: input.requestId, status: 'no-default-session' })
      return
    }
    this.currentInputSessionId = sessionId

    let agent = this.ctx.agents.get(sessionId)
    if (agent === undefined) {
      try {
        const resumeOptions: { resumeSessionId: string, agentOptions?: { provider: string, model: string } } = {
          resumeSessionId: sessionId,
        }
        const defaultModel = this.readDefaultModel()
        if (defaultModel !== undefined) resumeOptions.agentOptions = defaultModel
        agent = (await this.ctx.agents.resume(resumeOptions))?.agent
      } catch {
        agent = undefined
      }
    }
    if (!this.isCurrentInput(input.requestId)) return
    if (agent === undefined) {
      this.send({ kind: 'input-status', requestId: input.requestId, status: 'session-unavailable' })
      return
    }

    const content = await this.buildContent(input)
    const message = createUserMessage({
      content,
      source: { kind: 'user' },
    })
    const request: Request = {
      requestId: input.requestId,
      sessionId,
      previewEnabled: settings.previewEnabled,
      previewMaxChars: settings.previewMaxChars,
      preview: '',
    }
    this.requestsByMessageId.set(message.id, request)
    this.activeRequests.set(request.requestId, request)
    this.send({ kind: 'input-status', requestId: input.requestId, status: agent.status === 'running' ? 'queued' : 'sent' })

    try {
      await agent.followup(message)
    } catch {
      if (!this.isCurrentInput(input.requestId)) return
      this.removeRequest(request)
      this.send({ kind: 'input-status', requestId: input.requestId, status: 'rejected' })
    }
  }

  /** Images become durable attachment refs (real binary upload); files become path text the model can read. */
  private async buildContent(input: HelperInputMessage): Promise<Array<{ type: 'text', text: string } | { type: 'image', attachment: ImageAttachmentRef }>> {
    const blocks: Array<{ type: 'text', text: string } | { type: 'image', attachment: ImageAttachmentRef }> = []
    let fileText = ''
    for (const attachment of input.attachments ?? []) {
      if (attachment.type === 'image') {
        if (this.ctx.attachments === undefined) throw new Error('attachments service unavailable')
        const ref = await this.ctx.attachments.saveImage({
          data: Buffer.from(attachment.base64, 'base64'),
          mediaType: attachment.mediaType,
          ...(attachment.name === undefined ? {} : { name: attachment.name }),
        })
        blocks.push({ type: 'image', attachment: ref })
        continue
      }
      const name = attachment.name ?? baseName(attachment.path)
      fileText += fileText.length === 0 ? `[文件 ${name}]\n${attachment.path}` : `\n[文件 ${name}]\n${attachment.path}`
    }
    const text = [input.text, fileText].filter((part) => part.length > 0).join('\n')
    if (text.length > 0) blocks.push({ type: 'text', text })
    return blocks
  }

  /** The deployment's default provider/model so a resumed agent can assemble its persona prompt. */
  private readDefaultModel(): { provider: string, model: string } | undefined {
    try {
      const selection = this.ctx.agentDefaultModel?.currentSelection()
      if (selection !== undefined
        && typeof selection.provider === 'string' && selection.provider.length > 0
        && typeof selection.model === 'string' && selection.model.length > 0) {
        return { provider: selection.provider, model: selection.model }
      }
    } catch {
      // A missing default model must never break the input pipeline.
    }
    return undefined
  }

  /** Aborts the live turn. The terminal reply-preview/status are driven by the resulting aborted turn/end. */
  public stop(requestId: number): void {
    const settings = this.ctx.settings.get()
    const sessionId = this.currentInputSessionId ?? settings.defaultSessionId
    if (sessionId === null || sessionId === undefined) return
    const agent = this.ctx.agents.get(sessionId)
    if (agent === undefined) return
    if (agent.status === 'running') {
      agent.cancel({ kind: 'user' })
      return
    }
    // No live turn to abort: finalize the request locally so the UI never sticks.
    const request = this.activeRequests.get(requestId)
    if (request !== undefined) {
      this.removeRequest(request)
      this.send({ kind: 'input-status', requestId, status: 'stopped' })
    }
  }

  public observeEvent(sessionId: string, event: DshSessionEvent | unknown): void {
    if (!isSessionEvent(event)) return
    if (event.type === 'user/message') {
      const messageId = readMessageId(event.data)
      if (messageId === undefined) return
      const request = this.requestsByMessageId.get(messageId)
      if (request === undefined || request.sessionId !== sessionId) return
      this.requestsByMessageId.delete(messageId)
      const pending = this.pendingTurnRequests.get(sessionId) ?? []
      pending.push(request)
      this.pendingTurnRequests.set(sessionId, pending)
      return
    }

    const turn = readTurn(event.data)
    if (turn === undefined) return
    const key = turnKey(sessionId, turn)
    if (event.type === 'turn/start') {
      const request = this.pendingTurnRequests.get(sessionId)?.shift()
      if (request !== undefined) this.requestsByTurn.set(key, request)
      return
    }

    const request = this.requestsByTurn.get(key)
    if (event.type === 'assistant/chunk') {
      if (request === undefined) return
      const text = readTextDelta(event.data)
      if (text === undefined || !request.previewEnabled) return
      request.preview = retainTail(request.preview + text, request.previewMaxChars)
      if (request.preview.length > 0) {
        this.send({ kind: 'reply-preview', requestId: request.requestId, text: request.preview, completed: false })
      }
      return
    }

    if (event.type === 'turn/end') {
      const reason = readTurnEndReason(event.data)
      if (request !== undefined) {
        if (request.previewEnabled && request.preview.length > 0) {
          this.send({ kind: 'reply-preview', requestId: request.requestId, text: request.preview, completed: true })
        }
        this.requestsByTurn.delete(key)
        this.removeRequest(request)
      }
      if (this.currentInputRequestId !== undefined) {
        if (reason === 'aborted') {
          this.send({ kind: 'input-status', requestId: this.currentInputRequestId, status: 'stopped' })
        } else if (reason === 'interrupted') {
          this.send({ kind: 'input-status', requestId: this.currentInputRequestId, status: 'interrupted' })
        } else if (reason === 'error' || reason === 'max-tokens' || reason === 'blocked') {
          this.send({ kind: 'input-status', requestId: this.currentInputRequestId, status: 'failed' })
        } else {
          this.publishReply(sessionId, this.currentInputRequestId, turn)
        }
      }
    }
  }

  /** Publishes the full final text of exactly the ended turn (placeholders for tool-only turns). */
  private publishReply(sessionId: string, requestId: number, turn: number): void {
    try {
      const events = this.ctx.agents.get(sessionId)?.session?.events
      if (events === undefined) {
        diag('publishReply events=undefined')
        return
      }
      const text = turnAssistantText(events, turn)
      if (text === undefined || text === '') {
        diag('publishReply no-assistant-found')
        return
      }
      // The wire caps replies; keep the tail so the dialogue never sends nothing.
      const bounded = text.length <= REPLY_MAX_CHARS ? text : text.slice(-REPLY_MAX_CHARS)
      this.send({ kind: 'reply', requestId, text: bounded, completed: true })
      diag('publishReply sent')
    } catch (error) {
      diag(`publishReply threw ${error instanceof Error ? error.message : String(error)}`)
    }
  }

  public disablePreview(): void {
    this.clearAll('disabled')
  }

  public async requestHistory(requestId: number): Promise<void> {
    const settings = this.ctx.settings.get()
    const sessionId = this.currentInputSessionId ?? settings.defaultSessionId
    if (sessionId === null || sessionId === undefined) {
      this.send({ kind: 'conversation-history', requestId, available: false, messages: [] })
      return
    }
    const agent = this.ctx.agents.get(sessionId)
    if (agent !== undefined) {
      try {
        const messages = extractDialogueHistory(agent.session?.events ?? [])
        this.send({ kind: 'conversation-history', requestId, available: true, messages })
      } catch {
        this.send({ kind: 'conversation-history', requestId, available: false, messages: [] })
      }
      return
    }
    if (this.ctx.sessionQuery === undefined) {
      this.send({ kind: 'conversation-history', requestId, available: false, messages: [] })
      return
    }
    try {
      const snapshot = await this.ctx.sessionQuery.readSession(sessionId)
      const messages = extractDialogueHistory(snapshot.events)
      this.send({ kind: 'conversation-history', requestId, available: true, messages })
    } catch {
      this.send({ kind: 'conversation-history', requestId, available: false, messages: [] })
    }
  }

  public sessionUnavailable(sessionId: string): void {
    if (this.currentInputSessionId === sessionId) {
      this.currentInputRequestId = undefined
      this.currentInputSessionId = undefined
    }
    this.clearForSession(sessionId, 'session-unavailable')
    const settings = this.ctx.settings.get()
    this.clearConfiguredSession(sessionId, settings)
  }

  public helperClosed(): void {
    this.currentInputRequestId = undefined
    this.currentInputSessionId = undefined
    this.clearAll('closed')
  }

  public dispose(): void {
    this.currentInputRequestId = undefined
    this.currentInputSessionId = undefined
    this.clearAll('cancelled')
  }

  private clearConfiguredSession(sessionId: string, settings: DialogueSettings): void {
    if (settings.defaultSessionId !== sessionId) return
    try {
      void this.ctx.settings.update({ defaultSessionId: null }).catch(() => {})
    } catch {
      // Settings cleanup is best effort; it must never affect the DSH event pipeline.
    }
  }

  private clearForSession(sessionId: string, reason: 'session-unavailable'): void {
    const requests = [...this.activeRequests.values()].filter((request) => request.sessionId === sessionId)
    for (const request of requests) {
      this.send({ kind: 'clear-preview', requestId: request.requestId, reason })
      this.removeRequest(request)
    }
  }

  private clearAll(reason: 'disabled' | 'next-input' | 'closed' | 'cancelled'): void {
    for (const request of this.activeRequests.values()) {
      this.send({ kind: 'clear-preview', requestId: request.requestId, reason })
    }
    this.requestsByMessageId.clear()
    this.pendingTurnRequests.clear()
    this.requestsByTurn.clear()
    this.activeRequests.clear()
  }

  private removeRequest(request: Request): void {
    this.activeRequests.delete(request.requestId)
    for (const [messageId, mappedRequest] of this.requestsByMessageId) {
      if (mappedRequest === request) this.requestsByMessageId.delete(messageId)
    }
    for (const [sessionId, pending] of this.pendingTurnRequests) {
      const remaining = pending.filter((mappedRequest) => mappedRequest !== request)
      if (remaining.length === 0) this.pendingTurnRequests.delete(sessionId)
      else this.pendingTurnRequests.set(sessionId, remaining)
    }
    for (const [key, mappedRequest] of this.requestsByTurn) {
      if (mappedRequest === request) this.requestsByTurn.delete(key)
    }
  }

  private isCurrentInput(requestId: number): boolean {
    return this.currentInputRequestId === requestId
  }
}

function baseName(path: string): string {
  const cleaned = path.replace(/[/\\]+$/, '')
  const parts = cleaned.split(/[/\\]/)
  return parts.at(-1) ?? ''
}

function isSessionEvent(value: unknown): value is DshSessionEvent {
  return value !== null && typeof value === 'object' && !Array.isArray(value) && typeof (value as { type?: unknown }).type === 'string'
}

function readMessageId(data: unknown): string | undefined {
  const value = readRecord(data)
  const id = value?.id
  return typeof id === 'string' && id.length > 0 ? id : undefined
}

function readTurn(data: unknown): number | undefined {
  const turn = readRecord(data)?.turn
  return typeof turn === 'number' && Number.isSafeInteger(turn) && turn >= 0 ? turn : undefined
}

function readTurnEndReason(data: unknown): string | undefined {
  const kind = readRecord(readRecord(data)?.reason)?.kind
  return typeof kind === 'string' ? kind : undefined
}

function readTextDelta(data: unknown): string | undefined {
  const chunk = readRecord(readRecord(data)?.chunk)
  if (chunk?.type !== 'text-delta' || typeof chunk.text !== 'string' || chunk.text.length === 0) return undefined
  return chunk.text
}

function readRecord(value: unknown): Record<string, unknown> | undefined {
  return value !== null && typeof value === 'object' && !Array.isArray(value)
    ? value as Record<string, unknown>
    : undefined
}

function retainTail(value: string, maxChars: number): string {
  return value.length <= maxChars ? value : value.slice(-maxChars)
}

function turnKey(sessionId: string, turn: number): string {
  return `${sessionId}:${turn}`
}
