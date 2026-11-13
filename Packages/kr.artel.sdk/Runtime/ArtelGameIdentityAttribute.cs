using System;

namespace Artel
{
    /// <summary>기존 게임 코드의 호환용 이름. 새 선언에는 ArtelMetadata를 사용한다.</summary>
    [Obsolete("Use ArtelMetadataAttribute instead.")]
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
    public sealed class ArtelGameIdentityAttribute : ArtelMetadataAttribute
    {
    }
}
