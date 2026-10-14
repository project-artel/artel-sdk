namespace Artel
{
    /// <summary>cheat 와 behavior parameter 의 타입. catalog 의 <c>type</c> 값이 이 이름의 소문자다.</summary>
    public enum CheatParamType
    {
        Int,
        Float,
        Bool,
        String,
        Enum
    }

    /// <summary>catalog 의 어느 목록에 들어가는지.</summary>
    public enum GameFunctionKind
    {
        Cheat,
        Behavior
    }
}
