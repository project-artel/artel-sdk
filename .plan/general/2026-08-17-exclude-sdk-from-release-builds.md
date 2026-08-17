# 2026-08-17 — 릴리스 빌드에서 SDK 코드 제외

- Date: 2026-08-17
- Jira: ARTEL-363
- Status: Done

## Goal

Editor와 Development Build에서만 SDK 런타임 어셈블리(`Artel.Runtime`)가 컴파일되게
한다. 릴리스 빌드 산출물에는 SDK 런타임 코드와 그 전용 의존성이 남지 않는다.
`[ArtelAction]` / `[ArtelState]`를 쓰는 게임 코드는 릴리스 빌드에서도 조건부 컴파일
없이 그대로 컴파일된다.

## Non-goals

- 난독화·코드 스트리핑 수준의 보호
- 모바일 플랫폼 대응
- `Unity.WebRTC` / `Newtonsoft.Json` 패키지 의존성 자체 제거 (Editor·Dev에서 필요)

## Context / Constraints

- `Packages/kr.artel.sdk/Runtime/Artel.Runtime.asmdef`의 `defineConstraints`가 비어 있어
  어셈블리가 무조건 컴파일된다.
- 어트리뷰트 두 개(`Runtime/Tracking/ArtelActionAttribute.cs`,
  `ArtelStateAttribute.cs`)가 같은 어셈블리에 있어, 어셈블리를 통째로 빼면 사용자
  게임 코드가 릴리스에서 타입을 못 찾는다.
- 위빙은 `Editor/CodeGen/ArtelILPostProcessor.cs`가 한다. 게임 어셈블리 IL에
  `IArtelActionSource`, `ActionInvocationBuffer`, `ArtelActionRecorder`, `ArtelInput`
  호출을 심는다. 이 타입들이 없는 릴리스 빌드에서는 위빙이 반드시 꺼져야 한다.
- ARTEL-383이 세운 불변식: "어트리뷰트 타입이 있는 어셈블리에 대한 IL 참조가 없으면
  어트리뷰트도 없다." 어트리뷰트를 옮기면 이 불변식의 기준 어셈블리도 같이 옮겨진다.
- Unity 2022.3 `defineConstraints`는 `||` 연산자를 지원한다
  (2022.3 매뉴얼 *Assembly Definition properties*: "You can use the `||` (OR) operator
  to specify that at least one of the constraints must be present"). 이슈가 요구한
  선행 확인 사항이며, 별도 심볼 조합은 필요 없다.
- `samples/WordVenture`는 서브모듈이다. 이 이슈에서 샘플 소스는 건드리지 않는다.

## 선결 결정 — 어트리뷰트를 어떻게 남길 것인가

**어트리뷰트만 담은 최소 어셈블리 `Artel.Attributes`를 항상 컴파일한다.** 이슈가
유력하다고 본 방향이고, 다른 후보(게임 쪽 `#if` 요구)는 Constraints의 "사용자 게임
코드에 조건부 컴파일을 강요하지 않는다"와 정면으로 충돌한다.

- 네임스페이스는 `Artel.Tracking` 그대로 둔다. 사용자 코드도 SDK 코드도 `using`이
  바뀌지 않는다. 어셈블리 경계만 달라진다.
- 어트리뷰트는 메타데이터일 뿐이라 릴리스에 남아도 동작하지 않고, 참조 어셈블리도
  없다(엔진 참조조차 필요 없다).

## Approach (Checklist)

- [x] **Step 0: Recon** — 완료. 위 Context 참고.
- [x] **Step 1: 어트리뷰트 어셈블리 분리**
  - `git mv`로 `Runtime/Tracking/ArtelActionAttribute.cs`,
    `ArtelStateAttribute.cs`(+ `.meta`)를 `Runtime/Attributes/`로 옮긴다. GUID 유지가
    목적이므로 `.meta`를 반드시 같이 옮긴다.
  - `Runtime/Attributes/Artel.Attributes.asmdef` 신규:
    `noEngineReferences: true`, `autoReferenced: true`, 참조·제약 없음.
  - `Artel.Runtime.asmdef`: `references`에 `Artel.Attributes` 추가,
    `defineConstraints`에 `UNITY_EDITOR || DEVELOPMENT_BUILD` 추가.
  - `Tests/Fixtures/Artel.Tracking.Fixtures.asmdef`에 `Artel.Attributes` 참조 추가
    (fixture가 어트리뷰트를 단다).
- [x] **Step 2: 위버가 릴리스에서 스스로 꺼지게 한다**
  - `ArtelILPostProcessor.WillProcess`: 대상 판정 기준을
    "`Artel.Runtime` 또는 `Artel.Attributes`를 참조" 로 넓히고, 두 SDK 어셈블리 자신과
    `Unity.Artel.CodeGen`은 계속 제외한다.
  - `Process`: 런타임 모듈을 이름으로 resolve 한다. resolve 실패 = 릴리스 빌드 =
    위빙할 타입이 없다 → 조용히 no-op.
  - 단, `compiledAssembly.Defines`에 `UNITY_EDITOR`나 `DEVELOPMENT_BUILD`가 있는데
    런타임을 못 찾았고 `[ArtelAction]`이 실제로 붙어 있다면, 그건 릴리스가 아니라
    asmdef가 `Artel.Runtime`을 참조하지 않은 설정 실수다. 조용히 넘어가면 추적이
    말없이 죽으므로 진단 에러를 낸다.
  - `ActionMethodWeaver.TryCreate` / `InputMethodWeaver.TryCreate`의 "런타임 참조가
    있는가" 검사는 `Artel.Attributes` 기준으로 옮긴다. 어트리뷰트가 그쪽으로 갔으므로
    ARTEL-383의 불변식이 성립하는 어셈블리도 그쪽이다. 주입 대상 타입은 인자로 받은
    런타임 모듈에서 그대로 가져온다.
- [x] **Step 3: 플러그인·의존성**
  - `Runtime/Plugins/websocket-sharp.dll.meta`의 `PluginImporter.defineConstraints`에
    같은 제약을 넣어 릴리스 빌드에서 DLL이 빠지게 한다.
  - `Unity.WebRTC`, `Newtonsoft.Json`은 패키지 의존성으로 남는다. 릴리스에서 참조하는
    어셈블리가 사라지므로 산출물 포함 여부는 빌드로 확인하고 결과를 문서에 적는다.
- [x] **Step 4: 테스트**
  - `Tests/Runtime/ActionTrackingTests.cs`에 어트리뷰트가 `Artel.Attributes`에서 온다는
    회귀 가드 1건 추가. 이 분리가 깨지면 릴리스 컴파일이 깨지는데, 그건 EditMode에서
    잡을 수 있는 유일한 지점이다.
  - 기존 ILPP 테스트(`ActionTrackingTests`, `VirtualKeyboardStateTests` 등)가 분리 후에도
    통과해야 한다 — fixture 어셈블리가 위빙 경로를 그대로 탄다.
- [x] **Step 5: 문서**
  - `README.md`에 "릴리스 빌드에서 제외되는지 확인하는 방법" 절 추가:
    빌드 후 `<Build>_Data/Managed/`에 `Artel.Runtime.dll`이 없고
    `Artel.Attributes.dll`만 있는지 확인.
  - `.agents/docs/project.md`의 아키텍처 항목에 어셈블리 두 개 구조를 적는다.

## Validation

- **Commands to run:**
  - EditMode/PlayMode: `.github/scripts/setup-unity-test-project.sh <dest>` 후
    Unity `-runTests` (project.md 절차 그대로), `summarize-test-results.py`로 판정.
  - 릴리스 빌드: `samples/WordVenture`를 Windows Standalone으로 릴리스/개발 각각 빌드,
    `<Build>_Data/Managed/*.dll` 목록 비교.
- **Expected output:**
  - 릴리스: `Artel.Runtime.dll` 없음, `websocket-sharp.dll` 없음,
    `Artel.Attributes.dll` 있음, `Assembly-CSharp.dll` 컴파일 성공.
  - 개발 빌드/Editor: 기존과 동일하게 `Artel.Runtime.dll` 포함, 액션 추적 동작.
- **Not verified here:** macOS 빌드(장비 없음). PR에 명시한다.

### 실제 결과 (2026-08-17, Unity 2022.3.34f1 / Windows)

- EditMode 263 passed · 0 failed, PlayMode 14 passed · 0 failed
  (`setup-unity-test-project.sh`로 만든 throwaway 프로젝트).
- 같은 프로젝트에 `[ArtelAction]`/`[ArtelState]`와 `UnityEngine.Input`을 쓰는 게임
  스크립트를 넣고 StandaloneWindows64로 릴리스·개발 빌드:
  - 릴리스 `probe_Data/Managed`: `Artel.Attributes.dll`만 있고 `Artel.Runtime.dll`,
    `websocket-sharp.dll` 없음. `Assembly-CSharp.dll`의 문자열에도 `Artel.Attributes`만
    남고 `ArtelActionRecorder` / `ArtelInput` / `__artelActionBuffer`가 없다 —
    위빙이 실제로 꺼졌다.
  - 개발 빌드: `Artel.Runtime.dll`, `websocket-sharp.dll` 포함,
    `Assembly-CSharp.dll`에 위빙 흔적 그대로.
- `Unity.WebRTC.dll`은 릴리스 산출물에 남는다. 참조하는 어셈블리가 사라져도 패키지
  의존성이라 어셈블리 자체는 실린다 — 아래 Open Questions 참고.

## Risks & Rollback

- **Risks:**
  - 제약이 걸린 어셈블리를 참조하는 다른 asmdef(`Artel.CodeGen`, 테스트, fixture)가
    릴리스 컴파일에서 어떻게 처리되는지가 Unity 동작에 달렸다. Editor 전용/테스트
    전용이라 플레이어 빌드에 안 들어가는 것이 전제 — 실제 릴리스 빌드로 확인한다.
  - `PluginImporter.defineConstraints`의 `||` 지원이 asmdef와 다르면 개발 빌드에서
    `websocket-sharp`가 빠져 컴파일이 깨진다. 개발 빌드로 확인한다.
  - 커스텀 asmdef를 쓰는 사용자가 `Artel.Attributes`만 참조하면 위빙 대상에서 빠진다.
    Step 2의 진단 에러가 이 경우를 소리 나게 만든다.
- **Rollback steps:** 단일 커밋 `git revert`. 어셈블리 분리·제약 모두 되돌아간다.

## Open Questions

- `Unity.WebRTC.dll`은 릴리스 산출물에 남는 것으로 확인됐다. `package.json`이 패키지를
  의존성으로 선언하는 한 참조가 없어도 어셈블리는 실린다. 빼려면 의존성을 optional로
  돌리거나 스트리핑 설정을 손봐야 하므로 이 이슈의 AC(SDK 런타임 코드 제외) 밖이다.
  별도 이슈로 다룬다.
