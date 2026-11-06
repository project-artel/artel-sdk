# 2026-11-03 — SDK 프로젝트 목록 새로고침 추가

- Date: 2026-11-03
- Jira: ARTEL-1315
- Status: Implemented

## Goal
SDK 프로젝트 선택 화면에서 새로고침으로 서버의 프로젝트 목록을 다시 불러온다. 선택된 프로젝트를 표시하고 서버 설정 접기와 선택창 닫기를 분리한다.

## Non-goals
자동 polling, 로그인 및 프로젝트 자동 선택 정책 변경은 포함하지 않는다.

## Context / Constraints
기존 ReloadProjects와 중복 요청 방지를 재사용한다. 최신 develop의 카드 레이아웃을 기준으로 별도 worktree에서 변경하며 기존 작업 파일은 보존한다.

## Approach (Checklist)
- [x] **Step 0: Recon** 목록이 있을 때 기존 재시도 버튼을 숨기는 조건 확인.
- [x] **Step 1: Implementation** 선택된 프로젝트와 새로고침 버튼을 목록 위에 표시한다. 수동 새로고침은 자동 선택과 자동 등록을 하지 않는다. 고급 설정 표시와 선택창 표시를 분리하고 별도 닫기 버튼을 둔다.
- [x] **Step 2: Tests** 두 테마의 버튼과 선택된 이름, 서버 설정 접기, 선택창 닫기, loopback HTTP 응답 후 기존 선택 유지를 검증했다. Unity PlayMode 13개가 통과했다(저장소 테스트 12개와 임시 캡처 실행 1개).
- [x] **Step 3: Rollout / Rollback** 캡처 5장과 검증 결과를 포함한 draft PR을 develop 대상으로 준비했다. 배포는 포함하지 않는다.

## Validation
- **Commands to run:** Unity PlayMode의 OverlayGuiBootstrapTests 실행, git diff --check.
- **Expected output:** 두 테마에서 목록과 새로고침 버튼이 보이고 로그아웃과 겹치지 않는다.
- **Result:** Unity 2022.3.34f1에서 `-batchmode -runTests -testPlatform PlayMode -testFilter Artel.Tests.OverlayGuiBootstrapTests`를 실행해 13개 통과. 변경 파일의 `git diff --check`도 통과했다.
- **Screenshots:** `.plan/assets/2026-11-03-sdk-project-list-refresh/`의 이미지는 실제 Unity Canvas를 fixture 목록으로 렌더링한 화면이다. 라이트·다크 테마, 서버 설정 표시, 선택창 닫기를 확인했다. 실행 중인 로컬 서버가 없어 실제 서버의 프로젝트 생성·재조회 검증은 실행하지 않았다.
- **Review:** pair-review에서 발견한 단일 프로젝트 응답의 자동 선택 문제와 버튼 수 검증을 수정했으며 재검토 PASS를 받았다.
- **CI 수정:** 최초 실행의 PlayMode 4개 조합은 통과했지만 EditMode 4개 조합에서 `OverlayGui_HidesCoverWhenGateDismissed`가 이전 버튼 이름 `나중에 Button`을 찾아 실패했다. 테스트를 `닫기 Button`으로 수정하고 null 검증을 추가했다. 로컬 EditMode 전체 실행에서 613개 통과, 1개 skip, 실패 0개를 확인했다.

## Risks & Rollback
- **Risks:** 새로고침과 로그아웃의 배치가 기존 목록과 겹칠 수 있다.
- **Rollback steps:** 이번 버튼 표시 및 배치 변경만 되돌린다.

## Open Questions
- 없음.
