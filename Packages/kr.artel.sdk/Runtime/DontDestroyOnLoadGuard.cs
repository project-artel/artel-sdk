using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Artel
{
    /// <summary>
    /// Remembers which objects were in the DontDestroyOnLoad scene before a scene walk, so the
    /// ones a walked scene puts there can be cleared away once it is unloaded.
    /// </summary>
    internal sealed class DontDestroyOnLoadGuard
    {
        private readonly HashSet<int> preexisting = new HashSet<int>();
        private Scene scene;

        public void Capture()
        {
            scene = ResolveScene();
            preexisting.Clear();
            foreach (var root in scene.GetRootGameObjects())
            {
                preexisting.Add(root.GetInstanceID());
            }
        }

        /// <summary>
        /// Destroys every DontDestroyOnLoad root that was not there at <see cref="Capture"/> time.
        /// Returns how many were destroyed. Destruction is deferred to the end of the frame, so
        /// callers walking more scenes should wait a frame before scanning again.
        /// </summary>
        public int DestroyNewcomers()
        {
            if (!scene.IsValid())
            {
                return 0;
            }

            var destroyed = 0;
            foreach (var root in scene.GetRootGameObjects())
            {
                if (preexisting.Contains(root.GetInstanceID()))
                {
                    continue;
                }

                Object.Destroy(root);
                destroyed++;
            }

            return destroyed;
        }

        private static Scene ResolveScene()
        {
            // Unity hands out no reference to the DontDestroyOnLoad scene. Moving a throwaway
            // object into it is the only way to read the handle back, and it doubles as a way to
            // make the scene exist at all — it does not until something is put there.
            var probe = new GameObject("Artel DontDestroyOnLoad Probe");
            Object.DontDestroyOnLoad(probe);
            var resolved = probe.scene;
            Object.DestroyImmediate(probe);
            return resolved;
        }
    }
}
