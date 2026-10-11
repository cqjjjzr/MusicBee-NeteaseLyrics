using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Threading;

namespace MusicBeePlugin
{
    /* Encryption protocol adopted from ZonyLrcToolsX; see LICENSE-Zony. */
    internal static class NeteaseApi
    {
        public static IEnumerable<SearchResultSong> Search(string query, LookupSession session)
        {
            if (string.IsNullOrWhiteSpace(query) || !session.CanSearch) return Enumerable.Empty<SearchResultSong>();
            return session.SearchCached(query, () =>
            {
                var data = new Dictionary<string, object>
                {
                    { "csrf_token", "" }, { "s", query }, { "offset", 0 }, { "type", 1 }, { "limit", 20 }
                };
                var result = RequestNewApi<SearchResult>(session, "search/new",
                    "https://music.163.com/weapi/search/get", data, ValidSearch);
                if (result == null)
                    return SearchLegacy(query, session).ToArray();
                return UsableSongs(result);
            });
        }

        internal static IEnumerable<SearchResultSong> SearchLegacy(string query, LookupSession session)
        {
            if (string.IsNullOrWhiteSpace(query) || !session.CanSearch) return Enumerable.Empty<SearchResultSong>();
            return session.SearchCached("\0legacy:" + query, () =>
            {
                var route = "/api/search/get?s=" + Uri.EscapeDataString(query) + "&type=1&offset=0&total=true&limit=6";
                var result = session.Request<SearchResult>("search/legacy", HttpMethod.Get,
                    "https://music.163.com" + route, null, ValidSearch, RequestInterface.LegacyHttps, true);
                if (result == null && session.CanUseHttpFallback)
                    result = session.Request<SearchResult>("search/http", HttpMethod.Get,
                        "http://music.163.com" + route, null, ValidSearch, RequestInterface.LegacyHttp, true);
                return UsableSongs(result);
            });
        }

        private static SearchResultSong[] UsableSongs(SearchResult result)
        {
            return (result?.result?.songs ?? Enumerable.Empty<SearchResultSong>())
                .Where(song => song != null && song.id > 0 && !string.IsNullOrWhiteSpace(song.name)).ToArray();
        }

        private static bool ValidSearch(SearchResult result)
        {
            return result != null && result.code == 200 && result.result != null &&
                result.result.songCount.HasValue && result.result.songCount >= 0 &&
                (result.result.songCount == 0 || result.result.songs != null);
        }

        public static LyricResult RequestLyric(long id, LookupSession session)
        {
            if (id <= 0) return null;
            var data = new Dictionary<string, object>
            {
                { "os", "pc" }, { "id", id }, { "lv", -1 }, { "kv", -1 }, { "tv", -1 }, { "rv", -1 }
            };
            var result = RequestNewApi<LyricResult>(session, "lyrics/new",
                "https://music.163.com/weapi/song/lyric?csrf_token=", data, ValidLyrics);
            if (result != null) return result;
            var route = "/api/song/lyric?os=pc&id=" + id + "&lv=-1&kv=-1&tv=-1";
            result = session.Request<LyricResult>("lyrics/legacy", HttpMethod.Get,
                "https://music.163.com" + route, null, ValidLyrics, RequestInterface.LegacyHttps);
            if (result == null && session.CanUseHttpFallback)
                result = session.Request<LyricResult>("lyrics/http", HttpMethod.Get,
                    "http://music.163.com" + route, null, ValidLyrics, RequestInterface.LegacyHttp);
            return result;
        }

        private static bool ValidLyrics(LyricResult result)
        {
            // An explicit instrumental response is a valid absence. A bare code=200 is not.
            return result != null && result.code == 200 &&
                (IsInstrumental(result) || !string.IsNullOrWhiteSpace(result.lrc?.lyric));
        }

        internal static bool IsInstrumental(LyricResult result) => result?.nolyric == true ||
            LyricProcessor.IsInstrumentalPlaceholder(result?.lrc?.lyric);

        private static T RequestNewApi<T>(LookupSession session, string label, string url,
            Dictionary<string, object> data, Func<T, bool> validate) where T : class
        {
            if (!session.CanRequest(RequestInterface.NewApi)) return null;
            using (var content = new FormUrlEncodedContent(EncryptRequest(data)))
                return session.Request(label, HttpMethod.Post, url, content, validate,
                    RequestInterface.NewApi, label.StartsWith("search/", StringComparison.Ordinal));
        }

        private static Dictionary<string, string> EncryptRequest(object data)
        {
            var secret = NeteaseMusicEncryptionHelper.CreateSecretKey(16);
            return new Dictionary<string, string>
            {
                { "params", NeteaseMusicEncryptionHelper.AesEncode(
                    NeteaseMusicEncryptionHelper.AesEncode(JsonConvert.SerializeObject(data),
                        NeteaseMusicEncryptionHelper.Nonce), secret) },
                { "encSecKey", NeteaseMusicEncryptionHelper.RsaEncode(secret) }
            };
        }
    }

    internal enum RequestInterface { Other, NewApi, LegacyHttps, LegacyHttp }
    internal enum RequestOutcome { None, Success, TransportFailure, Rejected, InvalidResponse }

    // A synchronous MusicBee callback gets one client and one deadline, including all fallbacks.
    // The handler seam lets regression tests exercise the real request and parsing code offline.
    internal sealed class LookupSession : IDisposable
    {
        internal const int MaxSearchRequests = 9;
        internal const int MaxLyricRequests = 9;
        private int _searchRequests, _lyricRequests;
        private bool _secureRefusal;
        private readonly Dictionary<RequestInterface, RequestOutcome> _outcomes = new Dictionary<RequestInterface, RequestOutcome>();
        private readonly HttpClient _client;
        private readonly CancellationTokenSource _deadline;
        private readonly CancellationTokenSource _searchDeadline;
        private readonly TimeSpan _requestTimeout;
        private readonly TimeSpan _lookupTimeout, _searchTimeout;
        private readonly Stopwatch _elapsed = Stopwatch.StartNew();
        private readonly HashSet<RequestInterface> _unavailable = new HashSet<RequestInterface>();
        private readonly Action<string> _trace;
        private readonly Dictionary<string, SearchResultSong[]> _searches = new Dictionary<string, SearchResultSong[]>();

        internal LookupSession(Action<string> trace = null, HttpMessageHandler handler = null,
            TimeSpan? requestTimeout = null, TimeSpan? lookupTimeout = null, TimeSpan? lyricReserve = null)
        {
            _trace = trace;
            _requestTimeout = requestTimeout ?? TimeSpan.FromSeconds(8);
            _lookupTimeout = lookupTimeout ?? TimeSpan.FromSeconds(30);
            var reserve = lyricReserve ?? TimeSpan.FromTicks(_lookupTimeout.Ticks * 2 / 5);
            if (reserve <= TimeSpan.Zero || reserve >= _lookupTimeout) throw new ArgumentOutOfRangeException(nameof(lyricReserve));
            _searchTimeout = _lookupTimeout - reserve;
            _deadline = new CancellationTokenSource(_lookupTimeout);
            _searchDeadline = CancellationTokenSource.CreateLinkedTokenSource(_deadline.Token);
            _searchDeadline.CancelAfter(_searchTimeout);
            // These are anonymous lookups. Server-set cookies can turn subsequent searches
            // into unrelated code=200 answers; keep each request's public context unchanged.
            _client = new HttpClient(handler ?? new HttpClientHandler { UseCookies = false, AllowAutoRedirect = false });
            _client.Timeout = Timeout.InfiniteTimeSpan;
            _client.MaxResponseContentBufferSize = 4 * 1024 * 1024;
            _client.DefaultRequestHeaders.Referrer = new Uri("https://music.163.com/");
            _client.DefaultRequestHeaders.UserAgent.ParseAdd(
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36");
        }

        internal bool AllowInsecureHttp { get; set; } = true;
        internal bool CanUseHttpFallback => AllowInsecureHttp && !_secureRefusal &&
            _outcomes.TryGetValue(RequestInterface.LegacyHttps, out var outcome) && outcome == RequestOutcome.TransportFailure;
        internal bool CanSearch => _searchRequests < MaxSearchRequests &&
            !_searchDeadline.IsCancellationRequested && _elapsed.Elapsed < _searchTimeout;
        internal bool CanRequest(RequestInterface api) => !_unavailable.Contains(api) &&
            (api != RequestInterface.LegacyHttp || AllowInsecureHttp);

        private int AttemptsRemaining(RequestInterface api)
        {
            var ordered = new[] { RequestInterface.NewApi, RequestInterface.LegacyHttps, RequestInterface.LegacyHttp };
            var index = Array.IndexOf(ordered, api);
            return index < 0 ? 1 : Math.Max(1, ordered.Skip(index).Count(CanRequest));
        }

        internal IEnumerable<SearchResultSong> SearchCached(string query, Func<SearchResultSong[]> search)
        {
            if (!_searches.TryGetValue(query, out var result))
                _searches[query] = result = search();
            return result;
        }

        internal T Request<T>(string label, HttpMethod method, string url, HttpContent content,
            Func<T, bool> validate, RequestInterface api = RequestInterface.Other, bool search = false) where T : class
        {
            if (_deadline.IsCancellationRequested || !CanRequest(api)) return null;
            if ((search && _searchRequests >= MaxSearchRequests) ||
                (!search && _lyricRequests >= MaxLyricRequests))
            {
                Trace(search ? "Search request limit reached" : "Lyric request limit reached");
                return null;
            }
            var remaining = (search ? _searchTimeout : _lookupTimeout) - _elapsed.Elapsed;
            if (remaining <= TimeSpan.Zero || (search && !CanSearch))
            {
                Trace(label + (search ? ": search budget exhausted" : ": lookup deadline reached"));
                return null;
            }
            // Each pending fallback gets a share of the remaining phase, capped per request.
            var requestBudget = TimeSpan.FromTicks(Math.Min(_requestTimeout.Ticks, remaining.Ticks / AttemptsRemaining(api)));
            try
            {
                using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(search ? _searchDeadline.Token : _deadline.Token))
                using (var request = new HttpRequestMessage(method, url) { Content = content })
                {
                    if (api == RequestInterface.LegacyHttps || api == RequestInterface.LegacyHttp)
                        request.Headers.TryAddWithoutValidation("Cookie", "appver=1.5.0.75771;");
                    timeout.CancelAfter(requestBudget);
                    if (search) _searchRequests++; else _lyricRequests++;
                    _outcomes[api] = RequestOutcome.None;
                    using (var response = _client.SendAsync(request, timeout.Token).GetAwaiter().GetResult())
                    {
                        if (!response.IsSuccessStatusCode)
                        {
                            _outcomes[api] = RequestOutcome.Rejected;
                            if (request.RequestUri.Scheme == "https") _secureRefusal = true;
                            Trace(label + ": HTTP " + (int)response.StatusCode);
                            return null;
                        }
                        var result = JsonConvert.DeserializeObject<T>(response.Content.ReadAsStringAsync().GetAwaiter().GetResult());
                        if (validate(result)) { _outcomes[api] = RequestOutcome.Success; return result; }
                        var code = (result as ApiResponse)?.code;
                        _outcomes[api] = code.HasValue && code.Value != 200 ? RequestOutcome.Rejected : RequestOutcome.InvalidResponse;
                        if (_outcomes[api] == RequestOutcome.Rejected && request.RequestUri.Scheme == "https") _secureRefusal = true;
                        Trace(label + ": rejected or incomplete response" +
                            (code.HasValue ? " (code " + code.Value + ")" : ""));
                        return null;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                _outcomes[api] = RequestOutcome.TransportFailure;
                var phaseExpired = search && (_searchDeadline.IsCancellationRequested || _elapsed.Elapsed >= _searchTimeout);
                if (!_deadline.IsCancellationRequested && !phaseExpired && api != RequestInterface.Other)
                {
                    _unavailable.Add(api);
                    Trace(api == RequestInterface.NewApi ? "New interface disabled for this lookup after timeout" :
                        api + " interface disabled for this lookup after timeout");
                }
                Trace(label + (_deadline.IsCancellationRequested ? ": lookup deadline reached" :
                    phaseExpired ? ": search budget exhausted" : ": request timed out"));
            }
            catch (HttpRequestException ex)
            {
                _outcomes[api] = RequestOutcome.TransportFailure;
                if (api != RequestInterface.Other) _unavailable.Add(api);
                Trace(label + ": " + ex.GetType().Name);
            }
            catch (Exception ex)
            {
                _outcomes[api] = RequestOutcome.InvalidResponse;
                // Do not log response bodies, URLs, song tags or local file paths.
                Trace(label + ": " + ex.GetType().Name);
            }
            return null;
        }

        internal void Report(string message) => Trace(message);

        private void Trace(string message)
        {
            try { _trace?.Invoke(message); } catch { /* Diagnostics must not break a lookup. */ }
        }

        public void Dispose()
        {
            _deadline.Dispose();
            _searchDeadline.Dispose();
            _client.Dispose();
        }
    }
}
