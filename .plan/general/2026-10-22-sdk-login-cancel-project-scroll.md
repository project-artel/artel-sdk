# 2026-10-22 — SDK 로그인 취소와 프로젝트 목록 스크롤 추가

- Date: 2026-10-22
- Jira: None
- Status: Implemented

## Goal
브라우저 로그인 대기를 취소하고 로그인 화면으로 돌아간다. 4개를 넘는 프로젝트도 스크롤하여 선택한다.

## Non-goals
인증 API와 토큰 저장 방식은 변경하지 않는다. 저장된 테마 선호는 유지한다.

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

## 브라우저 스타일 적용
- 웹 콘솔의 크림색 표면, 얇은 테두리와 코랄 버튼을 SDK 로그인·대기·프로젝트 선택 카드에 적용했다.
- 프로젝트 행에 첫 글자 아이콘과 스크롤바를 추가하고, 기본 테마를 라이트로 변경했다.
- CanvasRenderer가 없어 표시되지 않던 로고를 수정했다. 토글 라벨 클릭을 유지하고 CanvasScaler의 Expand 설정으로 카드가 화면 높이를 넘지 않게 했다.
- Unity 2022.3.34f1에서 라이트 로그인 화면, 로고, 검증용 프로젝트 10개, 마우스 휠 및 스크롤바 드래그를 직접 확인했다.
- 재디자인 후 PlayMode OverlayGuiBootstrapTests 9/9 통과. 재검토 VERDICT: PASS. git diff --check 통과.
- 마지막 토글·scaling 보완 후 수동 검증은 Esc 입력으로 중단됐다. 다크 테마와 실제 브라우저 로그인 취소의 최종 수동 검증은 남아 있다. 기존 PNG는 재디자인 이전 fixture 화면이다.
- 실행 중 스크립트 reload 과정에서 ArtelManager.RecordFrameTime 및 StopTransport의 NullReferenceException이 기록됐다. 새 세션 재실행 검증은 중단되어 이 조건의 확인이 남아 있다.
