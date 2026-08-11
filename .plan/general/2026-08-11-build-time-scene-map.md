# 2026-08-11 — 빌드 타임 씬 지도로 scan_all_scenes 대체

- Date: 2026-08-11
- Jira: ARTEL-90
- Status: Approved (plan-review 3패스 통과)

## Goal

`scan_all_scenes`가 런타임에 빌드 씬을 실제로 로드·실행하는 대신, 빌드에 동봉된 정적 씬 지도를
읽어 `ALL_SCENES`로 답하게 한다.

런타임 순회는 방문 씬의 `Awake`/`OnEnable`/`Start`를 진짜로 실행한다. ARTEL-88의 잔여 오브젝트
이주는 "어딘가에 생긴 새 루트 오브젝트"만 청소할 수 있고, 네트워크 요청·애널리틱스 이벤트·세이브
쓰기·오디오처럼 이미 밖으로 나간 부작용은 되돌리지 못한다. 진행 중인 플레이 세션 한가운데서
전체 씬 구조를 물어볼 수 있으려면 씬을 실행하지 않는 경로가 필요하다.

지도는 에디터에서 `EditorSceneManager.OpenScene`으로 만든다. 플레이 모드가 아니므로 씬 코드가
한 줄도 돌지 않는다.

## Non-goals

- **빌드 콜백 안에서 지도 생성.** `IPreprocessBuildWithReport` 안에서 씬을 여는 것이 지원되는지
  확인할 수단이 이 환경에 없다. 훅은 **검사만** 하고, 생성은 사람이 메뉴로 한다.
- **런타임 순회 제거.** 코드로 UI를 구성하는 화면처럼 정적 지도가 못 잡는 경우가 있다.
  `params: ["live"]` 옵트인으로 남긴다.
- **지도의 상세도 선택.** 지도는 익스포트 시점에 한 번 만들어지므로, 읽는 쪽이 사후에 상세도를
  고를 수 없다. 항상 `SceneScanOptions.Full`로 굽는다(아래 결정 참조).
- **프리팹·스크립터블 오브젝트 변경 감지.** 신선도 검사는 이슈가 정한 대로 씬 에셋 기준이다.
  씬이 참조하는 프리팹만 바뀐 경우는 잡지 못한다. 한계로 문서화한다.
- **지도 페이지네이션.** 응답 크기 문제는 ARTEL-88에서 이미 열린 항목이고 여기서 다루지 않는다.
- **서버·에이전트 측 변경.** `ALL_SCENES` 메시지 형태는 그대로다.

## Context / Constraints

### 현재 코드

- `ArtelManager.ExecuteActionRequest`(`Runtime/ArtelManager.cs:406`)가 `scan_all_scenes`를
  가로채 `AllSceneScanner.ScanAll`을 코루틴으로 돌리고 `SendAllScenes`로 답한다.
- `TryReadScanOptions`(`Runtime/ArtelManager.cs:450`)가 `[]` / `["default"]` / `["full"]`을
  `SceneScanOptions`로 옮긴다. 그 외는 실패.
- `AllSceneScanner`(`Runtime/AllSceneScanner.cs`)가 additive 로드·활성 씬 전환·`StraySpawnTracker`
  이주·언로드·재스캔까지 담당한다. 이 클래스는 `live` 경로로 **그대로 남는다**.
- `SceneScanReporter`(`Runtime/SceneScanReporter.cs`)는 등록 시점 보고용으로 같은 `AllSceneScanner`를
  쓴다. 등록은 세션 시작 시점이라 부작용 위험이 낮고, 이 이슈의 완료 기준에 등록 경로는 없다.
  **이번 범위에서 건드리지 않는다.**
- 익스포터는 ARTEL-88의 커밋 `1049b7d`에서 제거됐다. `Editor/Artel.Editor.asmdef`와
  `Editor/ArtelSceneMapExporter.cs`를 되살려 시작점으로 쓴다.

### 결정: 지도는 `SceneScanOptions.Full`로 굽는다

지도는 빌드 시점에 한 번 만들어진다. 읽는 쪽이 상세도를 고를 수 없으므로, 둘 중 하나다 —
상세도별로 문서를 여러 벌 굽거나, 항상 상위집합을 굽거나. 지도의 용도는 "게임 전체 UI 구조 파악"
이고 그건 `full`이 정의하는 것(직렬화 필드 전체, 비활성 오브젝트, 버튼 `onClick` 배선) 그 자체다.
`default`로 구우면 `[ArtelState]` 붙은 값과 활성 오브젝트만 남아 지도로서 거의 쓸모가 없다.

따라서 `[]` / `["default"]` / `["full"]`은 **모두 같은 지도**를 돌려준다. 기존 클라이언트가 깨지지
않게 파라미터 문법은 유지하되, 상세도를 더 이상 바꾸지 않는다는 사실을 README에 명시한다.

### 결정: `["live"]`도 `Full`로 돈다

`live`는 정적 지도가 못 잡는 화면을 위한 탈출구다. 지도의 대체재가 되려면 같은 상세도여야 한다.
결과적으로 "런타임 순회를 기본 상세도로" 돌리는 조합은 사라진다. 그 조합의 유일한 이점은 페이로드
크기였고, 문법을 문자열 하나로 유지하는 값이 더 크다고 본다.

### 지도 파일 위치

`Assets/Resources/ArtelSceneMap.json`. 패키지가 아니라 **소비 프로젝트의** `Assets` 아래다.
`Resources` 폴더여야 빌드에 동봉되고 런타임에서 `Resources.Load<TextAsset>("ArtelSceneMap")`으로
읽을 수 있다. `.json`은 Unity가 `TextAsset`으로 임포트한다.

### 결정: 파일 포맷은 와이어 메시지와 분리한다

`1049b7d`의 익스포터는 `AllScenesMessageDto`를 그대로 파일에 썼다. 그러면 파일에 세션용
`type: "ALL_SCENES"` / `id: 0`이 의미 없이 남고, 더 나쁜 것은 **버전 표시가 없다는 점**이다.
`NewtonsoftJsonCodec`은 `MissingMemberHandling.Ignore`라, 스캔 DTO 모양이 바뀐 SDK로 올린 뒤
낡은 지도를 읽으면 사라진 필드가 조용히 비어서 반쪽 지도가 된다. mtime 검사는 이걸 못 잡는다 —
SDK를 올려도 씬 `.unity` 파일의 수정 시각은 그대로다.

그래서 파일 전용 DTO를 둔다:

```json
{ "formatVersion": 1, "scenes": [ { "buildIndex": 0, "path": "…", "scene": { … } } ] }
```

`SceneMap.FormatVersion` 상수를 손으로 올린다. 스캔 DTO 모양을 바꾸는 변경은 이 값을 올려야 하고,
빌드 훅이 불일치를 stale과 같은 실패로 처리한다. 런타임은 `scenes`만 꺼내 `SendAllScenes`에
넘기고, 와이어 메시지(`type`/`id`)는 지금처럼 `SendAllScenes`가 만든다. `scene`이 `GAME_STATE`와
같은 `SceneDto`라는 성질은 그대로다.

### 지도의 좌표는 에디터 해상도 기준이다

`SceneScanner`는 `Screen.width`/`Screen.height`를 `scene.screen`에 담고, 각 블록의
`transform.rect`를 그 화면에 투영해 픽셀로 적는다(`BlockTransformReader`). 에디터에서 구우면
그건 **Game view 해상도**이지 기기 해상도가 아니다. 프로토콜이 `screen`을 함께 싣는 이유가
비율 보정이라 단순 스케일은 되지만, 종횡비가 다르면 캔버스 스케일러와 앵커가 실제로 재배치하므로
보정으로 메울 수 없다.

받아들인다. 지도의 블록 ID는 어차피 죽은 값이라(그 씬은 로드된 적이 없다) `button_click` 대상이
될 수 없고, 지도는 구조 파악용 문서다. `rect`는 참고값이지 조준값이 아니라는 것을 README에 적는다.
실제 좌표가 필요하면 그게 `live`를 쓰는 이유다.

### 신선도 검사

- 대상: `EditorBuildSettings.scenes` 중 `enabled`인 것.
- 실패 조건 네 가지:
  1. 지도 파일이 없다.
  2. 지도의 `formatVersion`이 `SceneMap.FormatVersion`과 다르다(파싱 실패도 여기).
  3. 지도에 담긴 씬 경로 목록이 현재 빌드 씬 목록과 다르다(순서 포함).
  4. 어떤 씬 `.unity` 파일의 최종 수정 시각이 지도 파일의 최종 수정 시각보다 늦다.
- `BuildFailedException`으로 빌드를 세운다. 메시지는 `Artel ▸ Export Scene Map…`을 다시 돌리라고
  말한다.
- 비교 로직은 Unity API에 닿지 않는 순수 함수로 분리해 EditMode 테스트로 덮는다. `IPreprocessBuild`
  구현체는 그 함수에 값을 모아 넘기는 껍데기만 남긴다.

### "지도가 없으면 런타임이 실패를 답한다"가 닿는 곳

빌드 훅이 지도 없는 빌드를 세우므로, 릴리스 빌드에서 이 경로는 원칙적으로 안 닿는다. 실제로 닿는
곳은 셋이다 — 에디터 플레이 모드(훅이 안 돈다), 훅을 우회한 커스텀 빌드 파이프라인, 그리고 빌드
이후 누군가 `Resources` 자산을 들어낸 경우. 완료 기준이 이 실패 결과를 요구하는 이유는 그 셋에서
`scan_all_scenes`가 조용히 빈 `ALL_SCENES`를 보내지 않게 하기 위함이다. 특히 에디터 플레이 모드는
개발 중 상시 경로라 무시할 수 없다.

### 파일 시각 비교의 한계

`File.GetLastWriteTimeUtc`는 체크아웃·리베이스로도 바뀌고, 반대로 씬을 되돌려도 갱신된다. 해시가
더 정확하지만 씬 파일 전체를 매 빌드 해싱하는 비용을 이슈가 요구하지 않았다. mtime으로 간다.

### 부작용 없는 경로가 잃는 것

지도는 씬 에셋에 **직렬화된** 상태다. `Start`가 채우는 텍스트, 런타임에 계산되는 `[ArtelState]`
값은 인스펙터 값으로 읽힌다. 이건 부작용 제거의 대가이고, `live`가 그 대가를 물릴 탈출구다.

## Approach (Checklist)

- [ ] **Step 0: Recon** — 완료. 위 Context가 결과다.

- [ ] **Step 0.5: `.meta` 파일** — 이 환경에 Unity가 없으므로 임포터가 `.meta`를 만들어 주지
  않는다. 신규 파일마다 손으로 쓴다. 복원하는 두 파일(`Artel.Editor.asmdef`,
  `ArtelSceneMapExporter.cs`)은 `1049b7d^`의 `.meta`를 GUID까지 그대로 되살려 이전 참조를 깬 적이
  없게 한다. 신규 파일은 기존 포맷(`MonoImporter` / `AssemblyDefinitionImporter` /
  `folderAsset: yes`)에 새 32자리 hex GUID를 붙인다. GUID 충돌 여부는 커밋 전에 리포지토리 전체에서
  확인한다.

- [ ] **Step 1: 지도 문서 타입과 공유 상수** (Runtime)
  - `Runtime/Protocol/Dto/SceneMapDocumentDto.cs` 신규 — `formatVersion`(int),
    `scenes`(`List<ScannedSceneDto>`). 파일 전용이라 `type`/`id`가 없다.
  - `Runtime/SceneMap.cs` 신규 — `FormatVersion`(=1), `ResourceName`("ArtelSceneMap"),
    `AssetPath`("Assets/Resources/ArtelSceneMap.json") 상수.
    `TryParse(string json, out List<ScannedSceneDto> scenes, out string error)` — 순수 함수.
    빈 문자열·깨진 JSON·버전 불일치를 각각 다른 `error` 문장으로 떨어뜨린다.
    `TryLoad(out …)` — `Resources.Load<TextAsset>(ResourceName)` 후 `TryParse`. Unity에 닿는 건
    이 얇은 층뿐이다.
  - Editor 어셈블리가 `Artel.Runtime`을 참조하므로 상수·DTO·`TryParse`를 양쪽이 공유한다.

- [ ] **Step 2: 런타임 분기** (`Runtime/ArtelManager.cs`)
  - `TryReadScanOptions`를 `TryReadScanMode`로 바꾼다. 반환: `Map` 또는 `Live`.
  - `Map`: `SceneMap.TryLoad`로 `List<ScannedSceneDto>`를 얻어 그대로 `SendAllScenes`에 넘긴다.
    씬 로드 없음, 코루틴 양보 없음. 실패하면 `SendAllScenes`를 부르지 않고
    `ActionResultDto.Failure(action.Id, error)` — 빈 `ALL_SCENES`를 보내는 것보다 낫다.
  - `Live`: 기존 `allSceneScanner.ScanAll(SceneScanOptions.Full, …)` 경로 그대로.
  - `SendAllScenes(List<ScannedSceneDto>)` 시그니처는 그대로. 두 경로가 같은 출구를 쓰고
    `type`/`id`는 계속 여기서 붙는다.

- [ ] **Step 3: 에디터 익스포터** (`Editor/`)
  - `Artel.Editor.asmdef` 복원(1049b7d의 것 그대로).
  - `ArtelSceneMapExporter.cs` — `1049b7d`의 구조를 기반으로:
    `EditorSceneManager.OpenScene(path, Single)` 순회, `GetSceneManagerSetup` /
    `RestoreSceneManagerSetup`으로 원래 씬 구성 복구, `EditorUtility.DisplayProgressBar`.
    바뀌는 부분 — `SaveFilePanel` 대신 `SceneMap.AssetPath` 고정 경로에 쓰고,
    `Directory.CreateDirectory`로 `Assets/Resources`를 확보한 뒤 `File.WriteAllText`,
    끝나고 `AssetDatabase.Refresh()`로 임포트를 태운다.
    스캔 옵션은 `SceneScanOptions.Full`, 문서는 `SceneMapDocumentDto`.
  - `AssemblyInfo.cs`에 `InternalsVisibleTo("Artel.Editor")` 추가 — `SceneScanner`,
    `SceneScanOptions`, `SceneMap`이 전부 `internal`이다.

- [ ] **Step 4: 빌드 전처리 훅** (`Editor/`)
  - `ArtelSceneMapFreshness.cs` — 순수 판정 함수. 입력: 지도 존재 여부·지도 mtime·지도가 담은
    씬 경로 목록·현재 빌드 씬의 (경로, mtime) 목록. 출력: 통과 또는 사람이 읽을 실패 사유.
  - `ArtelSceneMapBuildCheck.cs` — `IPreprocessBuildWithReport`. 값을 모아 위 함수에 넘기고,
    실패면 `BuildFailedException`.

- [ ] **Step 5: 테스트 페이지** (`Runtime/ArtelTestPage.cs`)
  - `Scan all scenes (full)` 버튼을 `Scan all scenes (live)`로 바꾼다. 기본 버튼은 지도를 읽는다.
  - `WebSocketTransportTests`의 마크업 계약 테스트를 따라 고친다.

- [ ] **Step 6: 테스트**
  - `Tests/Editor/Artel.Editor.Tests.asmdef` 신규. `Artel.Editor`는 `autoReferenced: false`라
    **명시 참조**가 필요하고, `includePlatforms: ["Editor"]`와
    `optionalUnityReferences: ["TestAssemblies"]`를 `Artel.Runtime.Tests`와 같게 맞춘다.
    `Editor/` 아래에 `AssemblyInfo.cs`를 두고 `InternalsVisibleTo("Artel.Editor.Tests")`.
  - `SceneMapFreshnessTests`(Editor 테스트) — 신선한 지도 통과, 없는 지도 실패,
    `formatVersion` 불일치 실패, 씬이 더 새로우면 실패, 씬 목록·순서가 다르면 실패,
    경계값(씬 mtime == 지도 mtime)은 통과.
  - `SceneMapTests`(Runtime 테스트) — `SceneMapDocumentDto`를 직렬화해 `TryParse`로 되읽어
    `buildIndex`/`path`/`scene`이 살아 돌아오는지, 빈 문자열·깨진 JSON·다른 `formatVersion`이
    각각 실패로 떨어지는지.
  - `SceneJsonContractTests`에 지도 문서 계약(`formatVersion`/`scenes` 키) 확인 추가.

- [ ] **Step 7: 문서**
  - `Packages/kr.artel.sdk/README.md`의 "Scanning every scene in the build" 절을 다시 쓴다:
    지도가 기본, `live`가 탈출구, 익스포트 메뉴, 빌드 훅이 세우는 조건, 지도가 못 담는 것.

- [ ] **Step 8: Rollout / Rollback**
  - 롤아웃 순서가 있다. 이 SDK를 쓰는 프로젝트는 **업데이트 후 첫 빌드 전에** 익스포트를 한 번
    돌려야 한다. 안 하면 빌드가 선다. README와 PR 본문에 명시한다.

## Validation

- **Commands to run:** Unity Test Runner (EditMode) — `project.md`의 throwaway 프로젝트 절차.
  새 `Artel.Editor.Tests`가 포함되도록 `testables`에 패키지가 들어가 있어야 한다.
- **Expected output:** 신규 테스트 포함 전체 통과. `project.md`가 적어 둔 환경성 실패 8건은
  merge-base 기준선과 비교해 판정한다.
- **자동화가 못 덮는 것 (미리 밝혀 둔다):**
  - `EditorSceneManager.OpenScene` 순회와 씬 구성 복구 — 실제 Unity 에디터와 빌드 씬이 있는
    프로젝트에서 수동 확인이 필요하다.
  - `IPreprocessBuildWithReport`가 실제 빌드에서 발화하는지 — 실제 빌드가 필요하다.
  - `Resources.Load<TextAsset>` 경로 — 실제 `Resources` 폴더가 있는 프로젝트가 필요하다.
  - **이 작업 환경에 Unity가 없다.** 위 셋은 이번 PR에서 실행하지 못한다. 순수 로직(신선도 판정,
    지도 파싱, 파라미터 문법)만 자동 검증된다.

## Risks & Rollback

- **첫 빌드가 선다.** 지도 없이 이 SDK를 넣은 프로젝트는 익스포트를 돌리기 전까지 빌드를 못 한다.
  이슈가 요구한 동작이고, 지도 없는 빌드는 `scan_all_scenes`가 답할 수 없는 빌드라 의도된 것이다.
  실패 메시지가 정확히 무엇을 해야 하는지 말하는 것이 유일한 완화책이다.
- **지도는 직렬화 상태다.** `Start`가 채우는 값이 없다. `live`가 탈출구지만, 그걸 쓰는 순간
  부작용도 함께 돌아온다.
- **mtime 오탐.** 체크아웃·리베이스가 씬 파일 시각을 갱신하면 멀쩡한 지도도 stale로 잡힌다.
  재익스포트로 풀리지만 성가시다.
- **프리팹 변경 미탐.** 씬이 참조하는 프리팹만 바뀌면 검사를 통과한다. 지도가 조용히 낡는다.
- **지도 크기.** `Full` 상세도 × 전체 씬이 빌드에 동봉된다. 큰 프로젝트에서 무시 못 할 용량이고,
  같은 문서가 소켓으로도 나간다.
- **`rect`가 기기 좌표가 아니다.** 위 "지도의 좌표는 에디터 해상도 기준이다" 참조. 읽는 쪽이
  지도의 `rect`를 조준에 쓰면 틀린다.
- **`formatVersion`을 손으로 올려야 한다.** 스캔 DTO를 바꾸면서 상수를 안 올리면 검사가 통과하고
  지도가 조용히 낡는다. 사람이 지키는 규약이라는 한계가 있다.
- **Rollback steps:** `git revert`. 되돌리면 `scan_all_scenes`가 다시 런타임 순회로 돌아가고,
  빌드 훅이 사라져 지도 없는 빌드가 다시 통과한다. 남아 있는 `Assets/Resources/ArtelSceneMap.json`은
  아무도 읽지 않는 파일이 될 뿐 해롭지 않다.

## Rejected feedback

- **"신선도 판정 함수를 `Artel.Runtime`에 두고 기존 `Artel.Runtime.Tests`로 덮어라. 새 asmdef를
  안 만들어도 된다."** 기각. 빌드 타임 규칙이 기기로 실려 나간다. 크기는 사소하지만 어셈블리
  경계가 곧 "언제 도는 코드인가"의 선언이고, 그 선을 테스트 편의로 흐리면 다음 사람이 런타임
  코드에서 빌드 규칙을 만난다. `Tests/Editor/`는 Unity 표준 배치라 새로 배울 것도 없다.
- **"씬 mtime 대신 해시를 써라."** 기각. 매 빌드마다 전체 씬 파일을 해싱하는 비용을 이슈가
  요구하지 않았고, mtime 오탐의 대가는 "재익스포트 한 번"이다. 오탐이 실제로 성가시면 별도 이슈.
- **"`live`에도 상세도 파라미터를 남겨라(`["live", "full"]`)."** 기각. 파라미터 문법을 문자열
  하나로 유지하는 값이 크고, 기본 상세도 런타임 순회의 유일한 이점(페이로드 크기)을 원하는
  호출자가 아직 없다.

## Open Questions

- `SceneScanReporter`(등록 시점 보고)도 지도를 읽어야 하나. 이슈의 완료 기준은 `scan_all_scenes`만
  말한다. 등록은 세션 시작 시점이라 부작용 위험이 낮아 이번 범위 밖으로 둔다. 지도가 자리를 잡으면
  별도 이슈로 다룰 값이 있다.
- 익스포트를 CI에서 배치 모드로 돌려 사람 손을 빼는 방향. 빌드 콜백 안 씬 열기와는 다른 문제라
  별도 이슈로 열 수 있다.
