# 2026-10-23 — ARTEL 패널에 선택 프로젝트 표시 추가

- Date: 2026-10-23
- Jira: None
- Status: Implemented

## Goal
ARTEL 패널에서 현재 선택한 프로젝트 이름을 확인한다. 사용자가 패널 안 표시를 선택했다.

## Non-goals
접힌 패널 밖 상시 표시, 프로젝트 전환 UI, 추가 API 호출은 하지 않는다.

## Context / Constraints
현재 세션은 서버별 project ID만 저장하며 재실행 시 목록을 조회하지 않는다. 이름은 선택 시 목록에서 가져와 같은 서버 범위에 저장한다. 이름이 없으면 ID를 표시하고 선택 전에는 미선택을 표시한다. 이름은 마지막 선택 시점의 값이며 서버 이름 변경 자동 갱신은 범위 밖이다.

## Approach (Checklist)
- [x] **Step 0: Recon** ViewModel 선택/초기화/로그아웃과 서버별 세션 저장, 패널 배치 확인.
- [x] **Step 1: Implementation** ArtelSdkSession.SaveProjectId에 선택적 이름 인자를 추가하고 ID와 이름을 함께 저장한다. 이름 미지정 시 이전 이름을 지운다. ArtelOwnedPlayerPrefs 목록과 Clear에 이름 키를 포함한다. ViewModel에서 이름을 복원하고 선택·만료·404 시 갱신/초기화한다. 패널 제목 아래 2줄 높이 프로젝트 라벨을 넣고 상태·고급 섹션을 아래로 이동한다. RichText 해석을 끄고 긴 이름은 제한 영역 안에 표시한다.
- [x] **Step 2: Tests** 이름 선택과 재실행 복원, ID fallback, 다른 ID 저장 시 이름 제거, 로그아웃과 서버별 격리, SDK prefs 보존 확인. UI에서 미선택/선택, 테마 변경과 긴 이름 배치를 확인하고 라이트·다크 실제 Unity 화면을 저장한다. 전체 EditMode/PlayMode 및 GitHub CI 실행.
- [x] **Step 3: Rollout / Rollback** 기존 PR #152 브랜치에 별도 커밋. 필요하면 이 커밋만 revert한다.

## Validation
- **Commands to run:** CI 템플릿 기반 임시 프로젝트에서 Unity 2022.3.34f1 -batchmode -runTests -testPlatform EditMode/PlayMode. git diff --check.
- **Expected output:** 전체 테스트 통과, 프로젝트 표시와 기존 상태/고급 컨트롤 겹침 없음. 화면은 fixture이며 실제 서버 연동을 증명하지 않는다.

## Risks & Rollback
- **Risks:** 저장된 이름은 서버에서 이름을 바꾸면 다음 선택 전까지 이전 이름이다. 기존 세션 및 이름 없는 프로젝트는 ID fallback을 사용한다.
- **Rollback steps:** 이 기능 커밋만 revert. 새 이름 키는 인증에 영향을 주지 않는다.

## Open Questions
없음.
- 계획 검토: FAST/MEDIUM/HEAVY PASS. 404에서는 ClearProject로 저장된 ID와 이름도 함께 삭제한다.

- 구현 검토: pair-review VERDICT PASS. Unity 2022.3.34f1 전체 EditMode 556/556, PlayMode 69/69, 화면 fixture 1/1 통과. 일반/긴 이름의 라이트·다크 패널 PNG 4개를 실제 Unity 렌더링으로 확인했다. fixture는 프로젝트와 연결 상태를 주입했으며 서버 연동 검증은 아니다.
