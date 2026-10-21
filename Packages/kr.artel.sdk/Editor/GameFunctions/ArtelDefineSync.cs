using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.PackageManager;

namespace Artel.GameFunctions.Editor
{
    /// <summary>
    /// 에디터가 열릴 때마다 PlayerSettings 의 scripting define 을 <see cref="ArtelDefines.Supported"/> 에 맞추고,
    /// 이 SDK 가 제거될 때 <c>ARTEL_</c> define 을 모두 끈다.
    /// </summary>
    /// <remarks>
    /// 게임 코드 대부분은 asmdef 가 없는 <c>Assembly-CSharp</c> 에 있어서 asmdef 의 Version Defines 로는 cheat 파일을
    /// compile 에서 뺄 수 없다. 그래서 게임은 cheat 파일을 <c>#if ARTEL_GAME_FUNCTIONS</c> 로 감싸고, cheat 기능이 들어간
    /// SDK 가 그 define 을 켠다. SDK 가 없거나 cheat 기능이 없는 예전 SDK 이면 아무도 define 을 켜지 않는다.
    ///
    /// define 은 ProjectSettings 에 저장되므로 SDK 를 제거해도 남는다. 남은 define 은 SDK 타입이 없는 상태에서 감싼 코드를
    /// 다시 compile 하게 만든다. SDK 가 제거된 뒤에는 이 코드도 없으므로, 제거가 등록되는 시점인
    /// <see cref="Events.registeringPackages"/> 에서 미리 끈다. 이 event 는 에디터가 열려 있는 동안 Package Manager 로
    /// 제거하거나 버전을 바꿀 때만 온다. 에디터를 닫고 manifest.json 을 고치면 오지 않는다.
    ///
    /// 감싼 코드가 compile 에 실패하는 동안 Unity 는 이 assembly 를 load 하지 않는다. 그래서 compile 이 깨진 뒤에 define 을
    /// 고치는 것은 이 코드로 할 수 없고, 깨지기 전인 event 시점에 끄는 것만 가능하다.
    /// </remarks>
    [InitializeOnLoad]
    internal static class ArtelDefineSync
    {
        private const string PackageName = "kr.artel.sdk";

        static ArtelDefineSync()
        {
            Events.registeringPackages += OnRegisteringPackages;
            Apply(ArtelDefines.Supported);
        }

        /// <summary>
        /// 이 SDK 가 제거되거나 다른 버전으로 바뀌기 직전에 <c>ARTEL_</c> define 을 모두 끈다.
        /// </summary>
        /// <remarks>
        /// 다른 버전으로 바뀔 때도 끄는 이유: 예전 버전으로 내리면 그 버전에 없는 define 이 남는데, 그 define 으로 감싼 코드가
        /// compile 에 실패하면 Unity 는 예전 버전의 이 코드를 load 하지 않아서 아무도 define 을 끄지 못한다. 끈 뒤에는 감싼
        /// 코드가 빠져서 compile 이 성공하고, 새로 load 된 버전이 자기가 지원하는 define 을 다시 켠다.
        /// </remarks>
        private static void OnRegisteringPackages(PackageRegistrationEventArgs args)
        {
            var leaving = args.removed.Concat(args.changedFrom);
            if (leaving.Any(package => package.name == PackageName))
            {
                Apply(Array.Empty<string>());
            }
        }

        private static void Apply(IReadOnlyCollection<string> supported)
        {
            foreach (var target in TargetsToSync())
            {
                PlayerSettings.GetScriptingDefineSymbols(target, out var current);
                var reconciled = ArtelDefines.Reconcile(current, supported);
                if (reconciled.SequenceEqual(current))
                {
                    continue;
                }

                PlayerSettings.SetScriptingDefineSymbols(target, reconciled);
            }
        }

        /// <summary>
        /// Standalone 과 현재 build target 은 항상 맞춘다. 다른 build target 은 이미 define 이 하나라도 있는 것만 맞춘다 —
        /// 그 target 으로 build 하는 프로젝트라는 뜻이고, 쓰지 않는 target 에 define 을 새로 써서 ProjectSettings 를 바꾸지 않는다.
        /// </summary>
        private static IEnumerable<NamedBuildTarget> TargetsToSync()
        {
            var activeGroup = BuildPipeline.GetBuildTargetGroup(EditorUserBuildSettings.activeBuildTarget);
            var targets = new List<NamedBuildTarget>
            {
                NamedBuildTarget.Standalone,
                NamedBuildTarget.FromBuildTargetGroup(activeGroup),
            };

            foreach (var target in NamedTargetsOfValidGroups())
            {
                PlayerSettings.GetScriptingDefineSymbols(target, out var current);
                if (current.Length > 0)
                {
                    targets.Add(target);
                }
            }

            return targets.Distinct();
        }

        /// <remarks>
        /// <c>iPhone</c> 처럼 obsolete 이름이 다른 이름과 같은 값을 갖는 경우가 있어서 값이 아니라 field 로 obsolete 를 거른다.
        /// </remarks>
        private static IEnumerable<NamedBuildTarget> NamedTargetsOfValidGroups()
        {
            var groups = typeof(BuildTargetGroup)
                .GetFields(BindingFlags.Public | BindingFlags.Static)
                .Where(field => !field.IsDefined(typeof(ObsoleteAttribute), false))
                .Select(field => (BuildTargetGroup)field.GetValue(null))
                .Where(group => group != BuildTargetGroup.Unknown)
                .Distinct();

            foreach (var group in groups)
            {
                NamedBuildTarget target;
                try
                {
                    target = NamedBuildTarget.FromBuildTargetGroup(group);
                }
                catch (ArgumentException)
                {
                    continue;
                }

                yield return target;
            }
        }
    }
}
