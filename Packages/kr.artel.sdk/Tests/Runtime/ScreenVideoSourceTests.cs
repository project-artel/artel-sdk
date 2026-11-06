using System.Collections;
using Artel.Streaming;
using NUnit.Framework;
using UnityEngine;

namespace Artel.Tests.Streaming
{
    /// <summary>
    /// The capture loop itself needs a running frame loop and a back buffer, so it is not driven
    /// here. What is covered is the allocation lifetime: a capture target that outlives its
    /// session is a leak the game pays for with no peer attached.
    /// </summary>
    public sealed class ScreenVideoSourceTests
    {
        [Test]
        public void Stop_ReleasesTheRenderTextureAndTheCaptureLoop()
        {
            var captureLoop = new RecordingCaptureLoopRunner();
            var source = new ScreenVideoSource(captureLoop, 640, 30);

            var frame = source.Start();

            Assert.That(frame, Is.Not.Null);
            Assert.That(source.IsCapturing, Is.True);
            Assert.That(captureLoop.IsRunning, Is.True);

            source.Stop();

            Assert.That(captureLoop.IsRunning, Is.False);
            Assert.That(source.IsCapturing, Is.False);
            Assert.That(source.Frame, Is.Null);

            // A destroyed UnityEngine.Object is not a null reference, so this is the assertion that
            // actually says the native texture is gone rather than merely dereferenced.
            Assert.That(frame == null, Is.True);
        }

        [Test]
        public void Stop_IsSafeWithoutAStart()
        {
            var source = new ScreenVideoSource(new RecordingCaptureLoopRunner(), 640, 30);

            Assert.DoesNotThrow(source.Stop);
        }

        [Test]
        public void Start_KeepsTheFrameWithinMaxWidthAndEvenSided()
        {
            var captureLoop = new RecordingCaptureLoopRunner();
            var source = new ScreenVideoSource(captureLoop, 320, 30);

            var frame = source.Start();

            Assert.That(frame.width, Is.LessThanOrEqualTo(320));
            Assert.That(frame.width % 2, Is.Zero);
            Assert.That(frame.height % 2, Is.Zero);

            source.Stop();
        }

        [Test]
        public void Start_IsIdempotentSoOneSessionNeverHoldsTwoTextures()
        {
            var captureLoop = new RecordingCaptureLoopRunner();
            var source = new ScreenVideoSource(captureLoop, 640, 30);

            var frame = source.Start();

            Assert.That(source.Start(), Is.SameAs(frame));
            Assert.That(captureLoop.BeginCount, Is.EqualTo(1));

            source.Stop();
        }

        [TestCase(true, true)]
        [TestCase(false, false)]
        public void GPU_UV가_위에서_시작하는_환경에서만_JPEG를_상하_보정한다(bool startsAtTop, bool expected)
        {
            Assert.That(ScreenVideoSource.RequiresVerticalFlip(startsAtTop), Is.EqualTo(expected));
        }

        [Test]
        public void 복잡한_화면도_JPEG_상한에_맞게_축소하고_축소한_surface를_재사용한다()
        {
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
                Assert.Ignore("실제 GPU의 RenderTexture와 readback이 필요한 테스트입니다.");
            var source = new ScreenVideoSource(new RecordingCaptureLoopRunner(), 1280, 10);
            byte[] received = null;
            source.ConfigureJpeg(() => true, jpeg => received = jpeg, error => Assert.Fail(error), 65, 32768, 720);
            var texture = source.Start();
            var initialWidth = texture.width;
            var noise = new Texture2D(texture.width, texture.height, TextureFormat.RGB24, false);
            var decoded = new Texture2D(2, 2, TextureFormat.RGB24, false);
            try
            {
                var random = new System.Random(42);
                var pixels = new Color32[texture.width * texture.height];
                for (var i = 0; i < pixels.Length; i++)
                    pixels[i] = new Color32((byte)random.Next(256), (byte)random.Next(256), (byte)random.Next(256), 255);
                noise.SetPixels32(pixels); noise.Apply(false, false);
                Graphics.Blit(noise, texture);
                source.CaptureJpeg();
                Assert.That(received, Is.Not.Null);
                Assert.That(received.Length, Is.LessThanOrEqualTo(32768));
                Assert.That(source.Frame.width, Is.LessThan(initialWidth));
                Assert.That(ImageConversion.LoadImage(decoded, received), Is.True);
                Assert.That(decoded.width, Is.EqualTo(source.Frame.width));
                var smaller = source.Frame;
                source.CaptureJpeg();
                Assert.That(source.Frame, Is.SameAs(smaller));
                Assert.That(received.Length, Is.LessThanOrEqualTo(32768));
            }
            finally
            {
                source.Stop();
                Object.DestroyImmediate(noise);
                Object.DestroyImmediate(decoded);
            }
        }

        private sealed class RecordingCaptureLoopRunner : ICaptureLoopRunner
        {
            public bool IsRunning { get; private set; }
            public int BeginCount { get; private set; }

            public void Begin(IEnumerator loop)
            {
                BeginCount++;
                IsRunning = true;
            }

            public void End()
            {
                IsRunning = false;
            }
        }
    }
}
