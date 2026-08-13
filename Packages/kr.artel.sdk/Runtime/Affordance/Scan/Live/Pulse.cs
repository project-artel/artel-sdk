using System;
using System.Collections;
using UnityEngine;

namespace Artel.Affordances.Live
{
    /// <summary>Where a changed reading goes.</summary>
    /// <remarks>
    /// A seam rather than a socket. This package hand-writes its JSON so that a game shipping it
    /// takes on no serialisation dependency, and putting a transport in here would undo that for
    /// every game whether or not it wants one. What arrives is a finished document; carrying it is
    /// somebody else's decision.
    /// </remarks>
    public interface IPulseSink
    {
        void Send(string document);
    }

    /// <summary>
    /// Reads the watched members on a beat and delivers when the answer has changed.
    /// </summary>
    /// <remarks>
    /// A specification cannot be run against a report written before the game started. The evidence
    /// says what has to be true and what will change; only the running game says what is true now,
    /// and this is the channel that carries it.
    ///
    /// Sent on change rather than on the beat. The state a game is in is mostly the state it was in
    /// a frame ago, and a reader given the same document sixty times a second has to work out for
    /// itself which of them mattered.
    ///
    /// What decides is the list of values that moved, not a digest of the document. A digest was
    /// tried first and was wrong in a way only a run showed: the document carries which reading it
    /// is and which frame it was taken on, both of which differ every time, so every reading looked
    /// new and the gate never once shut. Measured on the sample game, thirty-three of thirty-three
    /// readings went out and twenty-two of them said in their own text that nothing had changed.
    /// Comparing the values themselves cannot drift from what the reading claims, because it is
    /// the same comparison the reading publishes.
    ///
    /// What makes the gate work is upstream of it. Hashing an unbounded scene dump would report a
    /// change every frame — a breathing idle animation is enough — which is why the other SDK's
    /// read-everything mode was never usable while playing. This hashes the members the evidence
    /// actually names, so a value moving means a condition somewhere may now read differently.
    ///
    /// Started deliberately and never on its own. Reading a hundred fields on a beat is a cost a
    /// game should agree to, and a package that began polling the moment it was installed would be
    /// spending it on projects that only ever wanted the report.
    /// </remarks>
    public sealed class Pulse : MonoBehaviour
    {
        /// <summary>
        /// One: a scene name, the statics, and the objects carrying watched members.
        /// </summary>
        /// <remarks>
        /// Separate from the report's own version. They are read by different code at different
        /// moments and neither has an opinion about the other's shape — a reader of this has no use
        /// for records, and a reader of the report cannot poll.
        /// </remarks>
        /// <remarks>
        /// Two, since the objects stopped being one list. They are sorted into <c>active</c> and
        /// <c>deactive</c> and no longer carry a flag saying which — the same fact in one place
        /// instead of two. A reader written against one shape cannot read the other, so the number
        /// moves rather than leaving that to be discovered.
        /// </remarks>
        internal const int SchemaVersion = 2;

        /// <summary>Seconds between readings.</summary>
        /// <remarks>
        /// Ten a second. Fast enough that a change arrives while a tester is still looking at what
        /// caused it, and slow enough that the reading itself is not what the profiler finds. The
        /// gate is what keeps the traffic down, not this.
        /// </remarks>
        private const float DefaultInterval = 0.1f;

        private static Pulse _beating;

        private IPulseSink _sink;
        private float _interval = DefaultInterval;
        private bool _read;

        /// <summary>Whether the reading before this one failed to reach the sink.</summary>
        private bool _lost;

        /// <summary>
        /// Which reading this is, counted from the moment watching began.
        /// </summary>
        /// <remarks>
        /// Counted over every reading taken rather than every one sent, so a gap in the numbers is
        /// itself the news: it says the state held still across that stretch, which a reader
        /// otherwise has to infer from two timestamps and a guess about the interval.
        /// </remarks>
        private long _reading;

        /// <summary>Coordinates that have not gone anywhere yet, and what they last said.</summary>
        private readonly Restless _restless = new Restless();

        /// <summary>What the reading before this one said, so this one can name the difference.</summary>
        private readonly System.Collections.Generic.Dictionary<string, string> _since =
            new System.Collections.Generic.Dictionary<string, string>(System.StringComparer.Ordinal);

        internal static bool InProgress => _beating != null;

        /// <summary>How many readings have gone out since this started.</summary>
        internal static int Sent { get; private set; }

        /// <summary>How many readings were taken and found unchanged.</summary>
        /// <remarks>
        /// Said because the two numbers together are what shows the gate is doing anything. A game
        /// whose every reading goes out has a member in its watch list that moves for reasons no
        /// condition mentions, and that is a thing to go and look at rather than to tune around.
        /// </remarks>
        internal static int Held { get; private set; }

        /// <summary>Begins reading, or answers false because it is already going.</summary>
        internal static bool Begin(IPulseSink sink, float interval = DefaultInterval)
        {
            if (_beating != null || sink == null || interval <= 0f)
            {
                return false;
            }

            var carrier = new GameObject("Artel Pulse") { hideFlags = HideFlags.HideAndDontSave };
            DontDestroyOnLoad(carrier);

            _beating = carrier.AddComponent<Pulse>();
            _beating._sink = sink;
            _beating._interval = interval;
            Sent = 0;
            Held = 0;

            _beating.StartCoroutine(_beating.Beat());
            return true;
        }

        internal static void Stop()
        {
            if (_beating == null)
            {
                return;
            }

            var carrier = _beating.gameObject;

            _beating._sink = null;
            _beating = null;
            Destroy(carrier);
        }

        private IEnumerator Beat()
        {
            // The first reading always goes out. Nothing has been said yet, so "unchanged" is not
            // a claim this can make about it.
            while (_beating == this)
            {
                Take();
                yield return new WaitForSecondsRealtime(_interval);
            }
        }

        private void Take()
        {
            string document;
            var settled = false;

            try
            {
                // The carrier was made to outlive scene loads, so the scene holding it is the one
                // Unity keeps everything else that outlives them in. It is the only handle on that
                // scene a package which installs itself has, and the scan's own walk takes it the
                // same way.
                document = LiveState.Compose(
                    ++_reading, gameObject.scene, _restless, _since, _lost, out settled);
            }
            catch (Exception exception)
            {
                // One bad reading is a reading to skip, not a reason to stop watching. A field that
                // throws is already reported as unread inside the document; this is the case where
                // the walk itself came apart, which a scene being torn down can cause.
                Debug.LogWarning("[Artel] A reading could not be taken: " + exception.Message);
                return;
            }

            if (_read && settled)
            {
                Held++;
                return;
            }

            _read = true;

            try
            {
                _sink.Send(document);
            }
            catch (Exception exception)
            {
                // The reading stands whether or not it arrived. Forgetting the hash would send the
                // same document again next beat and go on doing so for as long as the sink is
                // unhappy, which is the shape that turns one broken socket into a flood.
                //
                // The next one goes whole instead. Readings carry only what moved, so a lost one is
                // a difference nobody will hear again — a reader stays wrong about those values
                // until something moves them, which may be never. One full reading repairs that and
                // then the differences resume, which is not the flood resending would be.
                _lost = true;
                Debug.LogWarning("[Artel] A reading could not be delivered: " + exception.Message);
                return;
            }

            _lost = false;
            Sent++;
        }

        private void OnDestroy()
        {
            if (_beating == this)
            {
                _beating = null;
            }
        }
    }
}
