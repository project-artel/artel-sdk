using System.Collections.Generic;
using Newtonsoft.Json;

namespace Artel.Protocol.Dto
{
    internal sealed class GameIdentityCatalogMessageDto
    {
        [JsonProperty("type")]
        public string Type { get; set; } = "GAME_IDENTITY_CATALOG";

        [JsonProperty("identities")]
        public List<GameIdentityEntryDto> Identities { get; set; } = new List<GameIdentityEntryDto>();
    }

    internal sealed class GameIdentityEntryDto
    {
        [JsonProperty("name")]
        public string Name { get; set; }
        [JsonProperty("kind")]
        public string Kind { get; set; }
        [JsonProperty("base", NullValueHandling = NullValueHandling.Include)]
        public int? Base { get; set; }
        [JsonProperty("meaning")]
        public string Meaning { get; set; }
        [JsonProperty("changesWhen")]
        public string ChangesWhen { get; set; }
        [JsonProperty("identifies")]
        public string Identifies { get; set; }
        [JsonProperty("role")]
        public string Role { get; set; }
    }
}
