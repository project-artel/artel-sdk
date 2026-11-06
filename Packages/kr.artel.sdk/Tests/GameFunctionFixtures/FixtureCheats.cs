using System;
using System.Collections;
using UnityEngine;

namespace Artel.Tests.GameFunctions
{
    public enum FixtureSpawn
    {
        Start,
        Boss
    }

    /// <summary>
    /// Unity 가 이 어셈블리를 컴파일할 때 <c>GameFunctionWeaver</c> 가 실제로 위빙하는 static 함수들.
    /// </summary>
    /// <remarks>
    /// 손으로 만든 Cecil 모듈이 아니라 Unity 의 ILPP 를 거친 결과를 보려는 것이다. 메서드를 private 으로 둔 것도 그래서다 —
    /// 만들어진 stub 이 같은 타입 안에 있어 private 메서드를 부를 수 있는지가 여기서 확인된다.
    /// </remarks>
    public static class FixtureCheats
    {
        public static int Gold;
        public static string WalletMissingReason;
        public static int CountFinallyRuns;

        public static void Reset()
        {
            Gold = 0;
            WalletMissingReason = null;
            CountFinallyRuns = 0;
        }

        [ArtelCheat("Adds gold to the wallet.", when: nameof(HasWallet))]
        private static string GiveGold([CheatRange(1, 1000000)] int amount)
        {
            Gold += amount;
            return "gold=" + Gold;
        }

        private static string HasWallet() => WalletMissingReason;

        [ArtelCheat("Throws.")]
        private static void Explode()
        {
            throw new InvalidOperationException("boom");
        }

        [ArtelCheat("Waits a frame, then says where it went.")]
        private static IEnumerator Travel(FixtureSpawn to = FixtureSpawn.Boss)
        {
            yield return null;
            yield return "arrived at " + to;
        }

        [ArtelCheat("Takes one of every parameter type.", name: "every_type")]
        private static string TakesEverything(int count, float speed, bool flag, string label, FixtureSpawn spawn)
        {
            return count + "|" + speed.ToString(System.Globalization.CultureInfo.InvariantCulture) + "|" + flag + "|" +
                   label + "|" + spawn;
        }

        [ArtelBehavior("Counts frames until stopped.")]
        private static IEnumerator Count(ArtelBehaviorContext context)
        {
            var frames = 0;
            try
            {
                while (true)
                {
                    frames++;
                    context.Report("frames=" + frames);
                    yield return null;
                }
            }
            finally
            {
                CountFinallyRuns++;
            }
        }

        [ArtelBehavior("Waits for the given game seconds, then completes.")]
        private static IEnumerator Linger(float seconds)
        {
            yield return new WaitForSeconds(seconds);
        }
    }
}
