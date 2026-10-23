# WebGL player에서 SDK 실행부 제외

## 문제
GyeMong #489의 Unity 2022.3.34f1 WebGL release 빌드가 SDK e0e05b1의
Unity.WebRTC 참조로 CS0234/CS0246 오류를 낸다.

## 변경
- WebGL player에서 Artel.Runtime 및 이를 참조하는 player 테스트 fixture를 제외한다.
- WebGL을 선택한 Editor와 다른 player는 기존 경로를 유지한다.
- 입력 ILPostProcessor가 WebGL player에 Artel.Runtime 참조를 주입하지 않게 한다.
- Editor CodeGen은 GameFunctionKind와 CheatParamType을 사용하므로 Runtime 참조를 유지한다.
- WebGL SDK 연결 구현, GyeMong 코드 및 PackageCache 수정은 하지 않는다.

## 검증
- 별도 Unity 테스트 프로젝트에서 CodeGen 회귀 테스트를 실행한다.
- CI 테스트 프로젝트 템플릿을 복사하고 패키지를 embed한 뒤 `.github/webgl-smoke/`
  내용을 `Assets/`로 복사한다.
- Unity `-batchmode -nographics -buildTarget WebGL -executeMethod ArtelWebGlSmoke.Build`
  로 release 빌드를 확인하고 `-artelDevelopment`로 development 빌드도 확인한다.
- 실제 GyeMong 프로젝트 빌드는 원래 작업 채팅에서 패키지 커밋 적용 후 확인한다.

## 제한
WebGL에서 SDK runtime 타입을 직접 참조하는 게임 코드는 동일한 컴파일 조건이 필요하다.
별도 affordance 메타데이터·scan 어셈블리는 제외하지 않는다.
