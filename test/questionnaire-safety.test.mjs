import assert from 'node:assert/strict'
import test from 'node:test'
import { QuestionnaireController } from '../lib/questionnaire-controller.js'
import { PROTOCOL_VERSION, encodeHostMessage, parseHelperMessage, parseHostMessage } from '../lib/protocol.js'
import { registerQuestionnaireAnswerer, routeHelperMessage } from '../lib/index.js'
const questions = [{ id: 'native-question', question: '选择', options: [{ label: 'A' }, { label: 'B' }] }]
const request = (extra = {}) => ({ agent: { session: { id: 's-1' } }, questions, ...extra })
const answer = (answers = [{ id: 'q1', selected: ['A'] }]) => ({ version: PROTOCOL_VERSION, kind: 'question-answer', requestId: 1, answers })
function setup(mode = 'pet', timeout = 600000) {
  const sent = []; let selected = true
  const controller = new QuestionnaireController(() => selected, () => true, m => sent.push(m), () => mode, timeout)
  return { controller, sent, deselect: () => { selected = false; controller.selectionChanged() } }
}
test('registers the native question event and routes local submit/cancel only to its controller', async () => {
  const received = []; let listener
  registerQuestionnaireAnswerer({ on(name, callback, prepend) { assert.equal(name, 'user-questions/request'); assert.equal(prepend, true); listener = callback } }, { request: (request, next) => next() })
  assert.equal(await listener({}, async () => 'web'), 'web')
  const controller = { answer: m => received.push(m.kind), cancel: id => received.push(id) }
  await routeHelperMessage(answer(), undefined, undefined, undefined, undefined, undefined, controller)
  await routeHelperMessage({ version: 18, kind: 'question-cancel', requestId: 1 }, undefined, undefined, undefined, undefined, undefined, controller)
  assert.deepEqual(received, ['question-answer', 1])
})
test('multi-select plus text is preserved and mode changes do not change an in-flight decision owner', async () => {
  let mode = 'pet'
  const controller = new QuestionnaireController(() => true, () => true, () => {}, () => mode)
  const result = controller.request(request({ questions: [{ ...questions[0], multiSelect: true }, { id: 'free', question: '补充' }] }), async () => null)
  mode = 'both'
  controller.answer(answer([{ id: 'q1', selected: ['A', 'B'], custom: '说明' }, { id: 'q2', selected: [], custom: '文字' }]))
  assert.deepEqual(await result, { answers: [{ id: 'native-question', selected: ['A', 'B'], custom: '说明' }, { id: 'free', selected: [], custom: '文字' }] })
})
test('verifies question membership, rejects partial answers and maps local IDs back exactly once', async () => {
  const { controller, sent } = setup()
  const result = controller.request(request(), async () => null)
  assert.equal(sent[0].questions[0].id, 'q1')
  for (const a of [[], [{ id: 'wrong', selected: ['A'] }], [{ id: 'q1', selected: ['C'] }], [{ id: 'q1', selected: ['A', 'B'] }], [{ id: 'q1', selected: [], custom: ' ' }]]) controller.answer(answer(a))
  assert.equal(sent.length, 1)
  controller.answer(answer()); controller.answer(answer())
  assert.deepEqual(await result, { answers: [{ id: 'native-question', selected: ['A'] }] })
  assert.equal(sent.length, 2)
})
test('unsupported questions delegate intact instead of truncating', async () => {
  const { controller, sent } = setup()
  for (const invalid of [null, [], Array(9).fill(questions[0]), [{ ...questions[0], question: 'x'.repeat(2001) }], [{ ...questions[0], detail: 'review' }], [{ ...questions[0], intent: { kind: 'plan-review' } }], [{ ...questions[0], options: [{ label: 'A', description: 'context' }] }], [questions[0], questions[0]]]) assert.equal(await controller.request(request({ questions: invalid }), async () => 'web'), 'web')
  assert.deepEqual(sent, [])
})
test('both mode leaves Web answerable and rejects desktop answers', async () => {
  const { controller, sent } = setup('both'); let finish
  const result = controller.request(request(), () => new Promise(resolve => { finish = resolve }))
  assert.equal(sent[0].answerable, false)
  controller.answer(answer()); assert.equal(sent.length, 1)
  finish({ answers: [{ id: 'native-question', selected: ['B'] }] })
  assert.deepEqual(await result, { answers: [{ id: 'native-question', selected: ['B'] }] })
  assert.equal(sent.at(-1).outcome, 'answered')
})
test('closing a mirror does not cancel Web', async () => {
  const { controller } = setup('both'); let finish
  const result = controller.request(request(), () => new Promise(resolve => { finish = resolve }))
  controller.helperUnavailable(); finish({ answers: [] })
  assert.deepEqual(await result, { answers: [] })
})
test('abort, close, selection change, timeout and cancel clear the pending request', async () => {
  for (const action of ['abort', 'close', 'select', 'timeout', 'cancel']) {
    const { controller, sent, deselect } = setup('pet', action === 'timeout' ? 10 : 600000)
    const abort = new AbortController()
    const result = controller.request(request({ signal: abort.signal }), async () => null)
    const rejection = assert.rejects(result, e => e.name === 'UserQuestionError' && ['ASK_ABORTED', 'ASK_CANCELLED', 'NO_PROVIDER'].includes(e.code))
    if (action === 'abort') abort.abort()
    if (action === 'close') controller.helperUnavailable()
    if (action === 'select') deselect()
    if (action === 'cancel') controller.cancel(1)
    if (action === 'timeout') await new Promise(resolve => setTimeout(resolve, 30))
    await rejection; controller.answer(answer()); assert.equal(sent.length, 2)
  }
})
test('pre-aborted and failed-send requests do not strand a pending promise', async () => {
  const { controller, sent } = setup(); const abort = new AbortController(); abort.abort()
  await assert.rejects(controller.request(request({ signal: abort.signal }), async () => null), { code: 'ASK_ABORTED' })
  assert.equal(sent.length, 0)
  const failing = new QuestionnaireController(() => true, () => true, () => { throw new Error('transport') })
  await assert.rejects(failing.request(request(), async () => null), { code: 'NO_PROVIDER' })
  await assert.rejects(failing.request(request(), async () => null), { code: 'NO_PROVIDER' })
})
test('v18 validates question messages deeply and rejects old protocol', () => {
  const message = { kind: 'question-request', requestId: 1, answerable: true, questions: [{ id: 'q1', question: 'Q', options: ['A'], multiSelect: false }] }
  assert.equal(PROTOCOL_VERSION, 18)
  assert.deepEqual(parseHostMessage(encodeHostMessage(message)), { version: 18, ...message })
  assert.deepEqual(parseHelperMessage(JSON.stringify(answer())), answer())
  assert.deepEqual(parseHelperMessage('{"version":18,"kind":"question-cancel","requestId":1}'), { version: 18, kind: 'question-cancel', requestId: 1 })
  for (const q of [{ ...message.questions[0], extra: true }, { ...message.questions[0], options: ['A', 'A'] }, { ...message.questions[0], question: '' }]) assert.throws(() => encodeHostMessage({ ...message, questions: [q] }))
  assert.throws(() => parseHelperMessage(JSON.stringify(answer([{ id: 'q1', selected: ['A'], extra: true }]))))
  assert.throws(() => parseHelperMessage(JSON.stringify({ ...answer(), version: 17 })))
})
