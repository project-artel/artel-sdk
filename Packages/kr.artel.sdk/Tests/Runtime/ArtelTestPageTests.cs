using NUnit.Framework;

namespace Artel.Tests
{
    public sealed class ArtelTestPageTests
    {
        [Test]
        public void Html_OffersLocalWebSocketViewerControls()
        {
            Assert.That(ArtelTestPage.Html, Does.Contain("id=\"stream-start\""));
            Assert.That(ArtelTestPage.Html, Does.Contain("id=\"stream-stop\""));
            Assert.That(ArtelTestPage.Html, Does.Contain("id=\"stream-video\""));
            Assert.That(ArtelTestPage.Html, Does.Contain("image.decode()"));
        }

        [Test]
        public void Html_UsesExistingStreamingWireContract()
        {
            Assert.That(ArtelTestPage.Html, Does.Contain("type: 'STREAM_START'"));
            Assert.That(ArtelTestPage.Html, Does.Contain("type: 'STREAM_RENEW'"));
            Assert.That(ArtelTestPage.Html, Does.Contain("type: 'STREAM_STOP'"));
            Assert.That(ArtelTestPage.Html, Does.Contain("message.type === 'STREAM_FRAME'"));
            Assert.That(ArtelTestPage.Html, Does.Contain("type: 'STREAM_FRAME_ACK'"));
            Assert.That(ArtelTestPage.Html, Does.Contain("transport: 'JPEG_WS'"));
        }

        [Test]
        public void Html_DeclaresTheViewerCleanupBoundaries()
        {
            Assert.That(ArtelTestPage.Html, Does.Contain("ws.onclose = () => { status.textContent = 'closed'; stopStream(false); }"));
            Assert.That(ArtelTestPage.Html, Does.Contain("window.addEventListener('beforeunload', () => stopStream(true))"));
            Assert.That(ArtelTestPage.Html, Does.Contain("clearInterval(streamRenewTimer)"));
            Assert.That(ArtelTestPage.Html, Does.Contain("streamVideo.removeAttribute('src')"));
        }

        [Test]
        public void Html_SerializesCapturesSoTheirResultsRemainAddressable()
        {
            Assert.That(ArtelTestPage.Html, Does.Contain("captureButton.disabled = true"));
            Assert.That(ArtelTestPage.Html, Does.Contain("entry.id === pendingCaptureId"));
            Assert.That(ArtelTestPage.Html, Does.Contain("captureButton.disabled = false"));
        }
    }
}
