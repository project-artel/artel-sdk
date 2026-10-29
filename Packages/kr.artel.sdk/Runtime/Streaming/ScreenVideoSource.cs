using System;
using System.Collections;
using Artel.Diagnostics;
using UnityEngine;

namespace Artel.Streaming
{
    internal interface ICaptureLoopRunner
    {
        void Begin(IEnumerator loop);
        void End();
    }

    internal sealed class BehaviourCaptureLoopRunner : ICaptureLoopRunner
    {
        private readonly MonoBehaviour behaviour;
        private Coroutine loop;

        public BehaviourCaptureLoopRunner(MonoBehaviour behaviour)
        {
            this.behaviour = behaviour != null
                ? behaviour
                : throw new ArgumentNullException(nameof(behaviour));
        }

        public void Begin(IEnumerator routine)
        {
            End();
            loop = behaviour.StartCoroutine(routine);
        }

        public void End()
        {
            if (loop == null)
            {
                return;
            }

            behaviour.StopCoroutine(loop);
            loop = null;
        }
    }

    /// <summary>
    /// Captures the composited screen into a single RenderTexture that backs a video track.
    ///
    /// This exists instead of Camera.CaptureStreamTrack because a camera render does not contain
    /// Screen Space Overlay UI, which is most of what a QA viewer needs to see.
    /// </summary>
    internal sealed class ScreenVideoSource
    {
        private readonly ICaptureLoopRunner captureLoop;
        private readonly int maxWidth;
        private readonly float captureIntervalSeconds;
        private readonly WaitForEndOfFrame endOfFrame = new WaitForEndOfFrame();
        private RenderTexture frame;
        private float nextCaptureTime;
        private Texture2D readback;
        private Func<bool> canCapture;
        private Action<byte[]> captured;
        private int jpegQuality;
        private int maxFrameBytes;
        private int maxJpegHeight;
        private Action<string> captureFailed;
        public double LastJpegEncodeMilliseconds { get; private set; }
        public int LastJpegBytes { get; private set; }

        public void ConfigureJpeg(Func<bool> canCapture, Action<byte[]> captured, Action<string> failed, int quality, int maxBytes, int maxHeight)
        {
            if (frame != null) throw new InvalidOperationException("이미 시작한 capture 설정은 바꿀 수 없습니다.");
            this.canCapture = canCapture ?? throw new ArgumentNullException(nameof(canCapture));
            this.captured = captured ?? throw new ArgumentNullException(nameof(captured));
            captureFailed = failed ?? throw new ArgumentNullException(nameof(failed));
            jpegQuality = Mathf.Clamp(quality, 20, 80);
            maxFrameBytes = Mathf.Clamp(maxBytes, 1024, 131072);
            maxJpegHeight = Mathf.Clamp(maxHeight, 2, 720);
        }

        /// <param name="maxWidth">Zero or less means the screen's own width.</param>
        /// <param name="maxFramerate">Zero or less means every frame.</param>
        public ScreenVideoSource(ICaptureLoopRunner captureLoop, int maxWidth, int maxFramerate)
        {
            this.captureLoop = captureLoop ?? throw new ArgumentNullException(nameof(captureLoop));
            this.maxWidth = maxWidth;
            captureIntervalSeconds = maxFramerate > 0 ? 1f / maxFramerate : 0f;
        }

        public RenderTexture Frame { get { return frame; } }

        public bool IsCapturing { get { return frame != null; } }

        /// <summary>
        /// Allocates the capture target and starts the end-of-frame loop. Returns the texture the
        /// video track is to be built on.
        /// </summary>
        public RenderTexture Start()
        {
            if (frame != null)
            {
                return frame;
            }

            ResolveSize(out var width, out var height);
            if ((long)Screen.width * Screen.height > 4096L * 2160)
                throw new InvalidOperationException("영상 capture 화면은 4096×2160 이하여야 합니다.");

            // BGRA32 is the layout com.unity.webrtc reads a RenderTexture-backed track in. The
            // depth buffer is deliberately zero: nothing renders into this, it is only blitted to.
            frame = new RenderTexture(width, height, 0, RenderTextureFormat.BGRA32)
            {
                name = "ArtelScreenVideoSource"
            };
            frame.Create();
            if (captured != null) readback = new Texture2D(width, height, TextureFormat.RGB24, false);

            nextCaptureTime = 0f;
            captureLoop.Begin(CaptureFrames());
            return frame;
        }

        public void Stop()
        {
            captureLoop.End();
            if (readback != null)
            {
                if (Application.isPlaying) UnityEngine.Object.Destroy(readback);
                else UnityEngine.Object.DestroyImmediate(readback);
                readback = null;
            }

            if (frame == null)
            {
                return;
            }

            frame.Release();

            // Destroy is deferred and refuses to run outside play mode, so the edit-mode branch is
            // what keeps a test run from leaking one native texture per case.
            if (Application.isPlaying)
            {
                UnityEngine.Object.Destroy(frame);
            }
            else
            {
                UnityEngine.Object.DestroyImmediate(frame);
            }

            frame = null;
        }

        private IEnumerator CaptureFrames()
        {
            while (frame != null)
            {
                // The screenshot reads the back buffer, so it is only correct once everything for
                // this frame has been drawn — including the overlay UI this path exists for.
                yield return endOfFrame;

                if (Time.unscaledTime < nextCaptureTime)
                {
                    continue;
                }

                // ACK를 기다리는 동안 GPU readback과 JPEG encode도 생략한다.
                if (canCapture != null && !canCapture()) continue;

                nextCaptureTime = Time.unscaledTime + captureIntervalSeconds;
                try
                {
                    CaptureFrame();
                    if (captured != null) CaptureJpeg();
                }
                catch (Exception error)
                {
                    if (captureFailed != null) captureFailed("영상 capture 실패: " + error.Message);
                    else Debug.LogException(error);
                    yield break;
                }
            }
        }

        internal void CaptureJpeg()
        {
            var previous = RenderTexture.active;
            var encodeTimer = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                var quality = jpegQuality;
                for (var attempt = 0; attempt < 7; attempt++)
                {
                    RenderTexture.active = frame;
                    readback.ReadPixels(new Rect(0, 0, frame.width, frame.height), 0, 0, false);
                    readback.Apply(false, false);
                    var jpeg = readback.EncodeToJPG(quality);
                    if (jpeg.Length > maxFrameBytes && quality > 35)
                    {
                        quality = 35;
                        jpeg = readback.EncodeToJPG(quality);
                    }
                    LastJpegBytes = jpeg.Length;
                    if (jpeg.Length <= maxFrameBytes) { captured(jpeg); return; }
                    if (frame.width <= 2 || frame.height <= 2) break;
                    DownscaleFrame();
                }
                throw new InvalidOperationException("화면을 축소해도 JPEG frame 크기 제한을 맞추지 못했습니다.");
            }
            finally
            {
                encodeTimer.Stop();
                LastJpegEncodeMilliseconds = encodeTimer.Elapsed.TotalMilliseconds;
                RenderTexture.active = previous;
            }
        }

        private void DownscaleFrame()
        {
            var previousFrame = frame;
            var previousReadback = readback;
            var width = Mathf.Max(2, (previousFrame.width / 2) & ~1);
            var height = Mathf.Max(2, (previousFrame.height / 2) & ~1);
            var smaller = new RenderTexture(width, height, 0, RenderTextureFormat.BGRA32)
            {
                name = "ArtelScreenVideoSource"
            };
            Texture2D smallerReadback = null;
            try
            {
                smaller.Create();
                smallerReadback = new Texture2D(width, height, TextureFormat.RGB24, false);
                Graphics.Blit(previousFrame, smaller);
                RenderTexture.active = null;
            }
            catch
            {
                smaller.Release();
                DestroyCaptureObject(smaller);
                if (smallerReadback != null) DestroyCaptureObject(smallerReadback);
                throw;
            }
            frame = smaller;
            readback = smallerReadback;
            previousFrame.Release();
            DestroyCaptureObject(previousFrame);
            DestroyCaptureObject(previousReadback);
        }

        private static void DestroyCaptureObject(UnityEngine.Object resource)
        {
            if (Application.isPlaying) UnityEngine.Object.Destroy(resource);
            else UnityEngine.Object.DestroyImmediate(resource);
        }

        internal static bool RequiresVerticalFlip(bool graphicsUvStartsAtTop) { return graphicsUvStartsAtTop; }

        private void CaptureFrame()
        {
            // The end-of-frame wait sits in the caller on purpose: wrapping it would report the
            // wait as capture cost.
            using (ArtelProfilerMarkers.StreamCaptureFrame.Auto())
            {
                // CaptureScreenshotIntoRenderTexture wants a target the size of the screen, so the
                // downscale to maxWidth happens in the blit rather than here.
                var backBuffer = RenderTexture.GetTemporary(
                    Mathf.Max(2, Screen.width), Mathf.Max(2, Screen.height), 0, RenderTextureFormat.BGRA32);
                var previous = RenderTexture.active;

                try
                {
                    // grab은 현재 target이 아니라 화면 back buffer에서 시작해야 한다. 복원하지 않으면
                    // 같은 end-of-frame에 도는 capture_screen이 이 frame texture를 화면으로 오인한다.
                    RenderTexture.active = null;
                    ScreenCapture.CaptureScreenshotIntoRenderTexture(backBuffer);

                    // The screenshot is written in framebuffer orientation, which under D3D is upside
                    // down relative to what the encoder expects. The flip is not optional: getting it
                    // wrong produces a working-but-inverted stream, which reads as success in a smoke
                    // test and is only caught against on-screen text.
                    if (captured == null || RequiresVerticalFlip(SystemInfo.graphicsUVStartsAtTop))
                        Graphics.Blit(backBuffer, frame, new Vector2(1f, -1f), new Vector2(0f, 1f));
                    else Graphics.Blit(backBuffer, frame);
                }
                finally
                {
                    RenderTexture.active = previous;
                    RenderTexture.ReleaseTemporary(backBuffer);
                }
            }
        }

        private void ResolveSize(out int width, out int height)
        {
            var screenWidth = Mathf.Max(2, Screen.width);
            var screenHeight = Mathf.Max(2, Screen.height);

            width = maxWidth > 0 ? Mathf.Min(screenWidth, maxWidth) : screenWidth;
            height = Mathf.RoundToInt(screenHeight * (width / (float)screenWidth));
            if (captured != null && height > maxJpegHeight)
            {
                width = Mathf.RoundToInt(width * (maxJpegHeight / (float)height));
                height = maxJpegHeight;
            }

            // Video encoders reject odd dimensions, and the rejection surfaces at track creation
            // rather than here, which puts it a long way from the aspect-ratio maths that caused it.
            width = Mathf.Max(2, width - (width % 2));
            height = Mathf.Max(2, height - (height % 2));
        }
    }
}
