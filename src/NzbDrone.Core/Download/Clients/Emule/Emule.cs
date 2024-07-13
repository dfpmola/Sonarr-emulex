using System;
using System.Collections.Generic;
using System.Linq;
using FluentValidation.Results;
using NLog;
using NzbDrone.Common.Disk;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Http;
using NzbDrone.Core.Blocklisting;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Download.Clients.Emule.Models;
using NzbDrone.Core.Localization;
using NzbDrone.Core.MediaFiles.TorrentInfo;
using NzbDrone.Core.Organizer;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.RemotePathMappings;
using NzbDrone.Core.ThingiProvider;

namespace NzbDrone.Core.Download.Clients.Emule
{
    public class Emule : TorrentClientBase<EmuleSettings>
    {
        private readonly IEmuleProxy _proxy;
        private readonly IDownloadSeedConfigProvider _downloadSeedConfigProvider;

        public Emule(IEmuleProxy proxy,
                        IDownloadSeedConfigProvider downloadSeedConfigProvider,
                        ITorrentFileInfoReader torrentFileInfoReader,
                        IHttpClient httpClient,
                        IConfigService configService,
                        INamingConfigService namingConfigService,
                        IDiskProvider diskProvider,
                        IRemotePathMappingService remotePathMappingService,
                        ILocalizationService localizationService,
                        IBlocklistService blocklistService,
                        Logger logger)
            : base(torrentFileInfoReader, httpClient, configService, diskProvider, remotePathMappingService, localizationService, blocklistService, logger)
        {
            _proxy = proxy;
            _downloadSeedConfigProvider = downloadSeedConfigProvider;
        }

        private static IEnumerable<string> HandleTags(RemoteEpisode remoteEpisode, EmuleSettings settings)
        {
            var result = new HashSet<string>();

            if (settings.AdditionalTags.Any())
            {
                foreach (var additionalTag in settings.AdditionalTags)
                {
                    switch (additionalTag)
                    {
                        case (int)AdditionalTags.TitleSlug:
                            result.Add(remoteEpisode.Series.TitleSlug);
                            break;
                        case (int)AdditionalTags.Quality:
                            result.Add(remoteEpisode.ParsedEpisodeInfo.Quality.Quality.ToString());
                            break;
                        case (int)AdditionalTags.Languages:
                            result.UnionWith(remoteEpisode.Languages.ConvertAll(language => language.ToString()));
                            break;
                        case (int)AdditionalTags.ReleaseGroup:
                            result.Add(remoteEpisode.ParsedEpisodeInfo.ReleaseGroup);
                            break;
                        case (int)AdditionalTags.Year:
                            result.Add(remoteEpisode.Series.Year.ToString());
                            break;
                        case (int)AdditionalTags.Indexer:
                            result.Add(remoteEpisode.Release.Indexer);
                            break;
                        case (int)AdditionalTags.Network:
                            result.Add(remoteEpisode.Series.Network);
                            break;
                        default:
                            throw new DownloadClientException("Unexpected additional tag ID");
                    }
                }
            }

            return result.Where(t => t.IsNotNullOrWhiteSpace());
        }

        public override string Name => "Emule";
        public override ProviderMessage Message => new ProviderMessage(_localizationService.GetLocalizedString("DownloadClientFloodSettingsRemovalInfo"), ProviderMessageType.Info);

        protected override string AddFromTorrentFile(RemoteEpisode remoteEpisode, string hash, string filename, byte[] fileContent)
        {
            _proxy.AddTorrentByFile(Convert.ToBase64String(fileContent), HandleTags(remoteEpisode, Settings), Settings);

            return hash;
        }

        protected override string AddFromMagnetLink(RemoteEpisode remoteEpisode, string hash, string magnetLink)
        {
            _proxy.AddTorrentByUrl(magnetLink, remoteEpisode, HandleTags(remoteEpisode, Settings), Settings);

            return hash;
        }

        public override IEnumerable<DownloadClientItem> GetItems()
        {
            var items = new List<DownloadClientItem>();

            var list = _proxy.GetTorrents(Settings);

            foreach (var ed2k in list)
            {
                /*
                var properties = ed2k.Value;
                */
                /*
                if (!Settings.Tags.All(tag => properties.Tags.Contains(tag)))
                {
                    continue;
                }
                */

                var item = new DownloadClientItem
                {
                    DownloadClientInfo = DownloadClientItemClientInfo.FromDownloadClient(this, false),
                    DownloadId = ed2k.Hash,
                    Title = ed2k.Name,
                    OutputPath = _remotePathMappingService.RemapRemoteToLocal(Settings.Host, new OsPath(ed2k.Directory)),
                    Category = "sonarr",
                    RemainingSize = ed2k.SizeBytes - ed2k.BytesDone,
                    TotalSize = ed2k.SizeBytes,
                    SeedRatio = ed2k.Ratio,
                    Message = ed2k.Message,
                    CanMoveFiles = false,
                    CanBeRemoved = false,
                };

                if (ed2k.Eta > 0)
                {
                    item.RemainingTime = TimeSpan.FromSeconds(ed2k.Eta);
                }

                if (ed2k.Status.Contains("seeding") || ed2k.Status.Contains("" +
                    "" +
                    "Completed"))
                {
                    item.Status = DownloadItemStatus.Completed;
                }
                else if (ed2k.Status.Contains("Stopped") || ed2k.Status.Contains("Paused"))
                {
                    item.Status = DownloadItemStatus.Paused;
                }
                else if (ed2k.Status.Contains("error"))
                {
                    item.Status = DownloadItemStatus.Warning;
                }
                else if (ed2k.Status.Contains("downloading") || ed2k.Status.Contains("Waiting"))
                {
                    item.Status = DownloadItemStatus.Downloading;
                }

                if (item.Status == DownloadItemStatus.Completed)
                {
                    item.CanMoveFiles = item.CanBeRemoved = true;

                    // Grab cached seedConfig
                    /*
                    var seedConfig = _downloadSeedConfigProvider.GetSeedConfiguration(item.DownloadId);

                    if (seedConfig != null)
                    {
                        if (item.SeedRatio >= seedConfig.Ratio)
                        {
                            // Check if seed ratio reached
                            item.CanMoveFiles = item.CanBeRemoved = true;
                        }
                        else if (ed2k.DateFinished != null && ed2k.DateFinished > 0)
                        {
                            // Check if seed time reached
                            if ((DateTimeOffset.Now - DateTimeOffset.FromUnixTimeSeconds((long)ed2k.DateFinished)) >= seedConfig.SeedTime)
                            {
                                item.CanMoveFiles = item.CanBeRemoved = true;
                            }
                        }
                    }
                    */
                }

                items.Add(item);
            }

            return items;
        }

        public override DownloadClientItem GetImportItem(DownloadClientItem item, DownloadClientItem previousImportAttempt)
        {
            var result = item.Clone();

            // var contentPaths = _proxy.GetTorrentContentPaths(item.DownloadId, Settings);
            result.OutputPath = item.OutputPath + new OsPath(item.Title);

            return result;

            /*
if (!item.OutputPath.IsEmpty)
{
    // item.OutputPath = item.OutputPath + new OsPath(item.Title);

    item.OutputPath = item.OutputPath;

    return item;
}


var contentPaths = _proxy.GetTorrentContentPaths(item.DownloadId, Settings);

if (contentPaths.Count < 1)
{
    throw new DownloadClientUnavailableException($"Failed to fetch list of contents of torrent: {item.DownloadId}");
}


if (contentPaths.Count == 1)
{
    // For single-file torrent, OutputPath should be the path of file.
    result.OutputPath = item.OutputPath + new OsPath(contentPaths[0]);
}


            var outputPath = result.OutputPath + result.Title;

            result.OutputPath = _remotePathMappingService.RemapRemoteToLocal(Settings.Host, outputPath);

            return result;
             */
        }

        public override void MarkItemAsImported(DownloadClientItem downloadClientItem)
        {
            /*
            if (Settings.PostImportTags.Any())
            {
                var list = _proxy.GetTorrents(Settings);

                if (list.ContainsKey(downloadClientItem.DownloadId))
                {
                    _proxy.SetTorrentsTags(downloadClientItem.DownloadId,
                        list[downloadClientItem.DownloadId].Tags.Concat(Settings.PostImportTags).ToHashSet(),
                        Settings);
                }
            }*/
        }

        public override void RemoveItem(DownloadClientItem item, bool deleteData)
        {
            _proxy.DeleteTorrent(item.DownloadId, deleteData, Settings);
        }

        public override DownloadClientInfo GetStatus()
        {
            var destDir = _proxy.GetClientSettings(Settings).DirectoryDefault;

            if (Settings.Destination.IsNotNullOrWhiteSpace())
            {
                destDir = Settings.Destination;
            }

            return new DownloadClientInfo
            {
                IsLocalhost = Settings.Host == "127.0.0.1" || Settings.Host == "::1" || Settings.Host == "localhost",
                OutputRootFolders = new List<OsPath> { _remotePathMappingService.RemapRemoteToLocal(Settings.Host, new OsPath(destDir)) }
            };
        }

        protected override void Test(List<ValidationFailure> failures)
        {
            try
            {
                _proxy.CheckStatus(Settings);
            }
            catch (DownloadClientAuthenticationException ex)
            {
                failures.Add(new ValidationFailure("Password", ex.Message));
            }
            catch (Exception ex)
            {
                failures.Add(new ValidationFailure("Host", ex.Message));
            }
        }
    }
}
