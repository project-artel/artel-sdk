using UnityEngine;

namespace Artel.Affordances.CodeGen.Tests
{
    /// <summary>부모 class 의 진입점을 자식이 물려받는지 보는 데 쓰는 behaviour 들(ARTEL-1228).</summary>
    internal class InheritedBaseFixture : MonoBehaviour
    {
        private void Start()
        {
            enabled = false;
        }

        public void Press()
        {
            gameObject.SetActive(false);
        }

        protected static bool Ready()
        {
            return Input.anyKeyDown;
        }
    }

    /// <summary>아무것도 다시 선언하지 않는다. 부모의 <c>Start</c> 와 <c>Press</c> 를 그대로 쓴다.</summary>
    internal sealed class InheritedPlainFixture : InheritedBaseFixture
    {
    }

    /// <summary><c>Start</c> 를 다시 선언하고, 제 <c>Update</c> 에서 부모의 static 메서드를 부른다.</summary>
    internal sealed class InheritedOwnStartFixture : InheritedBaseFixture
    {
        private void Start()
        {
        }

        private void Update()
        {
            if (Ready())
            {
                enabled = false;
            }
        }
    }
}
