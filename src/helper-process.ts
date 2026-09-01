import { spawn, type ChildProcessWithoutNullStreams } from 'node:child_process'
import { fileURLToPath } from 'node:url'
import readline from 'node:readline'

import { encodeHostMessage, parseHelperMessage, type HelperHistoryRequest, type HelperInputMessage, type HelperMessage, type HelperStopMessage, type HelperTargetAnswerMessage, type HelperTargetOpenMessage, type HostOutboundMessage } from './protocol.js'

export type HelperProcessMessage = HelperInputMessage | HelperStopMessage | HelperHistoryRequest | HelperTargetOpenMessage | HelperTargetAnswerMessage | (Pick<HelperMessage, 'version'> & { kind: 'closed' })

export type HelperProcessOptions = {
  command?: string
  args?: string[]
  readyTimeoutMs?: number
  shutdownTimeoutMs?: number
  onSend?: (line: string) => void
  onMessage?: (message: HelperProcessMessage) => void
}

const defaultCommand = fileURLToPath(
  new URL('../runtime/bin/win32-x64/pet-helper.exe', import.meta.url),
)

// A cold .NET single-file WPF launch can include native extraction and security
// scanning before it can write the ready handshake.
export const defaultHelperReadyTimeoutMs = 15_000

export function withRequiredWindowsEnvironment(environment: NodeJS.ProcessEnv): NodeJS.ProcessEnv {
  if (environment.WINDIR !== undefined || !environment.SystemRoot) return { ...environment }

  return { ...environment, WINDIR: environment.SystemRoot }
}

export class HelperProcess {
  private readonly options: Required<HelperProcessOptions>
  private child?: ChildProcessWithoutNullStreams
  private readyPromise?: Promise<void>
  private stopPromise?: Promise<void>
  private exitPromise?: Promise<number | null>
  private closed = false
  private lastInputRequestId = 0
  private lastHistoryRequestId = 0

  public isReady = false
  public exitCode: number | null | undefined

  public constructor(options: HelperProcessOptions = {}) {
    this.options = {
      command: options.command ?? defaultCommand,
      args: options.args ?? [],
      readyTimeoutMs: options.readyTimeoutMs ?? defaultHelperReadyTimeoutMs,
      shutdownTimeoutMs: options.shutdownTimeoutMs ?? 2_000,
      onSend: options.onSend ?? (() => {}),
      onMessage: options.onMessage ?? (() => {}),
    }
  }

  public start(): Promise<void> {
    if (this.readyPromise) return this.readyPromise

    this.readyPromise = new Promise<void>((resolve, reject) => {
      const child = spawn(this.options.command, this.options.args, {
        stdio: ['pipe', 'pipe', 'pipe'],
        windowsHide: true,
        env: withRequiredWindowsEnvironment(process.env),
      })
      this.child = child

      const readyTimer = setTimeout(() => {
        reject(new Error('helper did not complete its ready handshake'))
        this.forceStop()
      }, this.options.readyTimeoutMs)
      readyTimer.unref()

      const finishStart = (error?: Error) => {
        clearTimeout(readyTimer)
        if (error) reject(error)
        else resolve()
      }

      this.exitPromise = new Promise<number | null>((resolveExit) => {
        child.once('exit', (code) => {
          this.exitCode = code
          this.child = undefined
          resolveExit(code)
          if (!this.isReady) finishStart(new Error('helper exited before ready handshake'))
        })
      })

      child.once('error', () => finishStart(new Error('helper process could not start')))

      const output = readline.createInterface({ input: child.stdout })
      output.on('line', (line) => {
        let message: HelperMessage
        try {
          message = parseHelperMessage(line)
        } catch {
          // Ignore malformed helper output: only validated protocol messages affect lifecycle.
          return
        }

        if (message.kind === 'ready' && !this.isReady) {
          this.isReady = true
          finishStart()
        }
        if (message.kind === 'closed') {
          this.closed = true
          try {
            this.options.onMessage({ version: message.version, kind: 'closed' })
          } catch {
            // A lifecycle consumer must not break a validated helper shutdown.
          }
          return
        }
        if (message.kind === 'input' && message.requestId > this.lastInputRequestId) {
          try {
            this.options.onMessage(message)
            this.lastInputRequestId = message.requestId
          } catch {
            // Ignore consumer failures so a validated input can be retried.
          }
        }
        if (message.kind === 'stop') {
          try {
            this.options.onMessage(message)
          } catch {
            // Ignore consumer failures so a validated stop can be retried.
          }
        }
        if (message.kind === 'request-history' && message.requestId > this.lastHistoryRequestId) {
          try {
            this.options.onMessage(message)
            this.lastHistoryRequestId = message.requestId
          } catch {
            // Ignore consumer failures so a validated history request can be retried.
          }
        }
        if (message.kind === 'target-open' || message.kind === 'target-answer') {
          try {
            this.options.onMessage(message)
          } catch {
            // Ignore consumer failures so a validated target message can be retried.
          }
        }
      })
    })

    return this.readyPromise
  }

  public send(message: HostOutboundMessage): void {
    if (!this.child || this.child.stdin.destroyed) return
    const line = encodeHostMessage(message)
    this.options.onSend(line)
    this.child.stdin.write(line)
  }

  public stop(): Promise<void> {
    if (this.stopPromise) return this.stopPromise

    this.stopPromise = (async () => {
      const child = this.child
      const exited = this.exitPromise
      if (!child || !exited) return

      this.send({ kind: 'shutdown' })
      await Promise.race([
        exited,
        new Promise<void>((resolve) => {
          const timer = setTimeout(resolve, this.options.shutdownTimeoutMs)
          timer.unref()
        }),
      ])

      if (this.child && !this.closed) this.forceStop()
      if (this.exitPromise) await this.exitPromise
    })()

    return this.stopPromise
  }

  private forceStop(): void {
    if (this.child && !this.child.killed) this.child.kill()
  }
}
