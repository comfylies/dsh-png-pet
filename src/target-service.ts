import { randomUUID } from 'node:crypto'

import type { DshSessionSummary, DshWorkspaceView } from './target-selection.js'

/**
 * The slice of `ctx.apiProxy` this feature uses, in local structural types so
 * the plugin does not depend on the dsh-host-apiproxy package at build time.
 * Method contracts mirror the host gateway: unary methods take `{rpcId,
 * payload}` and resolve to `{rpcId, result}`; business failures never throw.
 */
export type TargetApi = {
  workspace: {
    list(request: { rpcId: string, payload: {} }): Promise<RpcEnvelope<{ items: DshWorkspaceView[], archivedSessionIds: string[] }>>
    create(request: { rpcId: string, payload: { path: string } }): Promise<RpcEnvelope<{ workspace: DshWorkspaceView, created: boolean }>>
  }
  sessions: {
    list(request: { rpcId: string, payload: {} }): Promise<RpcEnvelope<{ items: DshSessionSummary[] }>>
    create(request: { rpcId: string, payload: { workspaceId?: string, cwd?: string } }): Promise<RpcEnvelope<{ sessionId: string }>>
  }
}

type RpcEnvelope<T> = {
  result: { ok: true, value: T } | { ok: false, error: { message: string } }
}

/** Failed host calls fold into a typed throw; the message stays diagnostic-only. */
export class TargetApiError extends Error {}

function nextRpcId(): string {
  return randomUUID()
}

export async function listWorkspaces(api: TargetApi): Promise<{ items: DshWorkspaceView[], archivedSessionIds: string[] }> {
  return unwrap(await api.workspace.list({ rpcId: nextRpcId(), payload: {} }), 'workspace.list')
}

export async function listSessions(api: TargetApi): Promise<DshSessionSummary[]> {
  const response = unwrap(await api.sessions.list({ rpcId: nextRpcId(), payload: {} }), 'session.list')
  return response.items
}

/**
 * Registers an existing directory as a workspace; `created: false` means the
 * directory was already registered and the returned workspace is reused.
 */
export async function createWorkspace(api: TargetApi, path: string): Promise<{ workspace: DshWorkspaceView, created: boolean }> {
  return unwrap(await api.workspace.create({ rpcId: nextRpcId(), payload: { path } }), 'workspace.create')
}

/** Creates a real session: in a workspace, or with the host cwd when ungrouped. */
export async function createSession(api: TargetApi, workspaceId?: string): Promise<{ sessionId: string }> {
  const payload = workspaceId === undefined ? {} : { workspaceId }
  return unwrap(await api.sessions.create({ rpcId: nextRpcId(), payload }), 'session.create')
}

function unwrap<T>(envelope: RpcEnvelope<T>, method: string): T {
  if (!envelope.result.ok) {
    throw new TargetApiError(`${method} failed: ${envelope.result.error.message}`)
  }
  return envelope.result.value
}
