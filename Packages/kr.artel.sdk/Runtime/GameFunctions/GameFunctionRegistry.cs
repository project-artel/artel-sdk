#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;

namespace Artel
{
    /// <summary>
    /// 이 프로세스에 등록된 cheat 와 behavior. 이름은 종류마다 하나씩이다.
    /// </summary>
    /// <remarks>
    /// weaver 가 만든 등록은 <c>RuntimeInitializeOnLoadMethod</c> 에서 <see cref="Shared"/> 에 들어오므로, 정적 자리 하나가
    /// 필요하다. 테스트는 <see cref="Replace"/> 로 그 자리를 잠시 빌린다.
    ///
    /// 같은 이름이 두 번 오면 먼저 온 것이 남는다. 한 어셈블리 안의 중복은 weaver 가 컴파일 오류로 막고, 이것은 어셈블리
    /// 둘이 같은 이름을 쓰거나 런타임 등록이 attribute 이름과 겹친 경우다.
    /// </remarks>
    internal sealed class GameFunctionRegistry
    {
        private static readonly Regex NamePattern = new Regex("^[a-z][a-z0-9_]*$");

        internal static GameFunctionRegistry Shared { get; private set; } = new GameFunctionRegistry();

        /// <summary>
        /// 플레이 모드마다 빈 registry 로 시작한다. weaver 가 만든 <c>BeforeSceneLoad</c> 등록보다 먼저 돈다.
        /// </summary>
        /// <remarks>
        /// domain reload 를 끈 Enter Play Mode 에서는 정적 필드가 지난 플레이를 넘어 산다. 비우지 않으면 두 번째 플레이의
        /// 모든 등록이 "already registered" 로 거절되고, 지난 플레이에서 <c>ArtelCheats.Register</c> 한 handler 가 이미
        /// 파괴된 객체를 쥔 채 남는다.
        /// </remarks>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetForPlay()
        {
            Shared = new GameFunctionRegistry();
        }

        private readonly Dictionary<string, GameFunction> cheats = new Dictionary<string, GameFunction>(StringComparer.Ordinal);
        private readonly Dictionary<string, GameFunction> behaviors = new Dictionary<string, GameFunction>(StringComparer.Ordinal);

        /// <summary>프로세스마다 1 에서 시작하고, 실제로 더하거나 뺄 때마다 하나 오른다.</summary>
        public long Revision { get; private set; } = 1;

        public IEnumerable<GameFunction> Cheats => cheats.Values;

        public IEnumerable<GameFunction> Behaviors => behaviors.Values;

        public bool HasBehaviors => behaviors.Count > 0;

        public bool TryAdd(GameFunction function, out string error)
        {
            if (function == null || string.IsNullOrEmpty(function.Name) || !NamePattern.IsMatch(function.Name))
            {
                error = "Game function name must match ^[a-z][a-z0-9_]*$: " + function?.Name;
                return false;
            }

            var table = TableFor(function.Kind);
            if (table.ContainsKey(function.Name))
            {
                error = "A " + KindWord(function.Kind) + " named " + function.Name + " is already registered.";
                return false;
            }

            table[function.Name] = function;
            Revision++;
            error = null;
            return true;
        }

        public bool Remove(GameFunctionKind kind, string name)
        {
            if (name == null || !TableFor(kind).Remove(name))
            {
                return false;
            }

            Revision++;
            return true;
        }

        public bool TryGet(GameFunctionKind kind, string name, out GameFunction function)
        {
            function = null;
            return name != null && TableFor(kind).TryGetValue(name, out function);
        }

        /// <summary>테스트가 등록 자리를 잠시 바꾼다. 돌려받은 것을 Dispose 하면 원래 것이 돌아온다.</summary>
        internal static IDisposable Replace(GameFunctionRegistry replacement)
        {
            var previous = Shared;
            Shared = replacement ?? throw new ArgumentNullException(nameof(replacement));
            return new Restore(previous);
        }

        internal static string KindWord(GameFunctionKind kind) => kind == GameFunctionKind.Cheat ? "cheat" : "behavior";

        private Dictionary<string, GameFunction> TableFor(GameFunctionKind kind) =>
            kind == GameFunctionKind.Cheat ? cheats : behaviors;

        private sealed class Restore : IDisposable
        {
            private GameFunctionRegistry previous;

            public Restore(GameFunctionRegistry previous)
            {
                this.previous = previous;
            }

            public void Dispose()
            {
                if (previous == null)
                {
                    return;
                }

                Shared = previous;
                previous = null;
            }
        }
    }
}
#endif
