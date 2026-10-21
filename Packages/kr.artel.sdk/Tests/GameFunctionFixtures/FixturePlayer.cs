using System;
using System.Collections;
using UnityEngine;

namespace Artel.Tests.GameFunctions
{
    /// <summary>instance cheat 와 behavior 를 단 fixture. 대상 규칙과 lifecycle 테스트가 이 객체를 여럿 만든다.</summary>
    public sealed class FixturePlayer : MonoBehaviour
    {
        public int Health = 10;
        public bool InBattle = true;
        public int Hits;
        public int FinallyRuns;

        [ArtelCheat("Restores health.")]
        private string Heal(int amount = 5)
        {
            Health += amount;
            return "health=" + Health;
        }

        [ArtelBehavior("Attacks every frame while in battle.", group: "combat", when: nameof(CanFight))]
        private IEnumerator AutoAttack([CheatRange(1, 20)] float range, ArtelBehaviorContext context)
        {
            try
            {
                while (true)
                {
                    Hits++;
                    context.Report("hits=" + Hits);
                    yield return null;
                }
            }
            finally
            {
                FinallyRuns++;
            }
        }

        private bool CanFight() => InBattle;

        [ArtelBehavior("Strikes for the given frames, then completes.", group: "combat")]
        private IEnumerator Strike(int frames, ArtelBehaviorContext context)
        {
            for (var frame = 0; frame < frames; frame++)
            {
                yield return null;
            }

            context.Complete("struck " + frames + " times");
        }

        [ArtelBehavior("Throws on its second step.")]
        private IEnumerator Stumble()
        {
            yield return null;
            throw new InvalidOperationException("tripped");
        }
    }
}
