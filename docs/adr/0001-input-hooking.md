# ADR 0001 — Input 후킹을 ILPP build-time rewriting 으로 한다

- 상태: 확정
- 근거: Notion `3b30bce5-474c-81a6-85a6-d9ef2a009e7a`, `Packages/kr.artel.sdk/Editor/CodeGen/ArtelILPostProcessor.cs`, `Packages/kr.artel.sdk/Editor/CodeGen/InputMethodWeaver.cs`, `Packages/kr.artel.sdk/Editor/CodeGen/WeavableAssemblies.cs`, 커밋 `646ce57`, `96f497f`

## 결정

- 게임이 부르는 `UnityEngine.Input` 호출을 빌드 시점에 `Artel.ArtelInput` 호출로 갈아 끼웁니다.
  - `ILPostProcessor` 인 `ArtelILPostProcessor` 가 Unity 컴파일 파이프라인 안에서 돕니다.
  - `InputMethodWeaver` 가 메서드 참조만 바꾸므로 인자 모양도 메서드 이름도 그대로입니다.
- 갈아 끼우는 멤버는 14 개입니다: `GetKeyDown`, `GetKey`, `GetKeyUp`, `get_anyKey`, `get_anyKeyDown`,
  `get_mousePosition`, `GetMouseButton`, `GetMouseButtonDown`, `GetMouseButtonUp`, `GetAxis`,
  `GetAxisRaw`, `GetButton`, `GetButtonDown`, `GetButtonUp`.
- 게임 팀이 하는 일은 패키지 설치 하나입니다. 코드 수정도, attribute 도, 자기 빌드 단계도 없습니다.
  - 커밋 `646ce57`(`feat: 패키지만 넣으면 붙는다`) 이 서버 주소 기본값까지 채워 그 약속을 완성했고,
    이 ADR 의 결정이 그 약속이 성립하는 이유입니다.

## 왜

- 아무도 고치지 않는 게임에서 `GetKeyDown`·`GetKey`·`GetKeyUp` 을 가로챌 수 있는 시점은 빌드 시점뿐입니다.
  - 그 셋은 정적 메서드 호출이라 런타임에 끼어들 이음매가 없습니다.
- `ILPostProcessor` 는 IL2CPP 가 변환하기 전에 돕니다. 그래서 최종 빌드 형식이 Mono 든 IL2CPP 든 결과가
  똑같이 거기 있습니다.

### 위빙 대상을 고르는 기준

- 예전에는 `module.AssemblyReferences` 에 `Artel.Runtime` 이 있는지 물었고, 그 물음이 틀렸다는 것이
  ARTEL-383 이며 커밋 `96f497f` 가 고쳤습니다.
  - `Input.GetKey` 호출은 Artel 타입을 하나도 쓰지 않으므로 참조를 남기지 않습니다.
  - 그래서 SDK 타입을 한 번도 쓰지 않은 게임은 입력 위빙이 통째로 건너뛰어졌고 진단조차 남지 않았습니다.
  - TrashDash 가 그 상태였습니다. content map 은 화살표 키를 정확히 읽어 냈고 `ACTION_RESULT` 는 전부
    성공이었는데 게임은 반응하지 않았습니다.
- 지금은 `module.GetTypeReferences()` 에 `UnityEngine.Input` 이 있는지를 봅니다.
  - 참조는 위빙의 전제가 아니라 결과입니다 — `ImportReference` 가 첫 교체와 함께 만들어 줍니다.

### skip 목록

| 거르는 것 | 어떻게 맞추나 | 왜 |
| --- | --- | --- |
| `Artel.Runtime` | 정확한 이름 하나 (`WeavableAssemblies.SkippedNames`) | `ArtelInput` 이 스스로 `UnityEngine.Input` 을 부르므로, 거르지 않으면 proxy 가 자기를 부름 |
| `UnityEngine`, `UnityEditor`, `Unity`, `System`, `mscorlib`, `netstandard`, `nunit`, `Newtonsoft`, `Mono` | 접두어 목록, 이름 경계에서 맞춤 | 엔진과 시스템 어셈블리 |

- `Artel.Runtime` 을 접두어로 거르면 `Artel.Runtime.Tests` 와 `Artel.Runtime.PlayModeTests` 까지 걸립니다.
  - 그 둘은 위빙 대상이어야 합니다. 위빙이 실제로 일어났는지 확인하는 테스트들이 제 어셈블리가 갈아
    끼워진 것을 보고 판정하기 때문입니다.
  - 접두어로 거르면 그 테스트가 아무것도 증명하지 못합니다.
- 맞추기가 이름 경계에서 일어나므로 `Unity` 는 `Unity.Artel.CodeGen` 을 덮고 `UnityLike` 는 덮지 않습니다.

## 거절한 대안

| 대안 | 왜 거절했나 |
| --- | --- |
| `Artel.UnityEngine.Input` namespace shim | 게임이 `using` 을 바꿔야 함. "패키지만 넣으면 붙는다" 가 깨짐 |
| 게임이 대고 코딩하는 Artel 입력 추상화 계층 | 이미 나온 게임과 이미 쓰인 코드에는 적용 불가. QA 대상은 대부분 그런 게임 |

## 대가

| 대가 | 무엇이 일어나나 |
| --- | --- |
| Unity New Input System 미지원 | `UnityEngine.InputSystem` 은 대상이 아니고 legacy Input Manager 만 갈아 끼움 |
| `WillProcess` 의 거절은 알려지지 않음 | 자기 어셈블리에 `Unity` 로 시작하는 이름을 붙인 게임은 조용히 지나쳐짐 |
| `Artel.Runtime` 을 못 찾은 어셈블리 | `UnityEngine.Input` 호출이 있어도 경고 진단만 남기고 건너뜀. ILPP 경고는 빌드가 성공하면 콘솔에 안 뜨고 `Editor.log` 에만 남음 |
| `ILPostProcessor` 둘이 같은 게임 어셈블리를 건드림 | 순서 의존성. [ADR 0005](0005-affordance-analyser-gate.md) 가 다룸 |

- `WillProcess` 에서 내린 거절이 알려지지 않는 이유는 `Process` 가 한 번도 불리지 않고, 진단을 쥐고
  있는 것이 `Process` 이기 때문입니다.
  - 그래서 `WillProcess` 에서 묻는 것은 사람이 이미 답을 아는 것 하나뿐입니다 — 자기 어셈블리를
    무엇이라 이름 지었는지.
