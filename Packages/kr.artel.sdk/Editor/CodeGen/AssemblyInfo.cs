using System.Runtime.CompilerServices;

// 위버는 internal 이다. 테스트는 Cecil 로 만든 모듈을 직접 먹여 대상 선택을 확인한다 —
// ILPP 진입점을 부르려면 Unity.CompilationPipeline.Common 이 필요한데, 그 어셈블리는
// 이름이 `*.CodeGen` 인 어셈블리에만 열려 있어 테스트 어셈블리가 참조할 수 없다.
[assembly: InternalsVisibleTo("Artel.CodeGen.Tests")]
