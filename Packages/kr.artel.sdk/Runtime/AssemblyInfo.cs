using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("Artel.Runtime.Tests")]

// A second test assembly because Unity does not run Awake, OnEnable, or DontDestroyOnLoad outside
// play mode. Anything that drives a live component — the pointer dispatcher, the cursor, the action
// queue on a real manager — can only be exercised there.
[assembly: InternalsVisibleTo("Artel.Runtime.PlayModeTests")]

// GameFunctionWeaver 테스트가 만들어진 등록 코드를 직접 로드해 돌린 뒤, 그 결과를 GameFunctionRegistry 에서 읽는다.
[assembly: InternalsVisibleTo("Artel.CodeGen.Tests")]
