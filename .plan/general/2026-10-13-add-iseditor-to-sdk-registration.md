# 2026-10-13 — SDK 등록 요청에 isEditor 추가

- Date: 2026-10-13
- Jira: ARTEL-1015
- Status: Reviewed

## Goal

The SDK runs under `UNITY_EDITOR || DEVELOPMENT_BUILD`. The server cannot tell an editor run from a
development build, so an editor run overwrites the development build's `game_build` row. Send
`isEditor` in the registration request so the server (ARTEL-1014) can keep a separate editor row.

## Non-goals

- Server behavior (ARTEL-1014) and artel-home display.
- Any change to `gameVersion` semantics.

## Context / Constraints

- Contract is fixed: request gains `"isEditor": <bool>` from `Application.isEditor`; response gains
  `"isEditor": <bool>`. A response without it must deserialize as false and registration must succeed.
- Old servers ignore unknown request fields, so the SDK can ship first.
- `GameVersion` is captured once in `ArtelManager` (`GameVersion = Application.version`), read by
  `ArtelOverlayController`, passed to `ArtelOverlayViewModel.Register`, then to
  `ArtelSdkRegistrationClient.CreateRequest`. `IsEditor` follows the same path so the client never
  calls `Application.isEditor` itself and stays testable.

## Approach (Checklist)
- [ ] **Step 0: Recon** Done: files are `ArtelManager.cs`, `ArtelOverlayController.cs` (2 Register calls, 2 text lines),
  `ArtelOverlayViewModel.cs`, `ArtelSdkRegistrationClient.cs`, both registration DTOs.
- [ ] **Step 1: Implementation**
  - `SdkRegistrationRequestDto.IsEditor` with `[JsonProperty("isEditor")]`, always serialized.
  - `SdkRegistrationResponseDto.IsEditor` (bool, default false).
  - `ArtelManager.IsEditor { get; private set; }` set to `Application.isEditor` next to `GameVersion`.
  - `CreateRequest` and `ArtelOverlayViewModel.Register` take `bool isEditor` right after `gameVersion`
    (required, before the optional `sceneScan`).
  - Overlay text: append ` (editor)` after the game version when `IsEditor`, in both text lines.
    The two `Register` call sites pass `artelManager.IsEditor`.
- [ ] **Step 2: Tests** in `Tests/Runtime/WebSocketTransportTests.cs`: update existing exact-JSON
  assertions; add a test that `CreateRequest` body contains `"isEditor":true` / `false`; add a test that a
  response without `isEditor` parses with `IsEditor == false` and one with it parses true. Update existing
  `Register` / `CreateRequest` call sites for the new parameter.
- [ ] **Step 3: Rollout / Rollback** Ship before the server change; `git revert` to roll back.

## Validation
- **Commands to run:** Unity EditMode and PlayMode via `.github/scripts/setup-unity-test-project.sh` and the Windows Unity editor.
- **Expected output:** all tests pass.

## Risks & Rollback
- **Risks:** a strict old server could reject unknown fields (assumed not, per contract). Overlay text gets longer by 9 characters.
- **Rollback steps:** `git revert`.

## Plan review outcome

- Accepted: name `SdkLoginTests.cs` and every `Register(` / `CreateRequest(` call site as updates; put `IsEditor`
  right after `GameVersion` in the request DTO (exact-JSON assertions end with `"isEditor":...`); the label
  lives in one helper `GameVersionLabel()` and the suffix stays English `(editor)` like the identifier-like
  words in the overlay; the response `IsEditor` is deserialized only, not consumed yet; validation reads
  "no new failures".
- Not tested: the overlay label (UI construction code, no existing overlay text tests).
- Old-server tolerance of unknown request fields is taken from the decided contract, not checked here.

## Open Questions
- None.
