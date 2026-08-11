using System.Collections.Generic;
using Newtonsoft.Json;

namespace Artel.Protocol.Dto
{
    /// <summary>
    /// The scene map as it sits on disk, written at build time and read back at runtime.
    /// </summary>
    /// <remarks>
    /// Deliberately not <see cref="AllScenesMessageDto"/>. That type carries a session's message
    /// <c>type</c> and <c>id</c>, which a file has no use for, and it has no version to check —
    /// the codec ignores members it does not know, so a map written by an older SDK would read
    /// back quietly missing whatever fields the scan has grown since.
    /// </remarks>
    public sealed class SceneMapDocumentDto
    {
        [JsonProperty("formatVersion")]
        public int FormatVersion { get; set; }

        [JsonProperty("scenes")]
        public List<ScannedSceneDto> Scenes { get; set; } = new List<ScannedSceneDto>();
    }
}
