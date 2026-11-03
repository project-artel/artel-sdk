using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace Artel.Affordances.Live
{
    /// <summary>
    /// 게임 코드가 <see cref="ArtelObserveAttribute"/> 로 표시한 멤버들. watch list 의 셋째 출처.
    /// </summary>
    /// <remarks>
    /// 한 번 훑는다. 타입은 게임이 도는 동안 바뀌지 않고, <see cref="Lookup"/> 이 이미 게임 어셈블리의 타입 목록을 쥐고
    /// 있다. 컴포넌트가 아닌 owner 의 인스턴스는 <see cref="Lookup.HoldersOf"/> 가 첫 판독에 찾는다 — 어셈블리를 훑는
    /// 순간은 아무것도 만들어지기 전일 수 있다.
    /// </remarks>
    internal static class Declared
    {
        internal static void AddTo(List<Watched> into)
        {
            const BindingFlags Flags =
                BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic |
                BindingFlags.DeclaredOnly;

            var seen = new HashSet<string>(StringComparer.Ordinal);

            foreach (var member in into)
            {
                seen.Add(member.Key);
            }

            foreach (var type in Lookup.GameTypes())
            {
                if (type.IsGenericTypeDefinition)
                {
                    continue;
                }

                MemberInfo[] members;

                try
                {
                    members = type.GetMembers(Flags);
                }
                catch (Exception)
                {
                    continue;
                }

                foreach (var member in members)
                {
                    if (!(member is FieldInfo || member is PropertyInfo) || !Marked(member))
                    {
                        continue;
                    }

                    var watched = Describe(type, member);

                    if (watched == null || !seen.Add(watched.Key))
                    {
                        continue;
                    }

                    into.Add(watched);
                }
            }
        }

        private static bool Marked(MemberInfo member)
        {
            try
            {
                return member.IsDefined(typeof(ArtelObserveAttribute), true);
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static Watched Describe(Type owner, MemberInfo member)
        {
            var field = member as FieldInfo;
            var getter = member as PropertyInfo;
            Type held;
            bool isStatic;

            if (field != null)
            {
                held = field.FieldType;
                isStatic = field.IsStatic;
            }
            else
            {
                if (getter == null || !getter.CanRead || getter.GetIndexParameters().Length != 0)
                {
                    return null;
                }

                held = getter.PropertyType;
                isStatic = getter.GetGetMethod(true).IsStatic;
            }

            var watched = new Watched
            {
                Declaring = owner.FullName,
                Member = member.Name,
                Property = getter != null ? getter.Name : null,
                Type = held.FullName,
                Static = isStatic,
                Field = field,
                Getter = getter,
                Owner = owner
            };

            if (!isStatic && !typeof(Component).IsAssignableFrom(owner))
            {
                // 씬 걷기가 만나지 못하는 owner. 비어 있는 채로 두면 첫 판독의 `HoldersOf` 가 찾는다.
                watched.Holders = new List<object>();
                Lookup.MarkStale();
            }

            return watched;
        }
    }
}
