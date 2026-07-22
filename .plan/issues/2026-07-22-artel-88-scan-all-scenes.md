# 2026-07-22 — scan_all_scenes로 빌드 내 전체 씬 스캔

- Date: 2026-07-22
- Jira Issue: ARTEL-88
- Status: Implemented
- Repository: sdk
- Work Type: feat
- Depends on: ARTEL-87 (배치 디스패치 경로)

## Goal

에이전트가 게임 전체의 UI 지도를 한 번에 얻을 수단이 없다. `scan_scene`은 현재 활성 씬만 본다.

Build Settings에 등록된 모든 씬을 인덱스 순서대로 로드하며 각각 스캔하고, 결과를 `ALL_SCENES`
메시지 하나로 묶어 보내는 `scan_all_scenes`을 추가한다.

## Non-goals

- **에디터 경로를 `scan_all_scenes` 안에 넣기.** `EditorSceneManager.OpenScene`은 플레이 모드에서
  거부된다. SDK는 플레이 모드에서만 소켓을 여니 같은 메서드 안에서 갈라질 수 없다. 부작용 없는
  경로는 별도 메뉴 커맨드(`Artel ▸ Export Scene Map…`)로 제공한다.
- **최상위 메시지 형태.** 씬 로드는 여러 프레임에 걸치므로 `HandleMessage`에서 즉시 응답할 수 없다.
  ACTION 배치 전용이다.
- **씬별 실패 격리.** 코루틴 안에서 `yield` 주위를 `try/catch`로 감쌀 수 없다. 기존
  `ExecuteActionRequest`와 같은 취약성을 그대로 둔다.
- **로드 순서 지정 / 부분 스캔.** 인덱스 0부터 전부 훑는다. 필터가 필요하면 별도 이슈.
- **씬 간 상태 보존.** 스캔용으로 잠깐 띄웠다 내리는 것이지, 게임을 진행시키는 것이 아니다.

## Context / Constraints

- **Additive 로드가 필수다.** `LoadSceneMode.Single`은 현재 씬을 파괴하고, `ArtelManager`가 그
  씬에 있다. 자기 자신을 destroy하면 이 코루틴이 중간에 죽는다. Additive는 현재 씬을 살려둔다.
- 이미 열려 있는 씬(대개 현재 씬)은 다시 로드하지 않는다. `GetSceneByPath(path).isLoaded`로 확인해
  제자리에서 스캔하고 언로드도 하지 않는다. 중복 인스턴스와 SDK 자폭을 동시에 피한다.
- `SceneScanner.Scan()`은 활성 씬만 훑으므로 씬마다 `SetActiveScene`이 필요하다.
- 로드 완료 시점에 `Awake`/`OnEnable`은 돌았지만 `Start`는 아직이다. UI 텍스트 상당수가 `Start`에서
  채워지므로 한 프레임(`yield return null`) 기다린 뒤 스캔한다.
- **`SceneStatePoller.ScanNow()`를 쓰지 않는다.** 그 경로는 해시 트래커를 갱신해서, 다른 씬의 스냅샷이
  현재 씬의 "변경 감지" 기준선을 오염시킨다. `scanner.Scan()` + `SceneSnapshotMapper.ToDto`를 직접 쓴다.
- **추적 액션을 커밋하지 않는다.** 방문한 씬의 pending 액션을 여기서 소비하면 다음 `GAME_STATE`에서
  사라진다.
- **`DontDestroyOnLoad`는 언로드로 안 사라진다.** 별도 씬이라 `UnloadSceneAsync`가 건드리지 않는다.
  워크 시작 전 DDOL 루트 인스턴스 ID를 스냅샷해두고, 씬마다 언로드 직후 신규 루트를 제거한다.
  씬 단위로 쓸어야 앞 씬의 잔재가 다음 씬 스캔 중에 돌지 않는다. DDOL 씬 핸들은 공개 API가 없어
  임시 오브젝트를 넣었다 빼서 얻는다.
- **끝나고 재스캔해야 한다.** `scanner`의 `targetsById`는 마지막으로 훑은 씬 것인데 그 씬은 이미
  언로드됐다. 원래 활성 씬으로 되돌린 뒤 `scanner.Scan()`을 한 번 더 돌려 대상 ID 맵을 복구한다.

## Approach (Checklist)

- [x] **Step 0: Recon** — `SceneScanner.Scan`의 활성 씬 의존, `SceneSnapshotMapper.ToDto`,
      `SceneStatePoller.ScanNow`의 해시 트래커 부작용 확인.
- [x] **Step 1: Implementation**
  - `AllSceneScanner` 신규 — 빌드 인덱스 순회, additive 로드/언로드, 활성 씬 전환, 복구까지 담당.
  - `AllScenesMessageDto` / `ScannedSceneDto` 신규. 항목마다 `buildIndex`, `path`, `scene`.
    `scene`은 `GAME_STATE`와 동일한 `SceneDto`라 서버 파싱 경로가 하나로 유지된다.
  - `ArtelManager.ExecuteActionRequest`에 `scan_all_scenes` 분기, `SendAllScenes` 추가.
  - `DontDestroyOnLoadGuard` 신규 — 워크 전 DDOL 루트 스냅샷, 씬마다 언로드 직후 신규 루트 제거.
  - 테스트 페이지에 `Scan all scenes` 버튼. 응답은 `GAME_STATE`와 같은 렌더러로 그리고, 언로드된
    씬의 컨트롤만 비활성.
  - `Artel ▸ Export Scene Map…` 에디터 메뉴 — 플레이 모드를 끄고 같은 문서를 만든다. 부작용 없음.
- [x] **Step 2: Tests** — `SceneJsonContractTests`에 `ALL_SCENES` 직렬화 계약 테스트,
      `WebSocketTransportTests`에 테스트 페이지 마크업 계약 테스트 추가.
- [x] **Step 3: Rollout / Rollback** — 순수 추가. 호출하지 않으면 영향 없음.

## Validation

- **Commands to run:** Unity Test Runner (EditMode) — `Artel.Runtime.Tests`.
- **Expected output:** 신규 계약 테스트 포함 전체 통과.
- **실제 수행:** 이 작업 환경에 Unity 에디터가 없어 **실행하지 못했다**.
- **자동화 공백:** 씬 로드는 EditMode에서 동작하지 않아 `AllSceneScanner`의 순회 자체는 PlayMode
  테스트가 필요하다. 이 커밋에는 없다. 직렬화 계약과 테스트 페이지 마크업만 자동 검증되고,
  **로드/스캔/복구·DDOL 정리·에디터 익스포트는 전부 미검증**이다. 실제 빌드 씬이 등록된 프로젝트에서
  수동 확인이 필요하다. `DontDestroyOnLoad`는 플레이 모드 밖에서 동작하지 않아
  `DontDestroyOnLoadGuard`도 EditMode 테스트로 덮을 수 없다.
- **테스트 페이지 렌더링은 검증됨:** 페이지 스크립트를 C# 문자열에서 추출해 Node에서 DOM 스텁과
  가짜 `ALL_SCENES` 페이로드로 실행, 살아있는 씬 버튼은 활성 / 언로드된 씬 버튼은 비활성 확인.

## Risks & Rollback

- **다른 씬의 코드가 실제로 실행된다.** `Awake`/`OnEnable`/`Start`가 돌면서 오디오, 네트워크 요청,
  `PlayerPrefs` 쓰기 같은 부작용이 진짜로 일어난다. README에 명시했고, 진행 중인 플레이 세션에서
  부를 메서드가 아니다. 부작용이 문제면 에디터 익스포트를 쓴다.
- **DDOL 가드가 되돌리지 못하는 것.** 파괴된 매니저가 남긴 `static` 필드·이벤트 구독, 중복을
  자폭시키는 싱글톤 패턴에서 게임 쪽 인스턴스가 먼저 죽은 경우, 이미 나간 오디오·네트워크 요청.
  워크 도중 게임이 스스로 만든 DDOL 오브젝트도 신규로 판정돼 함께 죽는다.
- **응답 크기.** 씬 수 × 블록 트리. 큰 프로젝트에서 한 메시지가 상당히 커질 수 있다. 페이지네이션은
  현재 없다.
- **소요 시간.** 씬당 최소 로드 1회 + 1프레임 + 언로드 1회. 배치 안의 뒤따르는 액션은 전부 대기한다.
- **반환된 블록 ID는 곧 죽는다.** 스캔이 끝나면 그 오브젝트들은 언로드된다. 구조 파악용이지
  `button_click` 대상으로 쓸 수 없다.
- **Rollback steps:** `git revert`.

## Open Questions

- 없음. 최초 요청은 `scan_all_scene`이었고, 복수형 `scan_all_scenes`로 확정했다. 서버·에이전트가
  아직 이 메서드를 부르지 않으므로 마이그레이션 부담은 없다.
- 응답이 커질 때 씬 단위 스트리밍(씬마다 `GAME_STATE` 한 건)으로 바꿀지. 지금은 한 건에 모은다.
