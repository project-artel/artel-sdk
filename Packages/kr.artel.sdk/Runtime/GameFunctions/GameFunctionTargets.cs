#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections.Generic;
using System.Linq;
using Artel.Affordances.Scan;
using Artel.Pointer;
using UnityEngine;

namespace Artel
{
    /// <summary>
    /// 함수가 무엇 위에서 돌지, 그리고 지금 돌 수 있는지 정한다.
    /// </summary>
    /// <remarks>
    /// 두 물음을 한 자리에 둔 것은 둘이 언제나 같은 순서로 함께 묻히기 때문이다 — 빌드, 대상, 게임 상태(<c>when</c>).
    /// 앞의 것이 거절하면 그 이유가 답이다.
    /// </remarks>
    internal static class GameFunctionTargets
    {
        /// <summary>대상이 여럿일 때 실패 문장에 나열하는 <c>selector</c> 수.</summary>
        public const int MaxListedSelectors = 5;

        internal readonly struct LiveInstance
        {
            public readonly Component Component;
            public readonly string Selector;

            public LiveInstance(Component component, string selector)
            {
                Component = component;
                Selector = selector;
            }
        }

        /// <summary>
        /// 그 component 를 단 활성 객체 전부. <c>selector</c> 순으로 정렬해 매번 같은 순서로 답한다.
        /// </summary>
        /// <remarks>
        /// 활성 씬 밖(<c>DontDestroyOnLoad</c>, additive 씬)의 객체도 포함한다. 그런 객체의 <c>selector</c> 는
        /// <see cref="ScenePathSelectorResolver"/> 가 풀지 못하므로, <see cref="TryResolve"/> 가 이 목록과 먼저 맞춰 본다.
        /// </remarks>
        public static List<LiveInstance> LiveInstances(Type componentType)
        {
            return UnityEngine.Object.FindObjectsOfType(componentType)
                .OfType<Component>()
                .Select(component => new LiveInstance(component, SelectorOf(component)))
                .OrderBy(instance => instance.Selector, StringComparer.Ordinal)
                .ToList();
        }

        /// <summary>스캔과 <c>pulse</c> 가 쓰는 것과 같은 <c>selector</c>.</summary>
        public static string SelectorOf(Component component)
        {
            var transform = component.transform;
            var roots = transform.gameObject.scene.GetRootGameObjects();
            var rootIndex = Array.IndexOf(roots, transform.root.gameObject);
            return ScenePath.SelectorOf(transform, Math.Max(rootIndex, 0));
        }

        /// <summary>
        /// 계약의 대상 규칙을 그대로 따른다. static 이면 대상이 없어야 하고, instance 면 대상을 찾는다.
        /// </summary>
        public static bool TryResolve(
            GameFunction function, object targetValue,
            out Component target, out string selector, out string error)
        {
            target = null;
            selector = null;
            error = null;

            if (targetValue != null && !(targetValue is string))
            {
                error = GameFunctionArguments.BadArgument("target", "must be a selector string");
                return false;
            }

            var requested = (string)targetValue;
            if (function.IsStatic)
            {
                if (!string.IsNullOrEmpty(requested))
                {
                    error = function.Name + " is static and takes no target";
                    return false;
                }

                return true;
            }

            var componentName = function.Component.Name;
            var live = LiveInstances(function.Component);
            if (!string.IsNullOrEmpty(requested))
            {
                return TryResolveSelector(function, requested, live, out target, out selector, out error);
            }

            if (live.Count == 0)
            {
                error = NoActive(componentName);
                return false;
            }

            if (live.Count > 1)
            {
                error = live.Count + " " + componentName + " objects; pass target as one of: " +
                        string.Join(", ", live.Take(MaxListedSelectors).Select(instance => instance.Selector));
                return false;
            }

            target = live[0].Component;
            selector = live[0].Selector;
            return true;
        }

        public static string NoActive(string componentName) => "no active " + componentName;

        /// <summary>
        /// <c>when</c> 이 지금 거절하는 이유. 쓸 수 있으면 null.
        /// </summary>
        /// <remarks>
        /// 던지는 <c>when</c> 은 쓸 수 없는 것으로 친다. 그 예외를 삼키지 않고 이유 문장에 실어야 개발자가 고칠 곳을 안다.
        /// </remarks>
        public static string UnavailableReason(GameFunction function, Component target)
        {
            if (function.Condition == null)
            {
                return null;
            }

            object answer;
            try
            {
                answer = function.Condition(target);
            }
            catch (Exception exception)
            {
                return Describe(exception);
            }

            if (answer is bool available)
            {
                return available ? null : function.ConditionName + " returned false";
            }

            var reason = answer as string;
            return string.IsNullOrEmpty(reason) ? null : reason;
        }

        public static string Describe(Exception exception) => exception.GetType().Name + ": " + exception.Message;

        private static bool TryResolveSelector(
            GameFunction function, string requested, List<LiveInstance> live,
            out Component target, out string selector, out string error)
        {
            selector = requested;
            foreach (var instance in live)
            {
                if (instance.Selector == requested)
                {
                    target = instance.Component;
                    error = null;
                    return true;
                }
            }

            target = null;
            if (!ScenePathSelectorResolver.TryResolve(requested, out var transform, out error))
            {
                return false;
            }

            target = transform.GetComponent(function.Component);
            if (target == null)
            {
                error = function.Component.Name + " is not on " + requested;
                return false;
            }

            return true;
        }
    }
}
#endif
