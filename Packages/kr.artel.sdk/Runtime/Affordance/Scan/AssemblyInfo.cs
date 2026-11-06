using System.Runtime.CompilerServices;

// 스캔이 문서를 어떻게 적는지는 이 어셈블리 안에서만 뜻이 있는 일이라 진입점이 internal 이다.
// 그런데 그 문서의 모양이 곧 소비자와의 계약이고, 계약을 검증하려면 그 진입점을 불러야 한다.
// 테스트에만 연다.
// 같은 패키지의 런타임도 연다. `ObjectIds` 가 여기 사는 것은 Artel.Runtime 이 이 어셈블리를 참조하고
// 그 반대가 아니어서인데, 정작 그 id 를 쓰는 곳이 양쪽에 있다. 공개로 올려 SDK 의 표면을 넓히는 것보다
// 이쪽이 싸다.
[assembly: InternalsVisibleTo("Artel.Runtime")]

[assembly: InternalsVisibleTo("Artel.Runtime.Tests")]
[assembly: InternalsVisibleTo("Artel.Runtime.PlayModeTests")]
