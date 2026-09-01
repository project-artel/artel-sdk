namespace Artel.Affordances.CodeGen.Tests
{
    /// <summary>
    /// 분석기가 읽을 IL. 소스로 쓰고 컴파일러가 만든 것을 그대로 읽는다.
    /// </summary>
    /// <remarks>
    /// 손으로 엮은 IL 이 아니라 제 어셈블리를 다시 읽는 것은, 확인하려는 것이 "이 명령어 배열을 읽는가" 가 아니라
    /// "게임이 쓰는 C# 을 읽는가" 이기 때문이다. 컴파일러가 모양을 바꾸면 테스트가 그것을 먼저 말한다.
    ///
    /// 필드가 <c>public</c> 인 것은 그렇게 쓰라는 뜻이 아니라, 조건에 이름으로 나타나는 것이 필드이고 프로퍼티
    /// 뒤에 숨기면 무엇이 읽혔는지가 가려지기 때문이다.
    /// </remarks>
    internal sealed class PredicateFixtures
    {
        internal int hp;
        internal int limit;
        internal object handle;
        internal float ratio;
        internal PredicateFixtures other;

        /// <summary>값 모양. 표현식 하나가 그대로 돌아간다.</summary>
        internal bool Alive => hp > 0;

        /// <summary>같은 값 모양인데 블록 body 다. 디버그 빌드가 답을 지역 변수로 옮기는 모양이 이것이다.</summary>
        internal bool Busy
        {
            get { return handle != null; }
        }

        /// <summary>
        /// 부호 없는 비교인데 <c>null</c> 과는 상관없다.
        /// </summary>
        /// <remarks>
        /// float 의 <c>&lt;=</c> 는 <c>cgt.un</c> 의 부정으로 컴파일된다. null 을 알아보는 규칙이 이것까지
        /// 집어삼키면 크기 비교가 <c>==</c> 로 뒤집힌다.
        /// </remarks>
        internal bool WithinRatio => ratio <= 1f;

        /// <summary>제 매개변수만 비교한다. 호출자는 이 이름을 댈 수 없다.</summary>
        internal bool Above(int mark)
        {
            return mark > 0;
        }

        /// <summary>답을 제어 흐름으로 고른다. 상수 둘을 서로 다른 자리에서 내놓는다.</summary>
        internal bool Ready()
        {
            if (hp > limit)
            {
                return true;
            }

            return false;
        }

        /// <summary>참을 돌려주는 자리가 둘이다. 그중 아무 곳에나 닿아도 된다.</summary>
        internal bool EitherWay()
        {
            if (hp > 0)
            {
                return true;
            }

            if (limit > 0)
            {
                return true;
            }

            return false;
        }

        /// <summary>
        /// 답의 절반은 상수로, 절반은 값으로 고른다.
        /// </summary>
        /// <remarks>
        /// 읽은 절반만 내놓으면 조건의 반쪽 진술이 되고, 그것은 그냥 틀린 선행 조건의 모양이다. 통째로 포기한다.
        /// </remarks>
        internal bool Mixed()
        {
            if (hp > 0)
            {
                return true;
            }

            return handle != null;
        }

        /// <summary>읽을 것이 있는 블록을 만들기 위한 호출. 이것이 없으면 기록이 나오지 않는다.</summary>
        internal void Mark()
        {
        }

        internal void GuardedByOwn()
        {
            if (Alive)
            {
                Mark();
            }
        }

        internal void GuardedByNegatedOwn()
        {
            if (!Alive)
            {
                Mark();
            }
        }

        internal void GuardedByOther()
        {
            if (other.Busy)
            {
                Mark();
            }
        }

        internal void GuardedByOtherWithArgument()
        {
            if (other.Above(limit))
            {
                Mark();
            }
        }

        internal void GuardedByOwnArgument()
        {
            if (Above(limit))
            {
                Mark();
            }
        }

        internal void GuardedByBranchingPredicate()
        {
            if (Ready())
            {
                Mark();
            }
        }

        internal void GuardedByEitherWay()
        {
            if (EitherWay())
            {
                Mark();
            }
        }

        internal void GuardedByMixedPredicate()
        {
            if (Mixed())
            {
                Mark();
            }
        }
    }
}
