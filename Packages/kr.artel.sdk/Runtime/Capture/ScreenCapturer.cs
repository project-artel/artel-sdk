using System;
using System.Collections;
using Artel.Diagnostics;
using UnityEngine;

namespace Artel.Capture
{
    /// <summary>
    /// Captures the composited screen, including Screen Space Overlay UI.
    /// </summary>
    /// <remarks>
    /// Reads the back buffer the same way <see cref="Streaming.ScreenVideoSource"/> does, for the
    /// same reason: a camera render omits overlay UI, which is most of what a QA agent needs to
    /// judge. The virtual cursor is part of that composite and appears in captures on purpose —
    /// the agent seeing where its own pointer is beats a clean image.
    ///
    /// The keyboard status panel is the one exception, and it is turned off for the frame this
    /// grabs (ARTEL-881). It reports the SDK's own state rather than the game's, and the agent
    /// reads it as the bottom strip of the game screen.
    /// </remarks>
    internal sealed class ScreenCapturer : IScreenCapturer
    {
        private readonly WaitForEndOfFrame endOfFrame = new WaitForEndOfFrame();
        private readonly KeyboardStatusController keyboardStatus;

        /// <param name="keyboardStatus">
        /// The panel to turn off for the captured frame. Required so that a new call site has to
        /// answer the question; the body still tolerates the reference being gone, since a
        /// destroyed <c>UnityEngine.Object</c> compares equal to null.
        /// </param>
        public ScreenCapturer(KeyboardStatusController keyboardStatus)
        {
            this.keyboardStatus = keyboardStatus;
        }

        public IEnumerator Capture(
            CaptureRequest request,
            Rect? pixelRect,
            Action<CapturedImage> completed)
        {
            if (completed == null)
            {
                throw new ArgumentNullException(nameof(completed));
            }

            if (Application.isBatchMode)
            {
                // There is no framebuffer to read. Said plainly here because the alternative is a
                // black image that reads as a rendering bug.
                completed(CapturedImage.Failed(
                    "The game runs in batchmode and has no screen to capture."));
                yield break;
            }

            // 패널을 끄는 것은 end-of-frame 을 기다리기 전이어야 한다. 그 뒤에 끄면 잡을 프레임은 이미 그려진
            // 뒤라 패널이 그대로 찍힌다. 여기서 끄면 이 coroutine 이 Update 에서 시작했든 직전 캡처의
            // end-of-frame 에서 이어졌든 grab 하는 프레임은 패널 없이 그려진다 — 후자에서 던진
            // WaitForEndOfFrame 은 다음 프레임 끝에 깨어나기 때문이다.
            //
            // 대신 사람이 보는 화면과 같은 back buffer 를 읽는 WebRTC stream 에서 캡처 한 번마다 한 프레임
            // 패널이 사라진다. 이미지에서 패널을 빼는 값으로 받아들인 것이다 (ARTEL-881).
            if (keyboardStatus != null)
            {
                keyboardStatus.HideForCapture();
            }

            try
            {
                // The screenshot reads the back buffer, so it is only correct once everything for
                // this frame has been drawn — including the overlay UI this path exists for.
                yield return endOfFrame;

                var screenWidth = Mathf.Max(2, Screen.width);
                var screenHeight = Mathf.Max(2, Screen.height);
                var source = pixelRect ?? new Rect(0f, 0f, screenWidth, screenHeight);
                var sourceWidth = Mathf.Max(1, Mathf.RoundToInt(source.width));
                var sourceHeight = Mathf.Max(1, Mathf.RoundToInt(source.height));
                var size = CaptureRect.Downscale(sourceWidth, sourceHeight, request.MaxEdge);

                RenderTexture screen = null;
                RenderTexture scaled = null;
                Texture2D readback = null;
                var previous = RenderTexture.active;
                try
                {
                    screen = RenderTexture.GetTemporary(
                        screenWidth, screenHeight, 0, RenderTextureFormat.BGRA32);
                    // 다른 end-of-frame 소비자가 남긴 target으로 back buffer grab이 향하지 않게 한다.
                    // 호출자가 쓰던 target은 finally에서 되돌려 이 캡처도 전역 렌더 상태를 새지 않는다.
                    RenderTexture.active = null;
                    ScreenCapture.CaptureScreenshotIntoRenderTexture(screen);

                    scaled = RenderTexture.GetTemporary(
                        size.x, size.y, 0, RenderTextureFormat.BGRA32);

                    // The screenshot is written in framebuffer orientation, which under D3D is
                    // upside down relative to what an encoder expects. The flip is not optional:
                    // getting it wrong produces a working-but-inverted image, which passes a smoke
                    // test and is only caught against on-screen text.
                    //
                    // Crop and flip ride in the same blit: the blit samples `uv * scale + offset`.
                    // The rectangle is in Unity screen coordinates, whose origin is bottom-left, so
                    // output row v must read screen row `yMin + v * height`; the buffer holds that
                    // upside down, which is where the negative y and the `1 -` come from.
                    var scale = new Vector2(
                        source.width / screenWidth, -source.height / screenHeight);
                    var offset = new Vector2(
                        source.xMin / screenWidth,
                        1f - source.yMin / screenHeight);
                    Graphics.Blit(screen, scaled, scale, offset);

                    // Only the synchronous work is wrapped. A marker spanning the end-of-frame wait
                    // above would report the idle time as capture cost.
                    using (ArtelProfilerMarkers.CaptureReadback.Auto())
                    {
                        readback = new Texture2D(size.x, size.y, TextureFormat.RGBA32, false);
                        RenderTexture.active = scaled;
                        readback.ReadPixels(new Rect(0f, 0f, size.x, size.y), 0, 0, false);
                        readback.Apply(false);
                    }

                    byte[] bytes;
                    using (ArtelProfilerMarkers.CaptureEncode.Auto())
                    {
                        bytes = request.UsePng
                            ? readback.EncodeToPNG()
                            : readback.EncodeToJPG(CaptureRequestReader.JpegQuality);
                    }

                    if (bytes == null || bytes.Length == 0)
                    {
                        completed(CapturedImage.Failed("The captured image could not be encoded."));
                        yield break;
                    }

                    completed(new CapturedImage
                    {
                        Bytes = bytes,
                        Width = size.x,
                        Height = size.y
                    });
                }
                finally
                {
                    // Every capture allocates three native objects. Leaking any of them kills a
                    // long run rather than the capture that caused it, so release runs even on
                    // failure.
                    RenderTexture.active = previous;
                    if (screen != null)
                    {
                        RenderTexture.ReleaseTemporary(screen);
                    }

                    if (scaled != null)
                    {
                        RenderTexture.ReleaseTemporary(scaled);
                    }

                    if (readback != null)
                    {
                        UnityEngine.Object.Destroy(readback);
                    }
                }
            }
            finally
            {
                // 안쪽 finally 와 합치지 않는 이유는 `RenderTexture.active` 를 읽는 자리를 옮기지 않기
                // 위해서다. 그 값은 end-of-frame 의 것이어야 한다 — ScreenVideoSource 가 같은 프레임 끝에
                // 남긴 target 을 되돌리는 것이 그 복원의 목적이다.
                if (keyboardStatus != null)
                {
                    keyboardStatus.ShowAfterCapture();
                }
            }
        }
    }
}
