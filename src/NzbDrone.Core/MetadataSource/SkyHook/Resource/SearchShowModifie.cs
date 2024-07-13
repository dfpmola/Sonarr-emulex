namespace NzbDrone.Core.MetadataSource.SkyHook.Resource
{
    public class SearchShowModifie
    {
        public SearchShowModifie()
        {
        }

        public string Name { get; set; }
        public string Overview { get; set; }

        public int TvdbId { get; set; }
    }
}
