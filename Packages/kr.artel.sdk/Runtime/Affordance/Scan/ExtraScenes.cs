using System;
using System.Collections;
using System.Collections.Generic;

namespace Artel.Affordances.Scan
{
    /// <summary>
    /// Scenes the build settings do not know about.
    /// </summary>
    /// <remarks>
    /// A walk goes by build index, and a project that loads its scenes by address has no reason to
    /// put them there — measured on Chop Chop, one scene was registered and fifty were on disk, so
    /// the walk visited one and reported a game with nothing in it.
    ///
    /// Filled in from the outside rather than asked for from here. Addressables is a package a
    /// project may not have, and this assembly cannot reference something that may not exist; the
    /// assembly that can compiles only when it does and hands its answer over. Left null, everything
    /// behaves exactly as it did.
    /// </remarks>
    public static class ExtraScenes
    {
        /// <summary>Every scene reachable by address, named the way <see cref="Load"/> wants them.</summary>
        public static Func<List<string>> List;

        /// <summary>Brings one of them up on its own, as a coroutine to be driven to completion.</summary>
        public static Func<string, IEnumerator> Load;

        internal static bool Available => List != null && Load != null;
    }
}
