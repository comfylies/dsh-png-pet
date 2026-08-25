import { spawn, type ChildProcessWithoutNullStreams } from 'node:child_process'
import { fileURLToPath } from 'node:url'
import readline from 'node:readline'

import { encodeHostMessage, parseHelperMessage, type HostOutboundMessage } from './protocol.js'

export type HelperProcessOptions = {
  command?: string
  args?: string[]
  readyTimeoutMs?: number
  shutdownTimeoutMs?: number
  onSend?: (line: string) => void
}

const defaultCommand = fileURLToPath(
  new URL('../runtime/bin/win32-x64/pet-helper.exe', import.meta.url),
)

export class HelperProcess {
  private readonly options: Required<HelperProcessOptions>
  private child?: ChildProcessWithoutNullStreams
  private readyPromise?: Promise<void>
  private stopPromise?: Promise<void>
  private exitPromise?: Promise<number | null>
  private closed = false

  public isReady = false
  public exitCode: number | null | undefined

  public constructor(options: HelperProcessOptions = {}) {
    this.options = {
      command: options.command ?? defaultCommand,
      args: options.args ?? [],
      readyTimeoutMs: options.readyTimeoutMs ?? 5_000,
      shutdownTimeoutMs: options.shutdownTimeoutMs ?? 2_000,
      onSend: options.onSend ?? (() => {}),
    }
  }

  public start(): Promise<void> {
    if (this.readyPromise) return this.readyPromise

    this.readyPromise = new Promise<void>((resolve, reject) => {
      const child = spawn(this.options.command, this.options.args, {
        stdio: ['pipe', 'pipe', 'pipe'],
        windowsHide: true,
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
        try {
          const message = parseHelperMessage(line)
          if (message.kind === 'ready' && !this.isReady) {
            this.isReady = true
            finishStart()
          }
          if (message.kind === 'closed') this.closed = true
        } catch {
          // Ignore malformed helper output: only validated protocol messages affect lifecycle.
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
