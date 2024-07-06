using Newtonsoft.Json;

namespace NzbDrone.Core.Download.Clients.Emule.Types
{
    public sealed class Ed2k
    {
        [JsonProperty(PropertyName = "_downloadedSize")]
        public long BytesDone { get; set; }

        [JsonProperty(PropertyName = "_path")]
        public string Directory { get; set; }

        [JsonProperty(PropertyName = "_eta")]
        public long Eta { get; set; }

        [JsonProperty(PropertyName = "message")]
        public string Message { get; set; }

        [JsonProperty(PropertyName = "_fileName")]
        public string Name { get; set; }

        [JsonProperty(PropertyName = "_hash")]
        public string Hash { get; set; }

        [JsonProperty(PropertyName = "ratio")]
        public float Ratio { get; set; }

        [JsonProperty(PropertyName = "_size")]
        public long SizeBytes { get; set; }

        [JsonProperty(PropertyName = "_status")]
        public string Status { get; set; }

        [JsonProperty(PropertyName = "tags")]
        public string Tags { get; set; }

        // added in Flood 4.5
    }
}
