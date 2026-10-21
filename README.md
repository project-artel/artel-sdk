# artel-sdk

- 도는 게임을 QA agent 가 관측하고 조작하게 해 주는 Unity 패키지
- 씬 읽기, 버튼 클릭과 키 입력, screen capture 업로드, 컴파일된 코드에서 빌드가 할 수 있는 일 읽기
- 에디터와 개발 빌드 전용. 출시 빌드에는 아무것도 컴파일돼 들어가지 않음
- 설치가 곧 통합 — 씬 오브젝트도, 컴포넌트 부착도, 게임 코드 수정도 없음

## Install

- Unity 의 **Window → Package Manager** → **Add package from git URL** 에 아래 주소

```text
https://github.com/project-artel/artel-sdk.git?path=/Packages/kr.artel.sdk
```

- 기본 branch 인 `develop` 을 따라감
- 다른 branch·tag·commit 은 `#<revision>` 을 뒤에 붙임

```text
https://github.com/project-artel/artel-sdk.git?path=/Packages/kr.artel.sdk#develop
```

## 서버 주소 설정

로그인 화면의 **서버 주소**에 HTTP(S) 주소를 입력하고 **적용**을 누른다.
연결 후에는 고급 메뉴의 **서버 변경**에서 바꾼다. 기존 연결을 종료하고 새 서버의 인증 상태를 불러온다.
주소는 origin만 지원한다. 예: `https://artel.example.com`, `http://localhost:8080`.
경로·query·fragment·사용자 정보는 넣지 않는다. Inspector의 `Server URL`로도 설정할 수 있다.

SDK는 console 주소를 받지 않는다. 서버 운영자가 `ARTEL_HOME_URL`에 console 주소를 설정한다.
SDK는 `/api/auth/sdk/login`을 열고 서버는 console의 `/sdk-login`으로 redirect한다.
console 자체의 API 설정도 같은 서버를 가리켜야 한다.

주소 우선순위는 실행 인자 → 화면에서 저장한 주소 → Inspector 설정 → 기본 서버다.
토큰·refresh token·표시 이름·프로젝트·인스턴스·빌드는 서버별로 보관하며,
다른 서버에서 로그아웃해도 원래 서버의 로그인은 남는다. 기존 버전의 서버 정보 없는 토큰은
재사용하지 않으므로 업데이트 후 한 번 다시 로그인해야 한다.

## 실행 인자

| 인자 | 값 | 무엇 |
| --- | --- | --- |
| `-artel-server` | `http://localhost:8080` 같은 HTTP(S) URL | 연결할 서버 주소 |
| `-artel-secure` | `true` / `false` | 이전 `host:port` 인자와 함께 쓰는 호환 옵션 |
| `-artel-project` | project id | 이번 실행이 쓸 프로젝트 |
| `-artel-logout` | 없음 | 저장된 세션을 지움 |
| `-artel-window-label` | 문자열 | 오버레이에 그릴 이번 실행의 라벨 |

SDK 토큰은 인자가 아니라 환경 변수 `ARTEL_SDK_TOKEN` 으로 받음.

| 규칙 | 이유 |
| --- | --- |
| 토큰은 환경 변수로만 받고 인자로는 받지 않음 | 실행 인자는 같은 기계의 다른 사용자가 프로세스 목록에서 그대로 읽음 |
| 명시한 `-artel-server`가 우선 | 저장된 주소와 씬 설정을 덮어씀 |
| 토큰과 프로젝트는 서버별로 저장 | 매니저의 `Awake`에서 실제 서버를 정한 뒤 적용 |
| `-artel-logout` 하나만 준 실행은 세션을 지우고 끝남 | `Application.Quit(0)` |
| 토큰이나 프로젝트를 함께 준 실행은 게임을 이어 감 | 계정을 바꾸러 온 실행 |

## Tests

- 저장소를 체크아웃한 상태로는 패키지 테스트를 돌릴 수 없음
- 로컬 실행과 CI 둘 다 일회용 Unity 프로젝트를 먼저 만듦

```bash
.github/scripts/setup-unity-test-project.sh /tmp/artel-unity-test
```

- `.github/workflows/unity-tests.yml` 이 모든 pull request 와 `develop` push 에 대해 EditMode 와
  PlayMode 를 그 프로젝트에 대고 돌림
- 에디터 명령행 전체와 Unity licence secret 의 출처는 `.agents/docs/project.md`

## 문서

| 문서 | 무엇 |
| --- | --- |
| [`docs/architecture.md`](docs/architecture.md) | subsystem 이 무엇이고 왜 있는지 |
| [`docs/protocol.md`](docs/protocol.md) | action 19 개, 메시지 타입, 소비자가 빠지는 함정 |
| [`docs/adr/`](docs/adr/README.md) | 이 저장소의 모양을 정한 결정 여섯 개 |
| [`Packages/kr.artel.sdk/README.md`](Packages/kr.artel.sdk/README.md) | 패키지 안쪽 — 로컬 테스트 페이지, 스캔 순서, attribute 사용법 |
| [`.plan/general/`](.plan/general/) | 나머지 결정의 이유. 주제별 안내는 ADR 색인에 있음 |
