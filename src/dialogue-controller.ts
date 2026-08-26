import { createUserMessage } from '@deepseek-ai/dsh-llm'

import type { DialogueSettings } from './dialogue-settings.js'
import type { DshDialogueContext, DshSessionEvent } from './dsh-dialogue-types.js'
import type { HelperInputMessage, HostOutboundMessage } from './protocol.js'

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
        agent = (await this.ctx.agents.resume({ resumeSessionId: sessionId }))?.agent
      } catch {
        agent = undefined
      }
    }
    if (!this.isCurrentInput(input.requestId)) return
    if (agent === undefined) {
      this.send({ kind: 'input-status', requestId: input.requestId, status: 'session-unavailable' })
      return
    }

    const message = createUserMessage({
      content: [{ type: 'text', text: input.text }],
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
    if (request === undefined) return
    if (event.type === 'assistant/chunk') {
      const text = readTextDelta(event.data)
      if (text === undefined || !request.previewEnabled) return
      request.preview = retainTail(request.preview + text, request.previewMaxChars)
      if (request.preview.length > 0) {
        this.send({ kind: 'reply-preview', requestId: request.requestId, text: request.preview, completed: false })
      }
      return
    }

    if (event.type === 'turn/end') {
      if (request.previewEnabled && request.preview.length > 0) {
        this.send({ kind: 'reply-preview', requestId: request.requestId, text: request.preview, completed: true })
      }
      this.requestsByTurn.delete(key)
      this.removeRequest(request)
    }
  }

  public disablePreview(): void {
    this.clearAll('disabled')
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
