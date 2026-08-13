using System.Collections.Generic;

namespace Artel.Affordances.Live
{
    /// <summary>
    /// Keeps a continuous value still until it has actually gone somewhere.
    /// </summary>
    /// <remarks>
    /// The change gate compares whole readings, so any value that never repeats opens it every beat
    /// and the gate stops meaning anything — a reader given every reading has to work out for itself
    /// which of them were news, which is the job the gate exists to do.
    ///
    /// Positions are the values that behave that way. A field holding a number the game recomputes,
    /// or an object a physics solver keeps nudging, differs in its last decimal place forever while
    /// sitting exactly where it was.
    ///
    /// This is rounding whose grid is anchored where the value last stopped, rather than at zero. A
    /// value that has not travelled far enough to be worth a word reads back as the number already
    /// sent, so the reading matches and the gate stays shut; once it has, the new number is taken
    /// and becomes the next anchor. Nothing accumulates: a slow drift crosses the bound eventually
    /// and is reported, because a thing that has moved a long way slowly has still moved.
    ///
    /// It does not — and must not — hold still a value that is genuinely travelling. A map cursor
    /// sliding to the next stage opens the gate on every beat it is moving, and that is the news.
    /// What bounds the traffic there is the beat itself, which is the difference from an SDK that
    /// hashes a whole scene every frame.
    /// </remarks>
    internal sealed class Restless
    {
        /// <summary>
        /// How far a coordinate must travel before it is worth saying.
        /// </summary>
        /// <remarks>
        /// A guess, and said to be one. It is in the game's own world units, which no package can
        /// know the scale of — a millimetre in one project is a screen's width in another. Chosen
        /// small enough that nothing a specification compares could hide under it, since what the
        /// evidence does with positions is ask whether one object has arrived where another is, and
        /// two objects assigned from each other are exactly equal rather than nearly.
        ///
        /// Whether it is right is measurable rather than arguable: a run whose readings almost all
        /// go out has a value moving for reasons no condition mentions, and the reading says which
        /// one it was.
        /// </remarks>
        private const float Bound = 0.001f;

        private readonly Dictionary<string, float> _standing = new Dictionary<string, float>();

        /// <summary>
        /// The number to write: the one already sent when nothing has happened, else the new one.
        /// </summary>
        internal float Settle(string key, float now)
        {
            if (_standing.TryGetValue(key, out var standing))
            {
                if (now >= standing - Bound && now <= standing + Bound)
                {
                    return standing;
                }
            }

            _standing[key] = now;
            return now;
        }

        internal void Forget()
        {
            _standing.Clear();
        }
    }
}
