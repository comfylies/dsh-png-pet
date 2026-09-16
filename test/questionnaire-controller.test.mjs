import assert from 'node:assert/strict'
import test from 'node:test'
import { QuestionnaireController } from '../lib/questionnaire-controller.js'

test('projects a selected question batch and resolves it once from the pet', async () => {
  const sent = []
  const controller = new QuestionnaireController((id) => id === 's-1', () => true, (message) => sent.push(message))
  let delegated = 0
  const result = controller.request({ agent: { session: { id: 's-1' } }, answerable: true, questions: [{ id: 'q1', question: '选择', options: [{ label: 'A' }] }] }, async () => { delegated++; return { answers: [] } })
  assert.deepEqual(sent, [{ kind: 'question-request', requestId: 1, answerable: true, questions: [{ id: 'q1', question: '选择', options: ['A'], multiSelect: false }] }])
  controller.answer({ version: 18, kind: 'question-answer', requestId: 1, answers: [{ id: 'q1', selected: ['A'] }] })
  assert.deepEqual(await result, { answers: [{ id: 'q1', selected: ['A'] }] })
  assert.equal(delegated, 0)
  assert.deepEqual(sent.at(-1), { kind: 'question-resolved', requestId: 1, outcome: 'answered' })
})
