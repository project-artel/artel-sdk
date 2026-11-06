# 2026-09-18 — evidence scan 이 Instrument 로 표시된 객체를 보고에서 뺀다

- Date: 2026-09-18
- Jira: ARTEL-906
- Status: Implemented. Jira 이슈의 Problem / Acceptance Criteria / Constraints 가 이미 원인과 방향을 못박아 둔
  상태로 작업이 시작됐고, 별도로 `plan-review` skill 을 돌리지 않았다 — 아래 Approach 는 구현과 함께 쓴 기록이다.

## Goal

`SceneEvidenceScan` 이 `Instrument.Marks(subject)` 인 객체와 그 아래를 보고에 쓰지 않는다. 씬 순회
(`Capture`/`CaptureLoaded`)와 `CapturePersistent` 양쪽에 적용한다. 게임 객체 자신의 보고 내용은 이 변경 전후로
같다.

## Non-goals

- `SceneScanner` 와 `AllSceneScanner` 는 건드리지 않는다. SDK 트리가 active scene 에 없어서 거기 안 나온다.
- orchestration 쪽에 이름 기반 제외를 넣지 않는다.
- 이미 적재된 content_map 을 소급해 고치지 않는다.
- 이름으로 거르지 않는다 — 오브젝트 이름은 게임이 자유롭게 쓴다.

## Context / Constraints

- `SceneEvidenceScan.cs:129`(변경 전)는 `hideFlags` 만 본다. 그 검사는 걷기 자신의 carrier
  (`Artel Pulse`, `Artel Scene Walk`) 만 거른다 — `ArtelManager` 는 게임이 놓은 오브젝트에 컴포넌트로 붙고
  캔버스를 그 자식으로 만들어서, root 가 게임 것이고 `hideFlags` 가 `None` 이다.
- `CapturePersistent` 는 `DontDestroyOnLoad` 씬을 걷는데, `ArtelManager.Awake` 가
  `transform.SetParent(null); DontDestroyOnLoad(gameObject);` 를 부르므로 SDK 의 오버레이 트리가 사는 자리가
  정확히 거기다.
- `Instrument.Marks` 는 `internal` 이고 `SceneEvidenceScan` 과 같은 어셈블리(`Artel.Affordances.Scan`)에 있어
  접근에 문제가 없다.
- `Instrument.Marks` 는 불릴 때마다 조상을 거슬러 오른다. `Live.Worth` 는 그 답을 객체마다 프레임을 넘어
  기억해 두지만, evidence scan 은 씬 로드나 evidence scan 요청 한 번에 한 번만 돈다
  (`SceneWalk.Visit`, `AffordanceBootstrap.Capture`) — 프레임마다 갚는 값이 아니므로 `Worth` 의 캐시를 그대로
  옮기는 것은 이 순회가 갖지 않은 문제에 코드를 더하는 일이다. 대신 root 하나를 걷는 동안만 사는 얕은
  `Dictionary<Transform, bool>` 을 쓴다 — `GetComponentsInChildren<Transform>(true)` 가 부모를 자식보다 먼저
  내놓는 것에 기대어, 각 transform 은 제 컴포넌트만 보고 부모의 답을 물려받는다.

## Approach (Checklist)

- [x] **Step 1: `SceneEvidenceScan.Instrumented` 헬퍼** — root 하나짜리 `Dictionary<Transform, bool>` 을 받아
  자기 자신의 `Instrument` 컴포넌트 여부와 부모의 기억된 답을 합쳐 반환한다.
- [x] **Step 2: 씬 순회(`Capture`)에 적용** — `GetComponentsInChildren<Transform>(true)` 루프에서
  `Describe` 를 부르기 전에 `Instrumented` 를 확인해 건너뛴다.
- [x] **Step 3: `CapturePersistent` 에도 같은 필터** — 기존 `hideFlags` 검사(캐리어 root 를 거르는 것) 뒤에,
  root 마다 새 사전으로 같은 필터를 적용한다.
- [x] **Step 4: `Instrument.cs` 주석에 소비자가 둘이 됐다는 사실을 적는다** — `Marks` 의 remarks 에
  `SceneEvidenceScan` 이 같은 규칙을 직접 구현해서 쓴다는 것과 그 이유(호출 빈도 차이)를 남긴다.
- [x] **Step 5: Tests** — `SceneEvidenceScanInstrumentTests.cs` 를 새로 추가한다.
  `UnityEventTools.AddPersistentListener` 로 인스펙터 배선을 흉내 내(`SceneScannerTests` 와 같은 방법)
  evidence 없이도 컴포넌트가 보고에 실릴 자격을 만들고, `Instrument` 로 표시된 객체·그 자식·꺼진 계기가
  `Capture`/`CapturePersistent` 양쪽에서 빠지는지, 계기가 붙은 게임 오브젝트 자신은 그대로 남는지 확인한다.
- [ ] **Step 6: Manual verification** — WordVenture 로 `scan_evidence` 를 돌려 `Artel` 로 시작하는 객체가
  결과 문서에 없는 것을 확인하고 전후 문서를 PR 에 붙인다. Windows 쪽 Unity 에디터가 필요해 이번 세션(worktree
  가 `/home` 아래)에서는 EditMode/PlayMode 자동 테스트까지만 확인하고, 이 수동 검증은 확인하지 못했다.

## Validation

- **Commands run:** `.github/scripts/setup-unity-test-project.sh /mnt/c/artel-unity-test-906` 로 throwaway
  프로젝트를 만들고, Windows 쪽 `Unity.exe -batchmode -nographics -runTests -testPlatform EditMode` 로 전체
  EditMode suite 를 돌렸다 (worktree 는 `/home` 아래이지만 `/mnt/c` 로 프로젝트를 복사해서 우회했다).
- **Not run:** PlayMode suite, WordVenture 로 실제 `scan_evidence` 를 돌리는 수동 검증. PR 본문의
  "확인하지 못한 것" 절에 남긴다.

## Risks & Rollback

- **Risks:** `Instrumented` 헬퍼가 `GetComponentsInChildren<Transform>(true)` 의 부모-먼저 순서에 기댄다 —
  Unity 문서화된 동작이고 이 파일의 기존 코드도 이미 같은 API 로 순회하므로 새로 생기는 가정은 아니다.
- **Rollback steps:** `git revert` 로 PR 커밋을 되돌린다. 프로토콜과 서버 계약은 바뀌지 않는다.

## Open Questions

- 없음.
