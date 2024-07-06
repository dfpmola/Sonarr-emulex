using Newtonsoft.Json;

namespace NzbDrone.Core.Download.Clients.Emule.Types
{
    public sealed class TorrentContent
    {
        [JsonProperty(PropertyName = "path")]
        public string Path { get; set; }
    }
}
