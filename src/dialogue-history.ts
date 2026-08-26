import { HISTORY_LIMIT, HISTORY_MESSAGE_MAX_CHARS, type HistoryMessage } from './protocol.js'

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
      const text = readTextBlocks(data?.content)
      if (text === '') continue
      messages.push({ role: 'user', text: retainTail(text, HISTORY_MESSAGE_MAX_CHARS) })
    } else if (type === 'assistant/message') {
      const data = readRecord(event.data)
      const message = readRecord(data?.message)
      const text = readTextBlocks(message?.content)
      if (text === '') continue
      messages.push({ role: 'assistant', text: retainTail(text, HISTORY_MESSAGE_MAX_CHARS) })
    }
  }
  return messages.slice(-HISTORY_LIMIT)
}

function readTextBlocks(content: unknown): string {
  if (!Array.isArray(content)) return ''
  let text = ''
  for (const block of content) {
    const record = readRecord(block)
    if (record?.type !== 'text' || typeof record.text !== 'string') continue
    text += record.text
  }
  return text
}

function readRecord(value: unknown): Record<string, unknown> | undefined {
  return value !== null && typeof value === 'object' && !Array.isArray(value)
    ? value as Record<string, unknown>
    : undefined
}

function retainTail(value: string, maxChars: number): string {
  return value.length <= maxChars ? value : value.slice(-maxChars)
}
