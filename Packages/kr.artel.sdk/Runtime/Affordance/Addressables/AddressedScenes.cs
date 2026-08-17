using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.ResourceProviders;
using UnityEngine.SceneManagement;

namespace Artel.Affordances.Scan
{
    /// <summary>
    /// Tells the walk about the scenes that live at addresses.
    /// </summary>
    /// <remarks>
    /// This assembly is compiled only when the project has Addressables. The asmdef turns
    /// <c>ARTEL_ADDRESSABLES</c> on from the package's own presence and then requires it, so a
    /// project without the package builds exactly what it built before and this file does not
    /// exist as far as the compiler is concerned. That is the whole reason it is an assembly of its
    /// own rather than an <c>#if</c> somewhere.
    ///
    /// The catalogue is asked, not the disk. A built player has no asset database and no folder of
    /// scenes; what it has is the locators the game itself uses to find them, which is the same list
    /// by construction.
    /// </remarks>
    internal static class AddressedScenes
    {
        /// <summary>How long one addressed scene may take before the walk gives up on it.</summary>
        private const float Patience = 30f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Install()
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            ExtraScenes.List = List;
            ExtraScenes.Load = Load;
#endif
        }

        /// <summary>
        /// Every address that resolves to a scene.
        /// </summary>
        /// <remarks>
        /// A locator answers to several keys for the same asset — its address, its guid, each of its
        /// labels. Guids are dropped because they are not what anybody wrote, and a label would
        /// bring up whichever of its members the resolver picked. What is left is the address, which
        /// is the name the game itself uses.
        /// </remarks>
        private static List<string> List()
        {
            var found = new List<string>();
            var seen = new HashSet<string>();

            foreach (var locator in Addressables.ResourceLocators)
            {
                if (locator?.Keys == null)
                {
                    continue;
                }

                foreach (var key in locator.Keys)
                {
                    if (!(key is string address) || IsGuid(address) || !seen.Add(address))
                    {
                        continue;
                    }

                    if (locator.Locate(key, typeof(SceneInstance), out var locations) &&
                        locations != null && locations.Count == 1)
                    {
                        // Exactly one, because a key answering with several is a label rather than
                        // an address and loading it would bring up whichever came first.
                        found.Add(address);
                    }
                }
            }

            found.Sort(System.StringComparer.Ordinal);
            return found;
        }

        private static IEnumerator Load(string address)
        {
            var loading = Addressables.LoadSceneAsync(address, LoadSceneMode.Single);
            var waited = 0f;

            while (!loading.IsDone)
            {
                waited += Time.unscaledDeltaTime;

                if (waited > Patience)
                {
                    Debug.LogWarning("[Artel] " + address + " did not finish loading in " +
                                     Patience + "s. Moving on.");
                    yield break;
                }

                yield return null;
            }
        }

        private static bool IsGuid(string key)
        {
            if (key.Length != 32)
            {
                return false;
            }

            foreach (var character in key)
            {
                var hex = (character >= '0' && character <= '9') ||
                          (character >= 'a' && character <= 'f') ||
                          (character >= 'A' && character <= 'F');

                if (!hex)
                {
                    return false;
                }
            }

            return true;
        }
    }
}
