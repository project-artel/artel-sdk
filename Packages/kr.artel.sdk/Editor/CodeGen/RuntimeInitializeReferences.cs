using System.Linq;
using Mono.Cecil;

namespace Artel.CodeGen
{
    /// <summary>
    /// 생성한 등록 메서드에 붙일 <c>[RuntimeInitializeOnLoadMethod(BeforeSceneLoad)]</c> 의 참조. cheat 등록과 입력 등록이 같이 쓴다.
    /// </summary>
    internal static class RuntimeInitializeReferences
    {
        /// <summary><c>UnityEngine.RuntimeInitializeLoadType.BeforeSceneLoad</c>.</summary>
        internal const int BeforeSceneLoad = 1;

        /// <summary>
        /// <c>UnityEngine.CoreModule</c> 을 <c>Artel.Runtime</c> 의 참조로 찾는다. 대상 assembly 자신은 UnityEngine 을 참조하지
        /// 않을 수도 있다. 찾지 못하면 false 다.
        /// </summary>
        internal static bool TryImport(
            ModuleDefinition module, ModuleDefinition runtimeModule, out MethodReference constructor, out TypeReference loadType)
        {
            constructor = null;
            loadType = null;
            var unityReference = runtimeModule.AssemblyReferences.FirstOrDefault(reference =>
                reference.Name == "UnityEngine.CoreModule");
            if (unityReference == null)
            {
                return false;
            }

            ModuleDefinition unityModule;
            try
            {
                unityModule = module.AssemblyResolver.Resolve(unityReference)?.MainModule;
            }
            catch (AssemblyResolutionException)
            {
                return false;
            }

            var attribute = unityModule?.GetType("UnityEngine.RuntimeInitializeOnLoadMethodAttribute");
            var attributeConstructor = attribute?.Methods.FirstOrDefault(method =>
                method.IsConstructor && method.Parameters.Count == 1);
            var loadTypeDefinition = unityModule?.GetType("UnityEngine.RuntimeInitializeLoadType");
            if (attributeConstructor == null || loadTypeDefinition == null)
            {
                return false;
            }

            constructor = module.ImportReference(attributeConstructor);
            loadType = module.ImportReference(loadTypeDefinition);
            return true;
        }
    }
}
