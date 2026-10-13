namespace Artel.Affordances.CodeGen.Tests
{
    /// <summary>
    /// 제 값으로 제 값을 갱신하는 필드를 가진 타입. <see cref="IlReading"/> 이 이 필드를 따라 읽다가 같은 자리로 되돌아오는지 확인하는 데 쓴다.
    /// </summary>
    /// <remarks>
    /// 필드가 private 이고 직렬화되지 않으며 쓰는 자리가 <see cref="Advance"/> 한 곳이라, <c>IlReading</c> 은 이
    /// 필드를 그 한 번의 대입이 담은 식으로 따라간다. 그 식 <c>count + 1</c> 이 다시 같은 필드를 읽으므로, 따라가는
    /// 깊이를 세지 않으면 끝나지 않는다.
    /// </remarks>
    internal sealed class SelfReferencingFixtures
    {
        private int count;

        internal void Advance()
        {
            count = count + 1;
        }

        internal bool Reached => count > 3;
    }
}
