# 2026-10-14 — SDK: [ArtelCheat] / [ArtelBehavior] 등록과 invoke_cheat · start_behavior action, GAME_FUNCTION_CATALOG frame

- Date: 2026-10-14
- Jira: ARTEL-1052 (Subtask of Story ARTEL-1051, Epic ARTEL-1050)
- Status: Draft

## Goal

Implement the SDK side of the game-functions wire contract
(`/home/yunseong/dev/artel/.plan/general/2026-10-14-game-functions-wire-contract.md`, binding):

- Public API that compiles in every build: `[ArtelCheat]`, `[ArtelBehavior]`, `[CheatRange]`,
  `ArtelBehaviorContext` (`Report`, `Complete`, `Fail`), `ArtelCheats.Register` / `Unregister`.
- A weaver pass that turns attributed methods into a generated registration with direct calls.
- Four actions (`invoke_cheat`, `start_behavior`, `stop_behavior`, `extend_behavior`), two frames
  (`GAME_FUNCTION_CATALOG`, `BEHAVIOR_ENDED`), two `PULSE` fields (`functions`, `behaviors`).
- ADR 0007 amending ADR 0002, `docs/protocol.md` update.

## Non-goals

- Orchestration, agent-server, artel-home (other subtasks).
- Behaviors registered at runtime (only `[ArtelBehavior]`; `ArtelCheats.Register` is cheats only).
- Generic behaviors without game code (phase 2 of the design).
- Weaver warnings for unsupported `yield return` values: what an iterator yields is not visible
  in its signature. The runner logs one runtime warning per behavior instead.

## Context / Constraints

- `ActionExecutor.Execute` is a closed `switch`; `scan_evidence` shows the
  `#if UNITY_EDITOR || DEVELOPMENT_BUILD` gate. `ArtelManager` and `ActionExecutor` ship in
  release builds.
- ILPP: `Editor/CodeGen/ArtelILPostProcessor.cs` runs `ActionMethodWeaver` and
  `InputMethodWeaver` on every assembly `WeavableAssemblies` lets through, test assemblies
  included. `IsDiscoveryBuild` (UNITY_EDITOR or DEVELOPMENT_BUILD define) lives privately in
  `AffordanceILPostProcessor`; the CodeGen asmdefs cannot share code, so the check is repeated.
- The CodeGen test assembly cannot reference `Unity.CompilationPipeline.Common`, so a weaver that
  reports through `DiagnosticMessage` cannot be unit tested. `InputMethodWeaver` returns plain
  strings for that reason; the new weaver does the same.
- `PULSE` is composed by hand in `Artel.Affordances.Scan` (`LiveState.Compose`), which
  `Artel.Runtime` references and not the other way round. A `PULSE` reading whose watched values
  did not move is held (`settled`) and never sent.
- Selectors: `ScenePath.SelectorOf(transform, rootIndex)` writes, `ScenePathSelectorResolver`
  reads (active scene only).

## Approach (Checklist)

- [ ] **Step 1: public surface** — `Runtime/GameFunctions/`
  - `ArtelCheatAttribute(string description, string name = null, string when = null)`,
    `ArtelBehaviorAttribute(string description, string group = null, string name = null, string when = null)`,
    `CheatRangeAttribute(double min, double max)`, `ArtelBehaviorContext`, `ArtelCheats`
    (`Register(name, description, IReadOnlyList<CheatParam>, Func<CheatArgs, object>)`,
    `Unregister(name)`), `CheatParam` (static factories per type), `CheatArgs`, `CheatParamType`.
  - Generated-code entry, public and `[EditorBrowsable(Never)]`: `GameFunctionTable.Add(...)`,
    delegates `GameFunctionCall(object target, object[] arguments)` and
    `GameFunctionCondition(object target)`. In a release build every body is empty.
- [ ] **Step 2: registry and execution** (everything below behind `#if UNITY_EDITOR || DEVELOPMENT_BUILD`)
  - `GameFunctionRegistry` (cheats and behaviors by name, `Revision` starting at 1, duplicate names
    rejected with a log line, first registration wins), catalog DTOs.
  - `GameFunctionArguments`: `args` object → typed `object[]`, defaults for omitted optional
    parameters, `bad argument <param>: <why>` (type, range, unknown enum value, unknown key,
    missing required).
  - `GameFunctionTargets`: the contract's target rules and error strings, selector via
    `ScenePathSelectorResolver`, live instances via `Object.FindObjectsOfType(Type)` (active
    objects only), selector of a live instance via `ScenePath.SelectorOf`.
  - `GameFunctionAvailability`: build → target → `when`. A `when` that throws is unavailable with
    `<ExceptionType>: <message>`.
  - `CheatInvocation`: synchronous result (`null`/`string` → `message`) or `IEnumerator` driven by
    hand inside the action coroutine so an exception becomes `<ExceptionType>: <message>`; the
    last `string` the coroutine yielded becomes `message`.
  - `BehaviorRunner`: at most 4, `maxSeconds` 1–300 (default 30), group replacement, iterator
    stack driven with `MoveNext()` (nested `IEnumerator` supported), `Dispose()` on every end,
    `yield return null` = one frame, `WaitForSeconds` read through its `m_Seconds` field,
    anything else = one frame plus one warning. Time is `Time.deltaTime` (frozen by
    `pause_time`). `when` and target checked every tick for target, at most once per second for
    `when`. Ends emit `BehaviorEndedDto` through an event.
  - `GameFunctionActions`: the four action handlers, called from `ActionExecutor.Execute`. Release
    build: `cheats are not available in this build` / `behaviors are not available in this build`.
- [ ] **Step 3: manager wiring** — `ArtelManager`
  - Send `GAME_FUNCTION_CATALOG` right after `DEVICE_CONTEXT`, then at most once per second when
    `Revision` moved.
  - Tick the runner every frame before the transport check; send `BEHAVIOR_ENDED` when connected.
  - `run_ended` on `reset_game` (before the reload), `RUN_STATUS` `FINISHED`, socket close
    (connected → not connected), `StopTransport`, `ClearWebSocketTransport`.
- [ ] **Step 4: PULSE fields** — `Artel.Affordances.Scan`
  - `IPulseAttachment { bool Write(StringBuilder text, bool whole); }` set on `LiveState`.
    `Compose` calls it before `changed`; a `true` return means "news" and keeps the reading from
    being held. `Artel.Runtime` implements it (`GameFunctionPulse`): `functions` whole on a whole
    reading, otherwise only changed entries (keyed by `(kind, name, target)`, `removed: true` for a
    gone instance), evaluated at most once per second; `behaviors` on every reading while the
    registry has at least one behavior (`[]` when none runs). News = a functions delta, or a
    behavior list change, or 1 second since the last behaviors report while one runs.
- [ ] **Step 5: weaver** — `Editor/CodeGen/GameFunctionWeaver.cs`
  - Runs only when the assembly references `Artel.Runtime` and the defines contain `UNITY_EDITOR`
    or `DEVELOPMENT_BUILD`.
  - Per attributed method: validate (parameters `int`/`float`/`bool`/`string`/enum, behaviors may
    end with `ArtelBehaviorContext`; cheat returns `void`/`string`/`IEnumerator`, behavior returns
    `IEnumerator`; no generic method or type; instance methods only on `MonoBehaviour`; declaring
    type reachable from its assembly; duplicate names per kind; `when` exists on the same type,
    takes no parameters, returns `bool` or `string`, static when the function is static).
  - Emit into the declaring type a static stub `__artel_call_<n>(object, object[])` that unboxes
    each argument and calls the method (so private methods work), and `__artel_when_<n>(object)`
    when there is a `when`.
  - Emit one class `Artel.Generated.GameFunctionRegistration` with a static `Register()` marked
    `[RuntimeInitializeOnLoadMethod(BeforeSceneLoad)]` calling `GameFunctionTable.Add` per method.
  - Names: `GiveGold` → `give_gold`, overridable by `name:`. Errors become ILPP error diagnostics.
- [ ] **Step 6: tests**
  - CodeGen EditMode: Cecil-built modules → each weaver error; a valid module → emitted
    registration loaded with `Assembly.Load(bytes)` and invoked against a fresh registry.
  - New fixtures asmdef `Artel.GameFunctions.Fixtures` (`UNITY_INCLUDE_TESTS` constraint), woven by
    Unity itself: end-to-end check that the real ILPP produced a registration.
  - Runtime EditMode: argument validation, unknown name, handler exception, coroutine cheat, target
    rows, release-define failure (the `#else` handler is a separate static method called directly),
    catalog JSON shape, `PULSE` fields shape.
  - PlayMode: every lifecycle row, `finally` on stop and timeout, group replacement, the cap,
    `when` turning false, `pause_time` freezes a behavior.
- [ ] **Step 7: docs** — `docs/adr/0007-game-registered-cheats-and-behaviors.md`, ADR index,
  ADR 0002 note, `docs/protocol.md` (17 → 21 actions, new frames, `PULSE` fields). Korean.

## Decisions after plan review

Fast and medium reviews ran as separate subagents; the heavy pass was done by the implementing
agent against this section.

- One `IteratorStepper` drives both cheat coroutines and behaviors: nested `IEnumerator` frames,
  `Dispose()` of every frame on any end, exceptions surface as `<ExceptionType.Name>: <message>`.
  A cheat passes Unity yield instructions (`AsyncOperation`, `WaitForSeconds`) through to the
  action coroutine; a behavior honours `null` and `WaitForSeconds` and treats anything else as one
  frame plus one warning. A cheat coroutine that runs longer than 60 seconds of real time is
  disposed and fails (`<name> did not finish within 60 seconds`) so the action queue cannot stall.
- `ArtelCheats.Register` wraps its handler into the same `GameFunction` record and
  `GameFunctionCall` delegate the weaver output creates. `CheatArgs` is a view over the validated
  `object[]`; there is one validation path. The weaver passes parameter type, enum type, range and
  default as data; the runtime never reflects over the game method.
- `ArtelILPostProcessor` computes the discovery-build flag once and passes it to the weaver.
- One constant, `GameFunctionTiming.PollSeconds = 1`, for catalog resend, `when` evaluation for
  `PULSE.functions`, the `when` check of running behaviors, and the behaviors report cadence.
- Availability lives in `GameFunctionTargets` with target resolution; they always run together.
- `IPulseAttachment` exists because `Artel.Affordances.Scan` cannot reference `Artel.Runtime`.
- Wire nulls: catalog entries always carry `default`, `values`, `min`, `max`, `target`, `group`
  (null when absent). `BEHAVIOR_ENDED` always carries `target` and `detail`. A `removed` entry in
  `PULSE.functions` carries only `kind`, `name`, `target`, `removed: true`. Golden JSON tests pin
  these against the contract examples.
- `PULSE.functions` is a diff over the key set `(kind, name, target)`, so the
  `target: null` placeholder of an instance function is removed when an instance appears and comes
  back when the last one goes.
- `start_behavior` returns success with `behaviorId` and `report` (the report after the first
  `MoveNext()`) even when that first step already ended it. `BEHAVIOR_ENDED` frames are queued by
  the runner and flushed from `ArtelManager.Update`, so a frame ended inside a batch goes out on the
  frame after the step, and in practice after the batch's `ACTION_RESULT` for one-frame batches.
- Validation order for `invoke_cheat` and `start_behavior`: build, unknown name, target rules,
  args, `maxSeconds` (behaviors), `when`, then cap. A same-group behavior that would be replaced
  does not count against the cap of 4.
- Targets: selectors listed in errors and in `PULSE` come from live instances of that component
  in any loaded scene. Resolving a `target` first matches it against those instances' selectors,
  then falls back to `ScenePathSelectorResolver` (which only knows the active scene) for the error
  text. So every selector the SDK hands out resolves.
- Arguments: `args` absent or null is `{}`; any other non-object is
  `bad argument args: must be an object`. Unknown key → `bad argument <key>: no such parameter`.
  `int` accepts JSON integers and integral doubles inside `Int32`; `float` rejects NaN and
  infinity; `bool` takes only JSON booleans; `string` only JSON strings; `enum` takes the exact
  member name. A parameter is optional only when it has a C# default; an enum default is written as
  the member name. A `string` parameter with default `null` and a required one both show
  `default: null` in the catalog (contract has no `required` key).
- `when`: `bool` false → reason `<Method> returned false`; `string` null or empty → available,
  any other string is the reason; a throw → `<ExceptionType>: <message>`. An instance function may
  name a static `when`; a static function must name a static one.
- Numbers: `seconds` rounded to one decimal; `maxSeconds` accepts fractions in 1–300; an integral
  `double` is written without a fraction. `extend_behavior.seconds` must be positive and the new
  total at most 300.
- `behaviorId` counts from 1 per process. `stop_behavior` with `all: true` ignores `behaviorId` and
  answers `{ "stopped": [] }` when nothing runs; an ended or unknown id is
  `unknown behavior id: <id>`; a reason other than `stopped` / `not_allowed` is
  `bad argument reason: ...`.
- Frames produced while the socket is down (including `run_ended` for that close) are dropped,
  not queued.
- `Revision` moves only on an actual add or remove. Release builds send no catalog frame.
- Names: `GiveXPNow` → `give_xp_now`; an explicit `name:` must match `^[a-z][a-z0-9_]*$` (weaver
  error otherwise).
- The same behavior may run twice (on two targets, or twice on one target when it has no group);
  each run counts toward the cap. `group` is global, not per target.

## Validation

- **Commands to run:** `.github/scripts/setup-unity-test-project.sh /mnt/c/temp/artel-unity-test-1052`,
  then Unity `-runTests` EditMode and PlayMode (Windows editor 2022.3.34f1 from WSL), summarized
  with `.github/scripts/summarize-test-results.py`.
- **Expected output:** baseline failures unchanged, all new tests pass.

## Risks & Rollback

- **Risks:** `RuntimeInitializeOnLoadMethod` on an ILPP-added method (Netcode for GameObjects does
  the same, and the fixture test checks it); `WaitForSeconds.m_Seconds` is a private Unity field
  (fallback: one frame plus warning); `FindObjectsOfType` once per second per instance function;
  `BEHAVIOR_ENDED` for a socket close cannot be delivered on that socket.
- **Rollback steps:** `git revert`. Old servers never send the four methods; extra frames are
  ignored by servers that do not know them.

## Open Questions

- None blocking. Interpretations are listed in the handoff report.
