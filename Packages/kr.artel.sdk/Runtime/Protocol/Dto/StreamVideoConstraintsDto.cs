using Newtonsoft.Json;

namespace Artel.Protocol.Dto
{
    /// <summary>
    /// Encoding ceiling. The server holds these because they decide what the game pays per frame,
    /// not what the viewer would like to receive.
    /// </summary>
    internal sealed class StreamVideoConstraintsDto
    {
        [JsonProperty("maxWidth")]
        public int MaxWidth { get; set; }

        [JsonProperty("maxHeight")]
        public int MaxHeight { get; set; } = 720;

        [JsonProperty("maxFramerate")]
        public int MaxFramerate { get; set; }

        [JsonProperty("jpegQuality")]
        public int JpegQuality { get; set; } = 65;

        [JsonProperty("maxFrameBytes")]
        public int MaxFrameBytes { get; set; } = 131072;
    }
}
