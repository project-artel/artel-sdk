using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("Artel.Runtime.Tests")]

// The scene map exporter and the build check both read SceneMap, and the exporter drives
// SceneScanner directly — all of it internal to the runtime assembly.
[assembly: InternalsVisibleTo("Artel.Editor")]
[assembly: InternalsVisibleTo("Artel.Editor.Tests")]

// A second test assembly because Unity does not run Awake, OnEnable, or DontDestroyOnLoad outside
// play mode. Anything that drives a live component — the pointer dispatcher, the cursor, the action
// queue on a real manager — can only be exercised there.
[assembly: InternalsVisibleTo("Artel.Runtime.PlayModeTests")]
