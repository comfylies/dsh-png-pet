import { appendFileSync } from 'node:fs'
import { tmpdir } from 'node:os'
import { join } from 'node:path'

import { createUserMessage } from '@deepseek-ai/dsh-llm'
import type { ImageAttachmentRef } from '@deepseek-ai/dsh-attachment'

import { extractDialogueHistory, turnAssistantText } from './dialogue-history.js'
import type { DialogueSettings } from './dialogue-settings.js'
import type { DshDialogueContext, DshSessionEvent } from './dsh-dialogue-types.js'
import { REPLY_MAX_CHARS, type HelperInputMessage, type HostOutboundMessage, type RandomChatTopic } from './protocol.js'

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
  origin: 'pet' | 'external'
  previewEnabled: boolean
  previewMaxChars: number
  preview: string
}

export class DialogueController {
  private readonly requestsByMessageId = new Map<string, Request>()
  private readonly pendingTurnRequests = new Map<string, Request[]>()
  private readonly requestsByTurn = new Map<string, Request>()
  private readonly activeRequests = new Map<number, Request>()
  private readonly externalUserMessages = new Map<string, number>()
  private nextExternalRequestId = Number.MAX_SAFE_INTEGER
  private lastSettings: DialogueSettings
  private currentInputRequestId?: number
  private currentInputSessionId?: string
  /** A right-click target only lives until the host exits and never changes settings. */
  private temporaryTarget?: { sessionId: string, workspaceId: string | null }
  /** An explicit random-chat click temporarily overrides the saved default until the dialogue closes. */
  private randomChatTarget?: { sessionId: string, workspaceId: string }

  public constructor(
    private readonly ctx: DshDialogueContext,
    private readonly send: (message: HostOutboundMessage) => void,
  ) {
    this.lastSettings = ctx.settings.get()
  }

  public publishConversationConfig(): void {
    const settings = this.ctx.settings.get()
    this.lastSettings = settings
    const target = this.effectiveTarget(settings)
    this.send({
      kind: 'conversation-config',
      previewEnabled: settings.previewEnabled,
      previewMaxChars: settings.previewMaxChars,
      defaultSessionId: target.sessionId,
      defaultWorkspaceId: target.workspaceId,
    })
    if (!settings.previewEnabled) this.clearAll('disabled')
  }

  public settingsChanged(settings = this.ctx.settings.get(), previous = this.lastSettings): void {
    const previousTarget = this.effectiveTarget(previous)
    this.lastSettings = settings
    const target = this.effectiveTarget(settings)
    this.send({
      kind: 'conversation-config',
      previewEnabled: settings.previewEnabled,
      previewMaxChars: settings.previewMaxChars,
      defaultSessionId: target.sessionId,
      defaultWorkspaceId: target.workspaceId,
    })

    if (previous.previewEnabled && !settings.previewEnabled) {
      this.clearAll('disabled')
      return
    }
    if (this.randomChatTarget !== undefined
      && (!settings.randomChatEnabled || !settings.randomChatBrowseOnOpen || settings.randomChatWorkspaceIds.length === 0)) {
      this.clearRandomChatTarget()
      return
    }
    if (previousTarget.sessionId !== target.sessionId) {
      if (this.currentInputSessionId === previousTarget.sessionId) {
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

  /** Right-click selection is deliberately transient; a saved default always wins. */
  public setTemporaryTarget(sessionId: string, workspaceId: string | null): void {
    const previous = this.effectiveTarget(this.ctx.settings.get())
    this.temporaryTarget = { sessionId, workspaceId }
    const next = this.effectiveTarget(this.ctx.settings.get())
    this.publishConversationConfig()
    if (previous.sessionId !== next.sessionId) {
      if (this.currentInputSessionId === previous.sessionId) {
        this.currentInputRequestId = undefined
        this.currentInputSessionId = undefined
      }
      this.clearAll('cancelled')
    }
  }

  public setRandomChatTarget(sessionId: string, workspaceId: string): void {
    const previous = this.effectiveTarget(this.ctx.settings.get())
    this.randomChatTarget = { sessionId, workspaceId }
    const next = this.effectiveTarget(this.ctx.settings.get())
    this.publishConversationConfig()
    if (previous.sessionId !== next.sessionId) this.clearAll('cancelled')
  }

  public clearRandomChatTarget(): void {
    if (this.randomChatTarget === undefined) return
    const previous = this.effectiveTarget(this.ctx.settings.get())
    this.randomChatTarget = undefined
    const next = this.effectiveTarget(this.ctx.settings.get())
    this.publishConversationConfig()
    if (previous.sessionId !== next.sessionId) this.clearAll('cancelled')
  }

  /** The exact session whose dialogue surface may own a one-shot approval. */
  public isSelectedSession(sessionId: string): boolean {
    return this.effectiveTarget(this.ctx.settings.get()).sessionId === sessionId
  }

  /** Starts the web-backed first turn only after the user clicked a clearly labelled local invitation. */
  public async startRandomChatTopic(requestId: number, topic: RandomChatTopic): Promise<void> {
    try {
      const target = this.randomChatTarget
      const settings = this.ctx.settings.get()
      if (target === undefined || !settings.randomChatEnabled || !settings.randomChatBrowseOnOpen) {
        this.send({ kind: 'input-status', requestId, status: 'rejected' })
        return
      }
      this.currentInputRequestId = requestId
      this.currentInputSessionId = target.sessionId
      this.clearAll('next-input')
      const agent = await this.findAgent(target.sessionId)
      if (agent === undefined) {
        this.send({ kind: 'input-status', requestId, status: 'session-unavailable' })
        return
      }
      const message = createUserMessage({
        content: [{ type: 'text', text: randomChatPrompt(topic) }],
        source: { kind: 'user' },
      })
      const request: Request = {
        requestId,
        sessionId: target.sessionId,
        origin: 'pet',
        previewEnabled: settings.previewEnabled,
        previewMaxChars: settings.previewMaxChars,
        preview: '',
      }
      this.requestsByMessageId.set(message.id, request)
      this.activeRequests.set(request.requestId, request)
      this.send({ kind: 'input-status', requestId, status: agent.status === 'running' ? 'queued' : 'sent' })
      await agent.followup(message)
    } catch {
      this.send({ kind: 'input-status', requestId, status: 'rejected' })
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
    const sessionId = this.effectiveTarget(settings).sessionId
    if (sessionId === null) {
      this.send({ kind: 'input-status', requestId: input.requestId, status: 'no-default-session' })
      return
    }
    this.currentInputSessionId = sessionId

    const agent = await this.findAgent(sessionId)
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
      origin: 'pet',
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

  private async findAgent(sessionId: string): Promise<import('./dsh-dialogue-types.js').DshAgent | undefined> {
    let agent = this.ctx.agents.get(sessionId)
    if (agent !== undefined) return agent
    try {
      const resumeOptions: { resumeSessionId: string, agentOptions?: { provider: string, model: string } } = { resumeSessionId: sessionId }
      const defaultModel = this.readDefaultModel()
      if (defaultModel !== undefined) resumeOptions.agentOptions = defaultModel
      agent = (await this.ctx.agents.resume(resumeOptions))?.agent
    } catch {
      agent = undefined
    }
    return agent
  }

  /** Aborts the live turn. The terminal reply-preview/status are driven by the resulting aborted turn/end. */
  public stop(requestId: number): void {
    const settings = this.ctx.settings.get()
    const sessionId = this.currentInputSessionId ?? this.effectiveTarget(settings).sessionId
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
      const request = messageId === undefined ? undefined : this.requestsByMessageId.get(messageId)
      if (request !== undefined && request.sessionId === sessionId) {
        this.queueTurnRequest(sessionId, request)
        return
      }
      if (!isUserMessage(event.data) || !this.isSelectedSession(sessionId)) return

      // DSH versions differ on whether the echoed session event carries the user-message
      // ID.  A pet input is the only unbound request in its session, so bind the next real
      // user turn even when that ID is absent or nested in an unsupported envelope.
      const fallback = this.unboundPetRequest(sessionId)
      if (fallback !== undefined) {
        this.queueTurnRequest(sessionId, fallback)
      } else {
        this.externalUserMessages.set(sessionId, (this.externalUserMessages.get(sessionId) ?? 0) + 1)
      }
      return
    }

    const turn = readTurn(event.data)
    if (turn === undefined) return
    const key = turnKey(sessionId, turn)
    if (event.type === 'turn/start') {
      const request = this.takePendingTurnRequest(sessionId)
        ?? this.unboundPetRequest(sessionId)
        ?? this.takeExternalTurnRequest(sessionId)
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
        if (reason === 'aborted') {
          this.send({ kind: 'input-status', requestId: request.requestId, status: 'stopped' })
        } else if (reason === 'interrupted') {
          this.send({ kind: 'input-status', requestId: request.requestId, status: 'interrupted' })
        } else if (reason === 'error' || reason === 'max-tokens' || reason === 'blocked') {
          this.send({ kind: 'input-status', requestId: request.requestId, status: 'failed' })
        } else {
          this.publishReply(sessionId, request.requestId, turn)
        }
        this.clearCurrentPetInput(request)
        return
      }
      // Preserve a final reply when an old DSH runtime omits both the echoed message ID and
      // a usable turn/start event. It cannot be streamed, but it must not leave the UI blank.
      if (this.currentInputRequestId !== undefined) {
        const currentRequest = this.activeRequests.get(this.currentInputRequestId)
        if (currentRequest === undefined || currentRequest.sessionId !== sessionId) return
        if (reason === 'aborted') {
          this.send({ kind: 'input-status', requestId: currentRequest.requestId, status: 'stopped' })
        } else if (reason === 'interrupted') {
          this.send({ kind: 'input-status', requestId: currentRequest.requestId, status: 'interrupted' })
        } else if (reason === 'error' || reason === 'max-tokens' || reason === 'blocked') {
          this.send({ kind: 'input-status', requestId: currentRequest.requestId, status: 'failed' })
        } else {
          this.publishReply(sessionId, currentRequest.requestId, turn)
        }
        this.removeRequest(currentRequest)
        this.clearCurrentPetInput(currentRequest)
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
    const sessionId = this.currentInputSessionId ?? this.effectiveTarget(settings).sessionId
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
    if (this.temporaryTarget?.sessionId === sessionId) this.temporaryTarget = undefined
    if (this.randomChatTarget?.sessionId === sessionId) this.randomChatTarget = undefined
    this.clearConfiguredSession(sessionId, settings)
  }

  public helperClosed(): void {
    this.randomChatTarget = undefined
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

  private effectiveTarget(settings: DialogueSettings): { sessionId: string | null, workspaceId: string | null } {
    if (this.randomChatTarget !== undefined) return this.randomChatTarget
    if (settings.defaultSessionId !== null) {
      return { sessionId: settings.defaultSessionId, workspaceId: settings.defaultWorkspaceId }
    }
    return this.temporaryTarget ?? { sessionId: null, workspaceId: null }
  }

  private clearForSession(sessionId: string, reason: 'session-unavailable'): void {
    this.externalUserMessages.delete(sessionId)
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
    this.externalUserMessages.clear()
  }

  private queueTurnRequest(sessionId: string, request: Request): void {
    for (const [messageId, mappedRequest] of this.requestsByMessageId) {
      if (mappedRequest === request) this.requestsByMessageId.delete(messageId)
    }
    const pending = this.pendingTurnRequests.get(sessionId) ?? []
    if (!pending.includes(request)) pending.push(request)
    this.pendingTurnRequests.set(sessionId, pending)
  }

  private takePendingTurnRequest(sessionId: string): Request | undefined {
    const pending = this.pendingTurnRequests.get(sessionId)
    const request = pending?.shift()
    if (pending !== undefined && pending.length === 0) this.pendingTurnRequests.delete(sessionId)
    return request
  }

  private unboundPetRequest(sessionId: string): Request | undefined {
    for (const request of this.requestsByMessageId.values()) {
      if (request.sessionId === sessionId && request.origin === 'pet') return request
    }
    return undefined
  }

  private takeExternalTurnRequest(sessionId: string): Request | undefined {
    const pending = this.externalUserMessages.get(sessionId) ?? 0
    if (pending > 0) {
      if (pending === 1) this.externalUserMessages.delete(sessionId)
      else this.externalUserMessages.set(sessionId, pending - 1)
    } else if (!this.isSelectedSession(sessionId)) {
      return undefined
    }

    const request: Request = {
      requestId: this.nextExternalRequestId--,
      sessionId,
      origin: 'external',
      previewEnabled: this.ctx.settings.get().previewEnabled,
      previewMaxChars: this.ctx.settings.get().previewMaxChars,
      preview: '',
    }
    this.activeRequests.set(request.requestId, request)
    return request
  }

  private clearCurrentPetInput(request: Request): void {
    if (request.origin !== 'pet' || this.currentInputRequestId !== request.requestId) return
    this.currentInputRequestId = undefined
    this.currentInputSessionId = undefined
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

function randomChatPrompt(topic: RandomChatTopic): string {
  if (topic === 'news') {
    return '请使用可用的网页检索工具查阅今天的热点新闻，选择不超过三项，标注来源名称和链接；若无法联网请明确说明，不要编造。'
  }
  if (topic === 'weather') {
    return '请先友好地请用户提供要查询天气的城市（可含国家或地区）。在用户给出城市前，不得猜测位置、读取定位或联网查询。用户提供城市后，使用可用的网页检索工具查阅该城市的当前天气和今日预报，标注来源名称和链接；若无法联网请明确说明，不要编造。'
  }
  return '请使用可用的网页检索工具找一件近期值得分享的科技或见闻，标注来源名称和链接；若无法联网请明确说明，不要编造。'
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
  const id = value?.id ?? readRecord(value?.message)?.id
  return typeof id === 'string' && id.length > 0 ? id : undefined
}

function isUserMessage(data: unknown): boolean {
  return readRecord(readRecord(data)?.source)?.kind === 'user'
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
