/** Transient question UI data; never persisted or logged. */
export type QuestionView = { id: string, question: string, options: readonly string[], multiSelect: boolean }
export type QuestionAnswer = { id: string, selected: readonly string[], custom?: string }

function record(value: unknown): value is Record<string, unknown> {
  return value !== null && typeof value === 'object' && !Array.isArray(value)
}
function exact(value: Record<string, unknown>, required: string[], optional: string[] = []): boolean {
  return required.every(k => Object.hasOwn(value, k)) && Object.keys(value).every(k => required.includes(k) || optional.includes(k))
}
export function boundedQuestionText(value: unknown, limit: number): value is string {
  return typeof value === 'string' && value.trim().length > 0 && value.length <= limit && !/[\u0000-\u0008\u000b\u000c\u000e-\u001f\u007f]/u.test(value)
}
function strings(value: unknown): value is string[] {
  return Array.isArray(value) && value.length <= 8 && value.every(v => boundedQuestionText(v, 200)) && new Set(value).size === value.length
}
export function validateQuestions(value: unknown): QuestionView[] {
  if (!Array.isArray(value) || value.length < 1 || value.length > 8) throw new Error('invalid questions')
  const seen = new Set<string>()
  return value.map(q => {
    if (!record(q) || !exact(q, ['id', 'question', 'options', 'multiSelect']) || !boundedQuestionText(q.id, 100)
      || !boundedQuestionText(q.question, 2000) || !strings(q.options) || typeof q.multiSelect !== 'boolean' || seen.has(q.id)) throw new Error('invalid question')
    seen.add(q.id)
    return { id: q.id, question: q.question, options: [...q.options], multiSelect: q.multiSelect }
  })
}
export function validateQuestionAnswers(value: unknown): QuestionAnswer[] {
  if (!Array.isArray(value) || value.length < 1 || value.length > 8) throw new Error('invalid answers')
  const seen = new Set<string>()
  return value.map(a => {
    if (!record(a) || !exact(a, ['id', 'selected'], ['custom']) || !boundedQuestionText(a.id, 100) || !strings(a.selected)
      || (Object.hasOwn(a, 'custom') && !boundedQuestionText(a.custom, 1000)) || seen.has(a.id)
      || (a.selected.length === 0 && a.custom === undefined)) throw new Error('invalid answer')
    seen.add(a.id)
    return { id: a.id, selected: [...a.selected], ...(a.custom === undefined ? {} : { custom: a.custom as string }) }
  })
}
export function answersMatch(questions: readonly QuestionView[], answers: readonly QuestionAnswer[]): boolean {
  return answers.length === questions.length && questions.every(q => {
    const a = answers.find(a => a.id === q.id)
    return a !== undefined && a.selected.every(s => q.options.includes(s)) && (q.multiSelect || a.selected.length + (a.custom === undefined ? 0 : 1) === 1)
  })
}
