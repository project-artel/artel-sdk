using System;
using System.IO;

namespace Artel.Tests
{
    /// <summary>
    /// 테스트 하나 동안 <see cref="ArtelIdentityStore"/> 를 빈 임시 파일로 돌린다. 실제 설치본의 <c>identity.json</c> 을
    /// 건드리지 않고, 테스트끼리 앞 테스트가 남긴 식별자를 보지 않게 한다.
    /// </summary>
    internal sealed class IdentityFileSandbox : IDisposable
    {
        private readonly string directory;

        public IdentityFileSandbox()
        {
            directory = Path.Combine(Path.GetTempPath(), "artel-identity-" + Guid.NewGuid().ToString("N"));
            ArtelIdentityStore.PathOverride = Path.Combine(directory, "identity.json");
            ArtelIdentityStore.ForgetCache();
        }

        public string FilePath => ArtelIdentityStore.PathOverride;

        public void Dispose()
        {
            ArtelIdentityStore.PathOverride = null;
            ArtelIdentityStore.ForgetCache();
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, true);
            }
        }
    }
}
