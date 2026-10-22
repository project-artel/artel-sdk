# 2026-10-22 — SDK 로그인 취소와 프로젝트 목록 스크롤 추가

- Date: 2026-10-22
- Jira: None
- Status: Implemented

## Goal
브라우저 로그인 대기를 취소하고 로그인 화면으로 돌아간다. 4개를 넘는 프로젝트도 스크롤하여 선택한다.

## Non-goals
인증 API, 토큰 저장 방식, 기존 테마는 변경하지 않는다.

## Context / Constraints
Unity uGUI 오버레이의 기존 변경 사항을 유지한다. Hindsight는 DNS 오류로 조회할 수 없었다.

## Approach (Checklist)
- [x] **Step 0: Recon** 로그인 코루틴과 4개 목록 제한 확인.
- [x] **Step 1: Implementation** 대기 취소, loopback 정리, ScrollRect와 전체 목록 추가.
- [x] **Step 2: Tests** 취소 후 재시도와 마지막 프로젝트 스크롤 확인.
- [x] **Step 3: Rollout / Rollback** 패키지 변경으로 적용하며 이번 변경만 되돌릴 수 있도록 유지.

## Validation
- **Commands to run:** Unity 2022.3.34f1 EditMode / PlayMode 관련 테스트.
- **Expected output:** 취소 시 토큰 요청 없이 로그인 가능 상태로 복귀. 4개 이후 항목도 표시되고 스크롤 가능.
- **Results:** Unity 2022.3.34f1에서 `-testPlatform EditMode -testFilter Artel.Tests.Auth.SdkLoginTests` 19/19 통과. `-testPlatform PlayMode -testFilter Artel.Tests.OverlayGuiBootstrapTests` 9/9 통과. `git diff --check` 통과.
- **PR validation:** 최신 `develop` (`e0e05b1`) 위에서 EditMode 19/19, PlayMode 9/9를 다시 통과했다. 임시 화면 캡처 fixture 1개도 통과했으며 로그인 대기, 취소 후 로그인, 두 테마의 프로젝트 목록 처음과 끝을 렌더링했다. 이미지는 `.plan/assets/2026-10-22-sdk-login-cancel-project-scroll/`에 저장했다.
- **Review:** pair-review-critic 결과 `VERDICT: PASS`.
- **Remaining:** 화면 이미지는 상태를 주입한 Unity UI fixture이며 실제 브라우저 인증과 서버 연동을 증명하지 않는다. 활성 listener의 포트 해제와 실제 마우스 휠·드래그 입력은 end-to-end로 검증하지 않았다.

## Risks & Rollback
- **Risks:** 취소 시점과 콜백 도착이 겹쳐도 토큰 교환으로 진행하지 않아야 한다. 목록이 다른 컨트롤을 가리지 않아야 한다.
- **Rollback steps:** 이번 변경분만 되돌리고 기존 작업은 유지한다.

## Open Questions
없음.
