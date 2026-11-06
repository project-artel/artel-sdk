using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;
using Artel.Auth;
using NUnit.Framework;

namespace Artel.Tests
{
    /// <summary>
    /// <c>SecretFile.Replace</c> 가 여러 프로세스가 같은 파일에 쓰는 자리를 견디는지 본다.
    /// </summary>
    /// <remarks>
    /// 실제 실패는 게임 인스턴스 두 개가 <c>%LOCALAPPDATA%\Artel\Secrets\Artel.SdkToken.bin</c>
    /// 하나에 같이 쓰면서 났다. 테스트에서 프로세스를 두 개 띄울 수는 없으므로 스레드로 바꿔
    /// 재현한다 — 부딪히는 지점이 파일 잠금이라 프로세스든 스레드든 같은 자리에서 막힌다.
    ///
    /// 실제 secret 경로는 건드리지 않는다. <c>DpapiSecretStore</c> 가 정하는 경로는 지금
    /// 로그인한 사람의 토큰이 있는 자리라, 테스트가 그리로 쓰면 로그인을 날린다.
    /// </remarks>
    public sealed class SecretFileTests
    {
        private const int WriterCount = 8;

        private string directory;
        private string path;

        [SetUp]
        public void SetUp()
        {
            directory = Path.Combine(Path.GetTempPath(), "artel-secret-file-" + Guid.NewGuid().ToString("N"));
            path = Path.Combine(directory, "Artel.SdkToken.bin");
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, true);
            }
        }

        [Test]
        public void TheFileIsCreatedWhenNothingIsThereYet()
        {
            SecretFile.Replace(path, new byte[] { 1, 2, 3 });

            Assert.That(File.ReadAllBytes(path), Is.EqualTo(new byte[] { 1, 2, 3 }));
        }

        [Test]
        public void NothingOfTheOldValueSurvivesAReplace()
        {
            SecretFile.Replace(path, new byte[] { 1, 2, 3, 4, 5 });
            SecretFile.Replace(path, new byte[] { 9 });

            Assert.That(File.ReadAllBytes(path), Is.EqualTo(new byte[] { 9 }));
        }

#if UNITY_EDITOR_WIN
        /// <summary>
        /// 끝내 끼우지 못하면 예외를 올리고, temp 파일은 남기지 않는다.
        /// </summary>
        /// <remarks>
        /// 재시도가 실제로 도는 것을 확인할 수 있는 유일한 자리다. 스레드 여덟 개로 부딪히게
        /// 하는 테스트는 어느 쪽도 재시도에 들어가지 않은 채 지나갈 수 있지만, 파일을
        /// <c>FileShare.None</c> 으로 붙잡고 있으면 열 번이 다 소진되는 것이 확실하다.
        ///
        /// Windows 에서만 돈다. 잠금을 지키는 것은 운영체제이고, Mono 는 유닉스에서 이
        /// <c>FileShare</c> 를 강제하지 않아 <c>File.Replace</c> 가 그냥 성공한다. 어차피
        /// 이 코드를 쓰는 <c>DpapiSecretStore</c> 도 Windows 전용이다.
        ///
        /// 저장에 실패하면 예외를 올리는 것이 맞다 — <c>ArtelSecretStore</c> 가 쓰기 실패만은
        /// 접지 않는 이유를 여기서도 지킨다. 조용히 넘어가면 로그인한 줄 알고 있다가 다음
        /// 실행에서 로그아웃된 상태로 돌아온다.
        /// </remarks>
        [Test]
        public void TheWriteFailsWhenTheFileStaysLocked()
        {
            SecretFile.Replace(path, new byte[] { 1 });

            using (File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                Assert.That(
                    () => SecretFile.Replace(path, new byte[] { 2 }),
                    Throws.InstanceOf<IOException>());
            }

            Assert.That(Directory.GetFiles(directory), Is.EqualTo(new[] { path }));
            Assert.That(File.ReadAllBytes(path), Is.EqualTo(new byte[] { 1 }));
        }
#endif

        [Test]
        public void TheTemporaryFileIsGoneAfterAWrite()
        {
            SecretFile.Replace(path, new byte[] { 1 });

            Assert.That(Directory.GetFiles(directory), Is.EqualTo(new[] { path }));
        }

        /// <summary>
        /// 여덟 스레드가 같은 순간에 같은 파일을 쓴다. 하나라도 예외를 맞으면 실패다.
        /// </summary>
        /// <remarks>
        /// <c>Barrier</c> 로 출발을 맞추는 것이 요점이다. 그냥 순서대로 띄우면 앞선 것이 끝난
        /// 뒤에 뒤엣것이 시작할 수 있고, 그러면 정작 재현하려는 자리를 한 번도 지나지 않은 채
        /// 초록불이 켜진다.
        ///
        /// thread pool 대신 스레드를 직접 만드는 이유도 같다. 코어가 둘뿐인 CI 에서
        /// <c>Task.Run</c> 여덟 개는 동시에 출발하지 못하고, 여덟이 다 모여야 열리는 barrier
        /// 앞에서 pool 이 스레드를 하나씩 늘려 줄 때까지 기다리게 된다.
        ///
        /// 예외는 스레드 안에서 받아 둔다. 스레드 밖으로 나가면 NUnit 이 실패로 세는 대신
        /// 테스트 실행 자체가 끝나 버린다.
        /// </remarks>
        [Test]
        public void EveryWriterFinishesWhenTheyAllWriteAtOnce()
        {
            var start = new Barrier(WriterCount);
            var failures = new ConcurrentQueue<Exception>();
            var writers = new Thread[WriterCount];
            for (var index = 0; index < WriterCount; index++)
            {
                var contents = new byte[] { (byte)index };
                writers[index] = new Thread(() =>
                {
                    start.SignalAndWait();
                    try
                    {
                        SecretFile.Replace(path, contents);
                    }
                    catch (Exception exception)
                    {
                        failures.Enqueue(exception);
                    }
                });
                writers[index].Start();
            }

            foreach (var writer in writers)
            {
                writer.Join();
            }

            Assert.That(failures, Is.Empty);

            // 이긴 쪽이 누구인지는 정하지 않는다. 정할 수 있는 것은 남은 파일이 하나라는
            // 것과, 그 내용이 잘리지 않은 채 어느 한 사람이 쓴 값 그대로라는 것뿐이다.
            Assert.That(Directory.GetFiles(directory), Is.EqualTo(new[] { path }));
            Assert.That(File.ReadAllBytes(path).Length, Is.EqualTo(1));
        }
    }
}
