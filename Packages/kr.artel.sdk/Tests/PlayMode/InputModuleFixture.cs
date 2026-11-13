using System;
using NUnit.Framework;
using UnityEngine;

namespace Artel.Tests
{
    internal static class InputModuleFixture
    {
        // 공통 UI 테스트는 optional package에 assembly 참조를 만들지 않는다.
        internal static void AddNativeModule(GameObject target)
        {
            var type = Type.GetType("UnityEngine.InputSystem.UI.InputSystemUIInputModule, Unity.InputSystem");
            Assert.That(type, Is.Not.Null, "New-only UI fixture requires Input System.");
            var assign = type.GetMethod("AssignDefaultActions", Type.EmptyTypes);
            Assert.That(assign, Is.Not.Null);
            assign.Invoke(target.AddComponent(type), null);
        }
    }
}
