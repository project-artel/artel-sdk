# Play Mode 재진입 검증

Unity 2022.3.34f1, Input System 1.7.0, Both 설정에서 실행했다.
`ArtelInputLifecycleCheck.cs`를 테스트 프로젝트의 `Assets/Editor/`에 복사한 뒤 다음 명령을 실행한다. `-quit`는 붙이지 않는다. 검증이 종료 상태를 직접 반환한다.

```text
Unity -batchmode -nographics -projectPath <test-project> -executeMethod ArtelInputLifecycleCheck.Run -logFile <log-path>
```

이 검증은 Test Runner와 별도 Editor 프로세스에서 실행한다. domain reload를 끈 상태로 Play Mode를 두 번 시작하고, 매번 위치와 클릭 전달, 장치 1개 생성, 종료 후 장치와 lifetime GameObject 및 복제 InputSettings 제거, 원본 settings 복원을 확인한다. 결과는 프로젝트 루트의 `lifecycle-check-result.txt`에 기록한다. 실행 뒤 복사한 script와 meta를 지운다.

첨부한 결과는 위 검증의 통과 기록이다. 전체 QA 서버 통합이나 Player build를 검증한 것은 아니다.
