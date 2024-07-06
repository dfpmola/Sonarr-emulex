using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using NLog;
using NzbDrone.Common.Cache;
using NzbDrone.Common.Http;
using NzbDrone.Common.Serializer;
using NzbDrone.Core.Download.Clients.Emule.Types;
using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.Download.Clients.Emule
{
    public interface IEmuleProxy
    {
        void CheckStatus(EmuleSettings settings);

        void AuthVerify(EmuleSettings settings);
        void AddTorrentByUrl(string url, RemoteEpisode remoteEpisode, IEnumerable<string> tags, EmuleSettings settings);
        void AddTorrentByFile(string file, IEnumerable<string> tags, EmuleSettings settings);
        void DeleteTorrent(string hash, bool deleteData, EmuleSettings settings);
        List<Ed2k> GetTorrents(EmuleSettings settings);
        List<string> GetTorrentContentPaths(string hash, EmuleSettings settings);
        void SetTorrentsTags(string hash, IEnumerable<string> tags, EmuleSettings settings);
        EmuleClientSettings GetClientSettings(EmuleSettings settings);
    }

    public class EmuleProxy : IEmuleProxy
    {
        private readonly IHttpClient _httpClient;
        private readonly Logger _logger;
        private readonly ICached<Dictionary<string, string>> _authCookieCache;

        public EmuleProxy(IHttpClient httpClient, ICacheManager cacheManager, Logger logger)
        {
            _httpClient = httpClient;
            _logger = logger;
            _authCookieCache = cacheManager.GetCache<Dictionary<string, string>>(GetType(), "authCookies");
        }

        private string BuildUrl(EmuleSettings settings)
        {
            return $"{(settings.UseSsl ? "https://" : "http://")}{settings.Host}:{settings.Port}/{settings.UrlBase}";
        }

        /*
        private string BuildCachedCookieKey(EmuleSettings settings)
        {
            // return $"{BuildUrl(settings)}:{settings.Username}";
        }
        */

        private HttpRequestBuilder BuildRequest(EmuleSettings settings)
        {
            var requestBuilder = new HttpRequestBuilder(HttpUri.CombinePath(BuildUrl(settings), "/emulex"))
            {
                LogResponseContent = true,

                // NetworkCredential = new NetworkCredential(settings.Username, settings.Password)
            };

            requestBuilder.Headers.Add("X-API-KEY", settings.ApiKey);

            // requestBuilder.SetCookies(AuthAuthenticate(requestBuilder, settings));

            return requestBuilder;
        }

        private HttpResponse HandleRequest(HttpRequest request, EmuleSettings settings)
        {
            try
            {
                return _httpClient.Execute(request);
            }
            catch (HttpException ex)
            {
                if (ex.Response.StatusCode == HttpStatusCode.Forbidden ||
                    ex.Response.StatusCode == HttpStatusCode.Unauthorized)
                {
                    // _authCookieCache.Remove(BuildCachedCookieKey(settings));
                    throw new DownloadClientAuthenticationException("Failed to authenticate with Flood.");
                }

                throw new DownloadClientException("Unable to connect to Flood, please check your settings");
            }
            catch
            {
                throw new DownloadClientException("Unable to connect to Flood, please check your settings");
            }
        }

        /*
        private Dictionary<string, string> AuthAuthenticate(HttpRequestBuilder requestBuilder, EmuleSettings settings, bool force = false)
        {
            var cachedCookies = _authCookieCache.Find(BuildCachedCookieKey(settings));

            if (cachedCookies == null || force)
            {
                var authenticateRequest = requestBuilder.Resource("/auth/authenticate").Post().Build();

                var body = new Dictionary<string, object>
                {
                    { "username", settings.Username },
                    { "password", settings.Password }
                };
                authenticateRequest.SetContent(body.ToJson());

                var response = HandleRequest(authenticateRequest, settings);
                cachedCookies = response.GetCookies();
                _authCookieCache.Set(BuildCachedCookieKey(settings), cachedCookies);
            }

            return cachedCookies;
        }
        */

        public void AuthVerify(EmuleSettings settings)
        {
            var verifyRequest = BuildRequest(settings).Resource("/auth/verify").Build();

            verifyRequest.Method = HttpMethod.Get;

            HandleRequest(verifyRequest, settings);
        }

        public void AddTorrentByFile(string file, IEnumerable<string> tags, EmuleSettings settings)
        {
            var addRequest = BuildRequest(settings).Resource("/torrents/add-files").Post().Build();

            var body = new Dictionary<string, object>
            {
                { "files", new List<string> { file } },
                { "tags", tags.ToList() }
            };

            if (settings.Destination != null)
            {
                body.Add("destination", settings.Destination);
            }

            if (!settings.AddPaused)
            {
                body.Add("start", true);
            }

            addRequest.SetContent(body.ToJson());

            HandleRequest(addRequest, settings);
        }

        public void AddTorrentByUrl(string url, RemoteEpisode remoteEpisode, IEnumerable<string> tags, EmuleSettings settings)
        {
            var addRequest = BuildRequest(settings).Resource("/download").Post().AddQueryParam("category", settings.MovieCategory).Build();

            var cadena = url;
            var inicio = "magnet:?xt=urn:btih:";
            var fin = "99999999";

            // Encontrar las posiciones de inicio y fin de la cadena que deseas extraer
            var startIndex = cadena.IndexOf(inicio) + inicio.Length;
            var endIndex = cadena.IndexOf(fin, startIndex);

            // Extraer la cadena deseada
            var cadenaExtraida = cadena.Substring(startIndex, endIndex - startIndex);

            var ed2kurlFormater = "ed2k://|file|" + Uri.EscapeDataString(remoteEpisode.Release.Title)  + "|" + remoteEpisode.Release.Size + "|" + cadenaExtraida + "|/";

            var body = new Dictionary<string, object> { };
            body.Add("ed2kurl", ed2kurlFormater);

            /*
            if (settings.Destination != null)
            {
                body.Add("destination", settings.Destination);
            }

            if (!settings.AddPaused)
            {
                body.Add("start", true);
            }
            */

            addRequest.Headers.ContentType = "application/json";

            addRequest.SetContent(body.ToJson());

            HandleRequest(addRequest, settings);
        }

        public void DeleteTorrent(string hash, bool deleteData, EmuleSettings settings)
        {
            var deleteRequest = BuildRequest(settings).Resource("/delete").Post().Build();

            var body = new Dictionary<string, object> { };
            body.Add("hash", hash);

            deleteRequest.Headers.ContentType = "application/json";

            deleteRequest.SetContent(body.ToJson());

            HandleRequest(deleteRequest, settings);
        }

        public List<Ed2k> GetTorrents(EmuleSettings settings)
        {
            var getTorrentsRequest = BuildRequest(settings).Resource("/downloads").AddQueryParam("category", settings.MovieCategory).Build();

            getTorrentsRequest.Method = HttpMethod.Get;

            return Json.Deserialize<List<Ed2k>>(HandleRequest(getTorrentsRequest, settings).Content);
        }

        public List<string> GetTorrentContentPaths(string hash, EmuleSettings settings)
        {
            var contentsRequest = BuildRequest(settings).Resource($"/torrents/{hash}/contents").Build();

            contentsRequest.Method = HttpMethod.Get;

            return Json.Deserialize<List<TorrentContent>>(HandleRequest(contentsRequest, settings).Content).ConvertAll(content => content.Path);
        }

        public void SetTorrentsTags(string hash, IEnumerable<string> tags, EmuleSettings settings)
        {
            var tagsRequest = BuildRequest(settings).Resource("/torrents/tags").Build();

            tagsRequest.Method = HttpMethod.Patch;

            var body = new Dictionary<string, object>
            {
                { "hashes", new List<string> { hash } },
                { "tags", tags.ToList() }
            };
            tagsRequest.SetContent(body.ToJson());

            HandleRequest(tagsRequest, settings);
        }

        public EmuleClientSettings GetClientSettings(EmuleSettings settings)
        {
            var contentsRequest = BuildRequest(settings).Resource($"/client/settings").Build();

            contentsRequest.Method = HttpMethod.Get;

            return Json.Deserialize<EmuleClientSettings>(HandleRequest(contentsRequest, settings).Content);
        }

        void IEmuleProxy.CheckStatus(EmuleSettings settings)
        {
            var verifyRequest = BuildRequest(settings).Resource("/status").Build();

            verifyRequest.Method = HttpMethod.Get;

            HandleRequest(verifyRequest, settings);
        }
    }
}
