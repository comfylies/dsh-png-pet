import { HISTORY_BLOCK_LIMIT, HISTORY_LIMIT, HISTORY_MESSAGE_MAX_CHARS, type HistoryBlock, type HistoryMessage } from './protocol.js'

/**
 * Reduces the session event log into a bounded dialogue transcript whose
 * messages carry blocks (text plus image placeholders). Only real user input
 * (`source.kind === 'user'`) is echoed; injected context stays hidden. An
 * assistant message with no visible text is preserved as a placeholder
 * (tool-only turns read "调用了 …"; otherwise "(无内容)") so tool-only turns
 * never vanish from the transcript.
 */
export function extractDialogueHistory(events: readonly unknown[]): HistoryMessage[] {
  const messages: HistoryMessage[] = []
  for (const raw of events) {
    const event = readRecord(raw)
    if (event === undefined) continue
    const type = event.type
    if (typeof type !== 'string') continue

    if (type === 'user/message') {
      const data = readRecord(event.data)
      const source = readRecord(data?.source)
      if (source?.kind !== 'user') continue
      const blocks = collectBlocks(data?.content)
      if (blocks.length === 0) continue
      messages.push({ role: 'user', blocks })
    } else if (type === 'assistant/message') {
      const data = readRecord(event.data)
      const message = readRecord(data?.message)
      const blocks = collectBlocks(message?.content)
      messages.push({
        role: 'assistant',
        blocks: blocks.length > 0 ? blocks : [{ type: 'text', text: toolPlaceholder(message?.content) }],
      })
    }
  }
  return messages.slice(-HISTORY_LIMIT)
}

/** The latest assistant/message content within one exact turn, or undefined when the turn streamed none. */
export function turnAssistantText(events: readonly unknown[], turn: number): string | undefined {
  for (let index = events.length - 1; index >= 0; index--) {
    const event = readRecord(events[index])
    if (event === undefined || event.type !== 'assistant/message') continue
    if (readTurn(event.data) !== turn) continue
    const data = readRecord(event.data)
    const message = readRecord(data?.message)
    const blocks = collectBlocks(message?.content)
    if (blocks.length > 0) return textOfBlocks(blocks)
    return toolPlaceholder(message?.content)
  }
  return undefined
}

function textOfBlocks(blocks: readonly HistoryBlock[]): string {
  let text = ''
  for (const block of blocks) {
    if (block.type === 'text') text += block.text
  }
  return text
}

/** Images first (content order), then one merged tail-retained text block. */
function collectBlocks(content: unknown): HistoryBlock[] {
  if (!Array.isArray(content)) return []
  const blocks: HistoryBlock[] = []
  let text = ''
  for (const block of content) {
    const record = readRecord(block)
    if (record === undefined) continue
    if (record.type === 'text' && typeof record.text === 'string' && record.text.length > 0) {
      text += record.text
      continue
    }
    if (record.type !== 'image' || blocks.length >= HISTORY_BLOCK_LIMIT) continue
    const attachment = readRecord(record.attachment)
    if (attachment === undefined) continue
    const width = readDimension(attachment.width)
    const height = readDimension(attachment.height)
    if (width === undefined || height === undefined) continue
    const name = typeof attachment.name === 'string' && attachment.name.length > 0 ? attachment.name.slice(0, 200) : ''
    blocks.push({ type: 'image', name, width, height })
  }
  if (text.length > 0 && blocks.length < HISTORY_BLOCK_LIMIT) {
    blocks.push({ type: 'text', text: retainTail(text, HISTORY_MESSAGE_MAX_CHARS) })
  }
  return blocks
}

function toolPlaceholder(content: unknown): string {
  if (!Array.isArray(content)) return '（无内容）'
  for (const block of content) {
    const record = readRecord(block)
    if (record?.type === 'tool-call' && typeof record.name === 'string' && record.name.length > 0) {
      return `调用了 ${retainTail(record.name, 40)}`
    }
  }
  return '（无内容）'
}

function readDimension(value: unknown): number | undefined {
  return typeof value === 'number' && Number.isSafeInteger(value) && value >= 1 && value <= 100000
    ? value
    : undefined
}

function readTurn(data: unknown): number | undefined {
  const turn = readRecord(data)?.turn
  return typeof turn === 'number' && Number.isSafeInteger(turn) && turn >= 0 ? turn : undefined
}

function readRecord(value: unknown): Record<string, unknown> | undefined {
  return value !== null && typeof value === 'object' && !Array.isArray(value)
    ? value as Record<string, unknown>
    : undefined
}

function retainTail(value: string, maxChars: number): string {
  return value.length <= maxChars ? value : value.slice(-maxChars)
}
