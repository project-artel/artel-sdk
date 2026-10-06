namespace Artel.Tracking
{
    /// <summary>
    /// How much of a scene a scan is allowed to see.
    /// </summary>
    /// <remarks>
    /// <see cref="Default"/> is what <c>GAME_STATE</c> runs: no field values at all, active objects
    /// only. <see cref="Full"/> is the discovery mode behind <c>scan_all_scenes ["full"]</c> —
    /// every serialized field of the game's own behaviours, and inactive objects too. Nothing on
    /// the live-play path uses it.
    /// </remarks>
    internal readonly struct SceneScanOptions
    {
        public static readonly SceneScanOptions Default = new SceneScanOptions(false, false, false);
        public static readonly SceneScanOptions Full = new SceneScanOptions(true, true, true);

        /// <summary>
        /// Include every MonoBehaviour the game itself wrote, reading the fields Unity would
        /// serialize. Without it a scan reports no field values, because the default scan has read
        /// none since <c>[ArtelState]</c> was removed (ARTEL-400).
        /// </summary>
        public bool IncludeAllSerializedFields { get; }

        /// <summary>
        /// Walk into objects that are inactive. Their blocks carry <c>active: false</c>.
        /// </summary>
        public bool IncludeInactive { get; }

        /// <summary>
        /// Read each button's inspector-wired <c>onClick</c> calls into its component. A scene the
        /// walk visited is unloaded before anyone can click it, so this is all the reader gets to
        /// learn about what a button does.
        /// </summary>
        public bool IncludeButtonHandlers { get; }

        private SceneScanOptions(
            bool includeAllSerializedFields,
            bool includeInactive,
            bool includeButtonHandlers)
        {
            IncludeAllSerializedFields = includeAllSerializedFields;
            IncludeInactive = includeInactive;
            IncludeButtonHandlers = includeButtonHandlers;
        }
    }
}
