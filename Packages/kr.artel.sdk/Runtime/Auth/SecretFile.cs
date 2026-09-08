using System;
using System.IO;
using System.Threading;

namespace Artel.Auth
{
    /// <summary>
    /// 다른 프로세스가 같은 경로에 동시에 쓰고 있어도 끝까지 쓰이는 파일 교체.
    /// </summary>
    /// <remarks>
    /// secret 파일은 프로젝트가 아니라 사람 단위로 하나다 (<c>DpapiSecretStore</c>).
    /// 같은 사람이 게임을 두 개 띄우면 두 프로세스가 같은 파일에 쓴다는 뜻이고,
    /// <c>ARTEL_SDK_TOKEN</c> 을 들고 인스턴스를 여러 개 띄우는 QA 런에서 실제로 부딪힌다.
    /// 토큰을 심는 자리가 <c>BeforeSceneLoad</c> 라 두 프로세스의 쓰기가 거의 같은 순간이다.
    ///
    /// <c>File.WriteAllBytes</c> 로는 그 자리를 넘길 수 없다. 대상 파일을 <c>FileShare.Read</c>
    /// 로 열기 때문에 뒤에 온 프로세스는 여는 순간 sharing violation 으로 <c>IOException</c> 을
    /// 맞는다. 읽는 쪽도 위험하다 — 그 함수는 파일을 먼저 잘라 놓고 채우므로, 그 사이에 읽으면
    /// 복호화되지 않는 반쪽 암호문이 나온다.
    ///
    /// 그래서 프로세스마다 다른 temp 파일에 다 쓴 뒤 이름만 바꿔 끼운다. 읽는 쪽에는 예전 값
    /// 아니면 새 값만 보이고, 쓰는 쪽끼리는 마지막에 끼운 값이 남는다. 같은 토큰을 쓰는 QA 런
    /// 에서는 어느 쪽이 이기든 결과가 같다.
    /// </remarks>
    internal static class SecretFile
    {
        // 400바이트짜리 쓰기라 부딪히는 구간은 밀리초 단위다. 그래도 유니티 메인 스레드를
        // 붙잡고 기다리는 것이므로 상한을 짧게 둔다 — 최대 250ms.
        private const int SwapAttempts = 10;
        private const int SwapWaitMilliseconds = 25;

        public static void Replace(string path, byte[] contents)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));

            // temp 이름에 Guid 를 붙인다. 프로세스 두 개가 같은 temp 파일을 쓰면 부딪히는
            // 자리를 옮겼을 뿐 아무것도 고치지 못한다.
            var temporaryPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            File.WriteAllBytes(temporaryPath, contents);

            try
            {
                Swap(temporaryPath, path);
            }
            catch
            {
                Discard(temporaryPath);
                throw;
            }
        }

        /// <summary>
        /// temp 파일을 <paramref name="path"/> 자리에 끼운다. 실패하면 짧게 기다렸다 다시 한다.
        /// </summary>
        /// <remarks>
        /// <c>File.Move</c> 는 대상이 이미 있으면 실패하고 <c>File.Replace</c> 는 대상이 없으면
        /// 실패하므로, 어느 쪽을 부를지는 파일이 있는지로 정한다. 확인과 호출 사이에 다른
        /// 프로세스가 파일을 만들거나 지울 수 있는데, 그때 나오는 <c>IOException</c> 은 다음
        /// 회차가 바뀐 상태를 다시 보고 반대쪽을 부르면서 저절로 풀린다.
        ///
        /// 재시도가 필요한 이유는 하나 더 있다. 읽는 쪽이 파일을 연 그 순간에는 교체도 막힌다.
        /// </remarks>
        private static void Swap(string temporaryPath, string path)
        {
            for (var attempt = 1; ; attempt++)
            {
                try
                {
                    if (File.Exists(path))
                    {
                        File.Replace(temporaryPath, path, null);
                    }
                    else
                    {
                        File.Move(temporaryPath, path);
                    }

                    return;
                }
                catch (IOException) when (attempt < SwapAttempts)
                {
                    Thread.Sleep(SwapWaitMilliseconds);
                }
            }
        }

        private static void Discard(string temporaryPath)
        {
            try
            {
                File.Delete(temporaryPath);
            }
            catch (IOException)
            {
                // 치우지 못한 temp 파일은 그 자리에 남는다. 여기서 예외를 올리면 정작 저장이
                // 왜 실패했는지가 이 예외에 가려진다.
            }
        }
    }
}
