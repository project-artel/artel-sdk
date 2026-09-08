using System;

namespace Artel.CodeGen
{
    /// <summary>
    /// 어느 어셈블리를 위버에 넘길지 이름만 보고 정한다.
    /// </summary>
    /// <remarks>
    /// <c>AffordanceILPostProcessor</c> 의 같은 목록과 짝이다. 두 CodeGen asmdef 이 갈라져 있어
    /// 공유하지 못하고 <c>CompiledAssemblyResolver</c> 처럼 각자 들고 있다.
    ///
    /// 별도 타입인 것은 <c>ILPostProcessor</c> 를 상속하지 않기 위해서다. 그래야
    /// <c>Unity.CompilationPipeline.Common</c> 을 참조할 수 없는 테스트에서도 이 판단을 부를 수
    /// 있다 — 그 어셈블리는 이름이 <c>*.CodeGen</c> 인 어셈블리에만 열려 있다.
    /// </remarks>
    internal static class WeavableAssemblies
    {
        /// <summary>
        /// 이름이 정확히 같을 때만 거른다.
        /// </summary>
        /// <remarks>
        /// <c>Artel.Runtime</c> 은 <c>ArtelInput</c> 을 담고 있고, 그 프록시는 스스로
        /// <c>UnityEngine.Input</c> 을 부른다. 걸러 내지 않으면 프록시가 자기를 부르게 된다.
        ///
        /// 접두어가 아니라 정확한 이름인 것이 중요하다. <c>Artel.Runtime.Tests</c> 와
        /// <c>Artel.Runtime.PlayModeTests</c> 는 위빙 대상이어야 한다 — 위빙이 실제로
        /// 일어났는지 확인하는 테스트들이 제 어셈블리가 갈아 끼워진 것을 보고 판정한다.
        /// 접두어로 거르면 그 테스트들이 통째로 무의미해진다.
        /// </remarks>
        private static readonly string[] SkippedNames = { "Artel.Runtime" };

        /// <summary>
        /// 엔진과 시스템 어셈블리. 게임이 아니다.
        /// </summary>
        /// <remarks>
        /// <c>UnityEngine.UI</c> 의 입력 모듈도 <c>Input</c> 을 읽는다. 게임이 아니라 엔진의
        /// 입력 경로를 갈아 끼우는 것은 이 위버가 할 일이 아니다.
        ///
        /// <c>Unity</c> 가 <c>Unity.Artel.CodeGen</c> 을 덮으므로 위버 자신도 여기서 걸린다.
        ///
        /// 넓은 접두어의 대가는 제 어셈블리를 그렇게 이름 지은 게임이 조용히 지나쳐진다는
        /// 것이다. 그 거절은 <c>WillProcess</c> 에서 일어나 스스로를 보고할 자리가 없다 —
        /// <c>Process</c> 가 한 번도 불리지 않기 때문이다. 그래서 여기서 묻는 것은 사람이 이미
        /// 답을 아는 것뿐이다: 자기 어셈블리를 무엇이라 이름 지었는지.
        /// </remarks>
        private static readonly string[] SkippedPrefixes =
        {
            "UnityEngine", "UnityEditor", "Unity", "System", "mscorlib", "netstandard",
            "nunit", "Newtonsoft", "Mono"
        };

        /// <summary>
        /// 접두어 맞추기는 이름 경계에서 일어난다. <c>Unity</c> 는 <c>Unity.Artel.CodeGen</c> 을
        /// 덮고, 그저 그 글자로 시작하기만 하는 <c>UnityLike</c> 는 덮지 않는다.
        /// </summary>
        internal static bool IsSkipped(string assemblyName)
        {
            if (string.IsNullOrEmpty(assemblyName))
            {
                return true;
            }

            foreach (var name in SkippedNames)
            {
                if (string.Equals(assemblyName, name, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            foreach (var prefix in SkippedPrefixes)
            {
                if (!assemblyName.StartsWith(prefix, StringComparison.Ordinal))
                {
                    continue;
                }

                if (assemblyName.Length == prefix.Length || assemblyName[prefix.Length] == '.')
                {
                    return true;
                }
            }

            return false;
        }
    }
}
