# DSH State Bridge Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Render the DSH Session/Agent lifecycle as safe, fixed-label desktop-pet status bubbles over the existing stdin/stdout JSON Lines channel.

**Architecture:** A TypeScript adapter converts only whitelisted DSH event metadata into `SessionFact` values, which a pure `CompanionReducer` aggregates and prioritizes. The plugin sends the reducer's fixed presentation through protocol v2; the WPF Helper validates it again, applies safe configuration, and binds it to a bubble with no free-form DSH content.

**Tech Stack:** Node.js 22 / TypeScript 5.9 / node:test; .NET 10 / WPF / xUnit; stdin/stdout JSON Lines.

---

## File structure

- `src/protocol.ts` — protocol-v2 types, encoders and strict Host/Helper validators.
- `src/companion-reducer.ts` — pure session-state aggregation, priority and terminal expiry tokens.
- `src/dsh-event-adapter.ts` — DSH event metadata allowlist; never exposes raw event payloads.
- `src/companion-bridge.ts` — sends changed presentations and schedules guarded terminal resets.
- `src/helper-process.ts` — sends structured Host messages after a v2 handshake.
- `src/index.ts` — subscribes to the two verified DSH events and contains observer errors.
- `pet-helper/ProtocolMessage.cs`, `ProtocolReader.cs` — v2 message models and strict parser.
- `pet-helper/PetDisplayState.cs` — fixed-label display/config validation usable without WPF.
- `pet-helper/MainWindow.xaml`, `MainWindow.xaml.cs`, `App.xaml.cs` — bubble rendering and UI-thread protocol application.
- `test/*.test.mjs`, `pet-helper.Tests/*.cs` — behavior and regression coverage.

### Task 1: Define and test protocol v2

**Files:**
- Modify: `test/protocol.test.mjs`
- Modify: `src/protocol.ts`

- [ ] **Step 1: Write failing protocol tests for a structured state message and hostile payloads.**

```js
import { encodeHostMessage, parseHelperMessage, parseHostMessage } from '../lib/protocol.js'

test('encodes only a fixed working state presentation', () => {
  assert.equal(
    encodeHostMessage({ kind: 'state', state: 'working', label: '工作中…', sequence: 42 }),
    '{"version":2,"kind":"state","state":"working","label":"工作中…","sequence":42}\n',
  )
})

test('rejects a state message with a free-form label', () => {
  assert.throws(
    () => parseHostMessage('{"version":2,"kind":"state","state":"working","label":"C:\\\\secret","sequence":1}'),
    /label/,
  )
})

test('rejects an old Helper handshake', () => {
  assert.throws(() => parseHelperMessage('{"version":1,"kind":"ready"}'), /version/)
})
```

- [ ] **Step 2: Run the focused tests and verify the expected missing-export failure.**

Run: `npm run build; node --test test/protocol.test.mjs`

Expected: FAIL because `parseHostMessage` is not exported and v1 is still encoded.

- [ ] **Step 3: Replace the v1 protocol boundary with exhaustive v2 types and validators.**

```ts
export const PROTOCOL_VERSION = 2 as const
export const displayLabels = {
  idle: '', thinking: '思考中…', working: '工作中…', waiting: '等待你的操作',
  success: '已完成', error: '发生错误', disconnected: '未连接',
} as const
export type CompanionState = keyof typeof displayLabels

export type HostMessage =
  | { version: 2; kind: 'hello' | 'shutdown' }
  | { version: 2; kind: 'config'; scale: 0.75 | 1 | 1.25 | 1.5; reducedMotion: boolean }
  | { version: 2; kind: 'state'; state: CompanionState; label: string; sequence: number }

export type HostOutboundMessage =
  | { kind: 'hello' | 'shutdown' }
  | { kind: 'config'; scale: 0.75 | 1 | 1.25 | 1.5; reducedMotion: boolean }
  | { kind: 'state'; state: CompanionState; label: string; sequence: number }

export function encodeHostMessage(message: HostOutboundMessage): string {
  return `${JSON.stringify({ version: PROTOCOL_VERSION, ...validateHostMessage(message) })}\n`
}
```

Implement `parseHostMessage(line)` with `JSON.parse`, an object-only guard, an exact-key guard per `kind`, maximum input length of 512 characters, `Number.isSafeInteger(sequence) && sequence >= 0`, allowed scales, boolean `reducedMotion`, and `label === displayLabels[state]`. Implement the equivalent strict v2-only `parseHelperMessage` for `ready` and `closed`.

- [ ] **Step 4: Run the focused protocol tests and verify they pass.**

Run: `npm run build; node --test test/protocol.test.mjs`

Expected: PASS.

- [ ] **Step 5: Commit the protocol boundary.**

```bash
git add src/protocol.ts test/protocol.test.mjs
git commit -m "feat: define state bridge protocol v2"
```

### Task 2: Build the pure reducer from desensitized facts

**Files:**
- Create: `src/companion-reducer.ts`
- Create: `test/companion-reducer.test.mjs`
- Modify: `package.json`

- [ ] **Step 1: Write failing tests for transitions, priority, filtering and stale facts.**

```js
import { CompanionReducer } from '../lib/companion-reducer.js'

test('waiting takes priority over a working top-level session', () => {
  const reducer = new CompanionReducer()
  reducer.apply({ sessionId: 'first', seq: 1, isSubagent: false, kind: 'working' })
  const result = reducer.apply({ sessionId: 'second', seq: 1, isSubagent: false, kind: 'waiting' })
  assert.deepEqual(result, { state: 'waiting', sequence: 1, terminal: false })
})

test('ignores duplicate facts and subagents by default', () => {
  const reducer = new CompanionReducer()
  reducer.apply({ sessionId: 'root', seq: 4, isSubagent: false, kind: 'thinking' })
  reducer.apply({ sessionId: 'root', seq: 4, isSubagent: false, kind: 'error' })
  const result = reducer.apply({ sessionId: 'child', seq: 9, isSubagent: true, kind: 'waiting' })
  assert.equal(result.state, 'thinking')
})

test('removes a disposed session without accepting a later fact', () => {
  const reducer = new CompanionReducer({ includeSubagents: true })
  reducer.apply({ sessionId: 's', seq: 1, isSubagent: false, kind: 'working' })
  reducer.dispose('s')
  assert.equal(reducer.apply({ sessionId: 's', seq: 2, isSubagent: false, kind: 'error' }).state, 'idle')
})
```

Add individual cases for all six reducible states, equal-priority sequence tie breaking, `includeSubagents: true`, `success` and `error` terminal flags, and every invalid fact (empty id, negative/non-integer sequence, unknown kind).

- [ ] **Step 2: Run the new test file and verify it fails because the module is absent.**

Run: `npm run build; node --test test/companion-reducer.test.mjs`

Expected: FAIL with `ERR_MODULE_NOT_FOUND` for `companion-reducer.js`.

- [ ] **Step 3: Implement the smallest stateful reducer.**

```ts
export type ReducibleState = Exclude<CompanionState, 'disconnected'>
export type SessionFact = { sessionId: string; seq: number; isSubagent: boolean; kind: ReducibleState }
export type Presentation = { state: ReducibleState; sequence: number; terminal: boolean }

const priority: Record<ReducibleState, number> = {
  idle: 0, success: 1, thinking: 2, working: 3, error: 4, waiting: 5,
}

const reducibleStates = new Set<ReducibleState>(['idle', 'thinking', 'working', 'waiting', 'success', 'error'])
function isFact(value: SessionFact): boolean {
  return typeof value.sessionId === 'string' && value.sessionId.length > 0
    && Number.isSafeInteger(value.seq) && value.seq >= 0
    && typeof value.isSubagent === 'boolean' && reducibleStates.has(value.kind)
}

export class CompanionReducer {
  private readonly sessions = new Map<string, Pick<SessionFact, 'seq' | 'kind'>>()
  private readonly retired = new Set<string>()
  public constructor(private readonly options: { includeSubagents?: boolean } = {}) {}
  public apply(fact: SessionFact): Presentation {
    if (!isFact(fact) || this.retired.has(fact.sessionId)) return this.current()
    if (fact.isSubagent && !this.options.includeSubagents) return this.current()
    const previous = this.sessions.get(fact.sessionId)
    if (previous !== undefined && fact.seq <= previous.seq) return this.current()
    this.sessions.set(fact.sessionId, { seq: fact.seq, kind: fact.kind })
    return this.current()
  }
  public dispose(sessionId: string): Presentation {
    if (sessionId.length === 0) return this.current()
    this.sessions.delete(sessionId)
    this.retired.add(sessionId)
    return this.current()
  }
  public disposeTerminal(sequence: number): Presentation {
    for (const [sessionId, value] of this.sessions) {
      if (value.seq === sequence && (value.kind === 'success' || value.kind === 'error')) this.sessions.delete(sessionId)
    }
    return this.current()
  }
  public current(): Presentation {
    const winner = [...this.sessions.values()].sort((a, b) => priority[b.kind] - priority[a.kind] || b.seq - a.seq)[0]
    return winner === undefined
      ? { state: 'idle', sequence: 0, terminal: false }
      : { state: winner.kind, sequence: winner.seq, terminal: winner.kind === 'success' || winner.kind === 'error' }
  }
}
```

Store only `{ seq, state }`, never an original DSH event. Select the highest priority record, breaking a tie with larger `seq`; return `{ state: 'idle', sequence: 0, terminal: false }` with no candidates. Mark only `success` and `error` terminal.

Update the `test` script so it includes the new files explicitly:

```json
"test": "npm run build && node --test test/helper-process.test.mjs test/package-layout.test.mjs test/protocol.test.mjs test/companion-reducer.test.mjs"
```

- [ ] **Step 4: Run reducer tests and then the existing TypeScript suite.**

Run: `npm run build; node --test test/companion-reducer.test.mjs; npm test`

Expected: all tests PASS.

- [ ] **Step 5: Commit the reducer.**

```bash
git add src/companion-reducer.ts test/companion-reducer.test.mjs package.json
git commit -m "feat: reduce safe companion session facts"
```

### Task 3: Adapt only verified DSH event metadata

**Files:**
- Create: `src/dsh-event-adapter.ts`
- Create: `test/dsh-event-adapter.test.mjs`
- Modify: `package.json`

- [ ] **Step 1: Write failing adapter tests using literal, non-sensitive fixture objects.**

```js
import { adaptSessionEvent } from '../lib/dsh-event-adapter.js'

test('maps a code-dispatch start to working without retaining arguments', () => {
  const fact = adaptSessionEvent(
    { id: 'root', header: {} },
    { type: 'tool/code-dispatch-start', seq: 7, data: { arguments: { ignored: true } } },
  )
  assert.deepEqual(fact, { sessionId: 'root', seq: 7, isSubagent: false, kind: 'working' })
  assert.equal(JSON.stringify(fact).includes('ignored'), false)
})

test('maps completed and failed turn endings', () => {
  assert.equal(adaptSessionEvent({ id: 'a', header: {} }, { type: 'turn/end', seq: 3, data: { reason: { kind: 'completed' } } }).kind, 'success')
  assert.equal(adaptSessionEvent({ id: 'a', header: {} }, { type: 'turn/end', seq: 4, data: { reason: { kind: 'error' } } }).kind, 'error')
})
```

Test every table row in the design, a malformed session id/sequence, a foreign event type and missing `reason.kind`; all return `undefined` without throwing.

- [ ] **Step 2: Run the adapter test and verify it fails because the module is absent.**

Run: `npm run build; node --test test/dsh-event-adapter.test.mjs`

Expected: FAIL with `ERR_MODULE_NOT_FOUND`.

- [ ] **Step 3: Implement a whitelist adapter with `unknown` inputs.**

```ts
export function adaptSessionEvent(session: unknown, event: unknown): SessionFact | undefined {
  const sessionId = readNonEmptyString(session, 'id')
  const seq = readNonNegativeSafeInteger(event, 'seq')
  const type = readString(event, 'type')
  if (sessionId === undefined || seq === undefined || type === undefined) return undefined
  const isSubagent = readNonNegativeSafeInteger(readObject(session, 'header'), 'delegationDepth') !== undefined
    && readNonNegativeSafeInteger(readObject(session, 'header'), 'delegationDepth') > 0
  const kind = mapEventType(type, event)
  return kind === undefined ? undefined : { sessionId, seq, isSubagent, kind }
}
```

`mapEventType` may inspect `event.data.reason.kind` only when `type === 'turn/end'`. Do not stringify, clone, log, store, or pass any `event.data` value elsewhere.

Extend the `test` script with `test/dsh-event-adapter.test.mjs` after that file exists.

- [ ] **Step 4: Run the adapter and full Node test suite.**

Run: `npm run build; node --test test/dsh-event-adapter.test.mjs; npm test`

Expected: all tests PASS.

- [ ] **Step 5: Commit the verified event adapter.**

```bash
git add src/dsh-event-adapter.ts test/dsh-event-adapter.test.mjs package.json
git commit -m "feat: adapt verified DSH state events"
```

### Task 4: Deliver state updates through the Helper lifecycle

**Files:**
- Create: `src/companion-bridge.ts`
- Modify: `src/helper-process.ts`
- Modify: `src/index.ts`
- Modify: `test/helper-process.test.mjs`
- Create: `test/companion-bridge.test.mjs`
- Modify: `package.json`

- [ ] **Step 1: Write failing lifecycle and bridge tests.**

```js
test('sends default v2 config and idle state after ready', async () => {
  const lines = []
  const helper = new HelperProcess({ command: process.execPath, args: [fixture], onSend: (line) => lines.push(line) })
  await helper.start()
  helper.send({ kind: 'config', scale: 1, reducedMotion: false })
  helper.send({ kind: 'state', state: 'idle', label: '', sequence: 0 })
  assert.deepEqual(lines.slice(-2), [
    '{"version":2,"kind":"config","scale":1,"reducedMotion":false}\n',
    '{"version":2,"kind":"state","state":"idle","label":"","sequence":0}\n',
  ])
})

test('does not send a terminal reset after a newer state', () => {
  const bridge = new CompanionBridge(send, { setTimeout: captureTimer, clearTimeout })
  bridge.apply({ sessionId: 's', seq: 1, isSubagent: false, kind: 'success' })
  bridge.apply({ sessionId: 's', seq: 2, isSubagent: false, kind: 'working' })
  timers[0].run()
  assert.deepEqual(sent.at(-1), { kind: 'state', state: 'working', label: '工作中…', sequence: 2 })
})
```

Update `test/fixtures/fake-helper.mjs` to emit v2 `ready`, parse v2 structured messages, and answer v2 `shutdown` with `closed`. Add a fake plugin context whose `on` registrations are invoked directly; assert malformed events do not throw and `session/disposed` removes the displayed session.

- [ ] **Step 2: Run focused tests and verify expected API failures.**

Run: `npm run build; node --test test/helper-process.test.mjs test/companion-bridge.test.mjs`

Expected: FAIL because `send()` still accepts a string and `CompanionBridge` does not exist.

- [ ] **Step 3: Change `HelperProcess` to send typed messages and add the guarded bridge.**

```ts
public send(message: HostOutboundMessage): void {
  if (!this.child || this.child.stdin.destroyed) return
  this.child.stdin.write(encodeHostMessage(message))
}

export class CompanionBridge {
  public apply(fact: SessionFact): void { this.publish(this.reducer.apply(fact)) }
  public dispose(sessionId: string): void { this.publish(this.reducer.dispose(sessionId)) }
  private publish(presentation: Presentation): void {
    this.clearTerminalTimer()
    this.send({ kind: 'state', state: presentation.state, label: displayLabels[presentation.state], sequence: presentation.sequence })
    if (presentation.terminal) this.scheduleIdle(presentation.sequence)
  }
}
```

Implement `scheduleIdle(sequence)` as follows, so an old timer cannot overwrite a later event:

```ts
private scheduleIdle(sequence: number): void {
  this.timer = this.clock.setTimeout(() => {
    const current = this.reducer.current()
    if (!current.terminal || current.sequence !== sequence) return
    this.reducer.disposeTerminal(sequence)
    this.publish(this.reducer.current())
  }, 2_500)
}
```

Add `disposeTerminal(sequence)` to the reducer. It removes only session entries in `success` or `error` with that exact sequence, then returns `current()`. `clearTerminalTimer()` clears and undefines the previous timer before every publication.

In `index.ts`, define structural `PluginContext.on()` overloads for the two exact event names. After `start()`, send `{ kind: 'hello' }`, default config, and initial idle. Register callbacks that call `adaptSessionEvent`; wrap each callback in `try/catch` and log only a constant event category such as `dsh-png-pet session event ignored`.

Extend the `test` script with `test/companion-bridge.test.mjs` after that file exists.

- [ ] **Step 4: Run all Node tests.**

Run: `npm test`

Expected: PASS, including existing graceful shutdown coverage.

- [ ] **Step 5: Commit the bridge and DSH subscriptions.**

```bash
git add src/index.ts src/helper-process.ts src/companion-bridge.ts test/helper-process.test.mjs test/companion-bridge.test.mjs test/fixtures/fake-helper.mjs package.json
git commit -m "feat: bridge DSH activity to helper states"
```

### Task 5: Validate protocol v2 and display state in C#

**Files:**
- Modify: `pet-helper/ProtocolMessage.cs`
- Modify: `pet-helper/ProtocolReader.cs`
- Create: `pet-helper/PetDisplayState.cs`
- Modify: `pet-helper.Tests/ProtocolReaderTests.cs`
- Create: `pet-helper.Tests/PetDisplayStateTests.cs`

- [ ] **Step 1: Write failing xUnit tests for valid state/config input and safe fallback.**

```csharp
[Fact]
public void Parses_a_fixed_working_state()
{
    var message = ProtocolReader.Parse("{\"version\":2,\"kind\":\"state\",\"state\":\"working\",\"label\":\"工作中…\",\"sequence\":4}");
    Assert.Equal(new StateMessage("working", "工作中…", 4), message);
}

[Fact]
public void Rejects_a_free_form_state_label()
{
    Assert.Null(ProtocolReader.Parse("{\"version\":2,\"kind\":\"state\",\"state\":\"working\",\"label\":\"secret\",\"sequence\":4}"));
}

[Fact]
public void Invalid_display_state_is_disconnected()
{
    Assert.Equal(PetDisplayState.Disconnected, PetDisplayState.From("working", "secret", 4));
}
```

- [ ] **Step 2: Run the C# tests and verify missing-type failures.**

Run: `dotnet test pet-helper.Tests\PetHelper.Tests.csproj --no-restore`

Expected: FAIL because `StateMessage` and `PetDisplayState` do not exist.

- [ ] **Step 3: Add discriminated Host message records and a pure display validator.**

```csharp
public abstract record ProtocolMessage(int Version, string Kind);
public sealed record ShutdownMessage() : ProtocolMessage(2, "shutdown");
public sealed record ConfigMessage(double Scale, bool ReducedMotion) : ProtocolMessage(2, "config");
public sealed record StateMessage(string State, string Label, long Sequence) : ProtocolMessage(2, "state");

public sealed record PetDisplayState(string State, string Label, long Sequence)
{
    public static readonly PetDisplayState Disconnected = new("disconnected", "未连接", 0);
    public static PetDisplayState From(string state, string label, long sequence) =>
        sequence >= 0 && Labels.TryGetValue(state, out var expected) && expected == label
            ? new(state, label, sequence) : Disconnected;
}
```

Use `JsonDocument` rather than permissive record deserialization so the parser can require exactly the keys for each kind, reject a line over 512 characters, reject v1 and extra fields, and only accept the four allowed scale values. `ProtocolReader.Parse` must return `null` for invalid JSON or values; `PetDisplayState.From` is the sole fallback to disconnected.

- [ ] **Step 4: Run C# tests and ensure previous window-state tests remain green.**

Run: `dotnet test pet-helper.Tests\PetHelper.Tests.csproj --no-restore`

Expected: PASS.

- [ ] **Step 5: Commit C# protocol validation.**

```bash
git add pet-helper/ProtocolMessage.cs pet-helper/ProtocolReader.cs pet-helper/PetDisplayState.cs pet-helper.Tests/ProtocolReaderTests.cs pet-helper.Tests/PetDisplayStateTests.cs
git commit -m "feat: validate helper state protocol"
```

### Task 6: Render the fixed-label state bubble in WPF

**Files:**
- Modify: `pet-helper/MainWindow.xaml`
- Modify: `pet-helper/MainWindow.xaml.cs`
- Modify: `pet-helper/App.xaml.cs`
- Modify: `test/packaged-helper.test.mjs`

- [ ] **Step 1: Write a failing published-helper test for v2 ready/shutdown.**

```js
if (line === '{"version":2,"kind":"ready"}' && !shutdownSent) {
  shutdownSent = true
  child.stdin.write('{"version":2,"kind":"shutdown"}\n')
}
if (line === '{"version":2,"kind":"closed"}') resolve()
```

- [ ] **Step 2: Run the package handshake test and verify it fails against the old helper.**

Run: `node --test test/packaged-helper.test.mjs`

Expected: FAIL because the existing binary emits v1 messages.

- [ ] **Step 3: Add a bubble binding and UI-thread-safe application path.**

```xml
<Grid MouseLeftButtonDown="Pet_MouseLeftButtonDown">
  <Border x:Name="StateBubble" Visibility="Collapsed" Background="#E62B4B5F"
          CornerRadius="12" Padding="12,6" VerticalAlignment="Top" Margin="10,0,10,176">
    <TextBlock x:Name="StateLabel" Foreground="White" FontWeight="SemiBold" TextWrapping="Wrap" />
  </Border>
  <Image Source="Assets/placeholder-a.png" Stretch="Uniform" />
</Grid>
```

Add `MainWindow.ApplyDisplayState(PetDisplayState state)` to set the `TextBlock` only from `state.Label`, collapse it for idle, and use an alternate waiting background. Add `ApplyConfig(ConfigMessage config)` to resize through `PetWindowState.Normalize` and store reduced-motion for later animation work without changing its visual behavior now.

In `App.ReadProtocolLoop`, parse each line once. Dispatch `StateMessage` and `ConfigMessage` with `Dispatcher.InvokeAsync`; on `null`, dispatch `PetDisplayState.Disconnected` and call `Shutdown()` after the visual state is applied. Keep v2 `ready`/`closed` writes literal-free by using a shared `SerializeHelperMessage` method.

- [ ] **Step 4: Build the Helper, then run the published handshake test.**

Before building, confirm that `runtime/bin/win32-x64/pet-helper.exe` is not running. If it is, ask the user to choose **关闭桌宠** from its right-click menu; do not terminate the process without explicit permission because Windows locks the published executable.

Run: `npm run build:helper; node --test test/packaged-helper.test.mjs`

Expected: PASS with a v2 `ready` / `closed` exchange.

- [ ] **Step 5: Commit the WPF presentation.**

```bash
git add pet-helper/MainWindow.xaml pet-helper/MainWindow.xaml.cs pet-helper/App.xaml.cs test/packaged-helper.test.mjs
git commit -m "feat: show safe companion status bubble"
```

### Task 7: Run complete verification and package inspection

**Files:**
- Modify only if required by failing verification: affected source/test file from Tasks 1–6.
- Do not modify: image resources, package version, or user-local DSH profile in this task.

- [ ] **Step 1: Run the complete automated suite.**

Run:

```powershell
npm test
dotnet test pet-helper.Tests\PetHelper.Tests.csproj --no-restore
npm run build:helper
npm run test:package
```

Expected: every command exits `0`.

- [ ] **Step 2: Inspect the packed artifact and protocol source for prohibited material.**

Run:

```powershell
npm pack
rg -n -i 'api[_-]?key|authorization|bearer|prompt|tool arguments|session\.events|event\.data\.arguments' src test pet-helper pet-helper.Tests
```

Expected: package creation succeeds; any match must be a test assertion or design documentation, never a protocol payload, log statement, or Helper display path.

- [ ] **Step 3: Re-run targeted tests after any required correction.**

Run: `npm test; dotnet test pet-helper.Tests\PetHelper.Tests.csproj --no-restore; npm run test:package`

Expected: PASS.

- [ ] **Step 4: Commit only verification-driven corrections.**

```bash
git add src test pet-helper pet-helper.Tests package-lock.json package.json
git commit -m "test: verify DSH state bridge"
```

Do not create this commit when no correction was necessary.
