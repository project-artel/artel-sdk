# SDK 오버레이 OnGUI 전환

- Date: 2026-11-06
- Jira: None
- Status: Draft

## Goal

Editor와 네이티브 플레이어에서 ARTEL UI를 OnGUI로 표시하고, 입력 영역에 겹치는 게임 uGUI 버튼의 클릭을 막는다.

## Non-goals

WebGL 플레이어 지원과 게임 입력 코드 수정은 범위 밖이다.

## Context / Constraints

로그인 게이트, 프로젝트 선택, 서버 주소 입력, 연결 상태, 가상 커서와 키보드 상태가 모두 기존 흐름대로 작동해야 한다. QA 캡처에서 숨기는 상태와 신 Input System 전용 프로젝트의 마우스 조작을 보존한다.

## Approach (Checklist)

- [x] **Step 0: Recon** 기존 Canvas 구조, 입력 모듈과 캡처 경로 확인
- [ ] **Step 1: Implementation** SDK UI의 표시를 OnGUI로 이동하고 SDK 입력 영역에 투명 Canvas 적용
- [ ] **Step 2: Tests** Match3 IMGUI 중첩, uGUI 버튼 차단과 패널 밖 클릭, 기존 SDK 입력과 캡처 회귀 검증
- [ ] **Step 3: Rollout / Rollback** SDK develop 대상 draft PR 작성

## Validation

- **Commands to run:** Unity Test Runner EditMode와 PlayMode, Windows Standalone 빌드와 실행
- **Expected output:** 게임 UI 위에서 ARTEL 표시 확인. ARTEL 영역의 클릭은 뒤쪽 uGUI 버튼에 닿지 않고 바깥 클릭은 전달된다.

## Risks & Rollback

- **Risks:** IMGUI 그리기 순서와 uGUI 레이캐스트 순서는 따로 검증한다.
- **Rollback steps:** PR 커밋을 revert한다.

## Open Questions

- Editor와 Windows 플레이어에서 같은 순서로 합성되는지 검증한다.
