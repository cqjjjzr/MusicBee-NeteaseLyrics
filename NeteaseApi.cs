using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;

namespace MusicBeePlugin
{
    /*
     * Partially adopted from https://github.com/real-zony/ZonyLrcToolsX/blob/dev/src/ZonyLrcTools.Common/Lyrics/Providers/NetEase/NetEaseLyricsProvider.cs
     */
    internal static class NeteaseApi
    {
        public static IEnumerable<SearchResultSong> Search(string s)
        {
            var postData = new Dictionary<string, object>
                    {
                        { "csrf_token", "" },
                        { "s", s },
                        { "offset", 0 },
                        { "type", 1 },
                        { "limit", 20 }
                    };
            var result = RequestNewApi<SearchResult>(
                @"https://music.163.com/weapi/search/get", 
                postData, it => it.code == 200);
            if (result == null)
                return SearchLegacy(s);
            return result.result.songCount > 0 ? result.result.songs : Enumerable.Empty<SearchResultSong>();
        }

        public static LyricResult RequestLyric(long id)
        {
            var postData = new Dictionary<string, object>
                    {
                        { "OS", "pc" },
                        { "id", id },
                        { "lv", -1 },
                        { "kv", -1 },
                        { "tv", -1 },
                        { "rv", -1 }
                    };
            return RequestNewApi<LyricResult>(
                @"https://music.163.com/weapi/song/lyric?csrf_token=", 
                postData, it => it.code == 200) ?? RequestLyricLegacy(id);
        }

        private static T RequestNewApi<T>(string url, Dictionary<string, object> postData, Func<T, bool> checkFunc) 
            where T : class
        {
            try
            {
                using (var client = new HttpClient())
                {
                    client.DefaultRequestHeaders.Referrer = new Uri(@"https://music.163.com");
                    client.DefaultRequestHeaders.UserAgent.ParseAdd(
                        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36");
                    using (var result = client.PostAsync(
                               url,
                               new FormUrlEncodedContent(EncryptRequest(postData))).Result)
                    {
                        var resultString = result.Content.ReadAsStringAsync().Result;
                        if (!result.IsSuccessStatusCode)
                            throw new HttpRequestException($"non 200 response from {url}: {resultString}");;
                        var resultObject = JsonConvert.DeserializeObject<T>(resultString);
                        if (!checkFunc(resultObject))
                            throw new HttpRequestException($"non 200 response from {url}: {resultString}");
                        return resultObject;
                    }
                }
            }
            catch (Exception e)
            {
                Debug.WriteLine(e);
                return null;
            }
        }

        private static Dictionary<string, string> EncryptRequest(object srcParams)
        {
            var secretKey = NeteaseMusicEncryptionHelper.CreateSecretKey(16);
            var encSecKey = NeteaseMusicEncryptionHelper.RsaEncode(secretKey);
            return new Dictionary<string, string>
            {
                {
                    "params", 
                    NeteaseMusicEncryptionHelper.AesEncode(
                        NeteaseMusicEncryptionHelper.AesEncode(
                            JsonConvert.SerializeObject(srcParams),
                            NeteaseMusicEncryptionHelper.Nonce),
                        secretKey)
                },
                { "encSecKey", encSecKey }
            };
        }

        private static IEnumerable<SearchResultSong> SearchLegacy(string s)
        {
            try
            {
                using (var client = new WebClient())
                {
                    client.Headers[HttpRequestHeader.Referer] = "https://music.163.com/";
                    client.Headers[HttpRequestHeader.UserAgent] =
                        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36";

                    var nameEncoded = Uri.EscapeDataString(s);
                    var resultStr = Encoding.UTF8.GetString(
                        client.DownloadData(
                            $"http://music.163.com/api/search/get/?csrf_token=hlpretag=&hlposttag=&s={nameEncoded}&type=1&offset=0&total=true&limit=6")
                    );
                    var searchResult = JsonConvert.DeserializeObject<SearchResult>(resultStr);
                    if (searchResult.code != 200) return null;
                    return searchResult.result.songCount <= 0
                        ? Enumerable.Empty<SearchResultSong>()
                        : searchResult.result.songs;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex);
                return Enumerable.Empty<SearchResultSong>();
            }
        }

        private static LyricResult RequestLyricLegacy(long id)
        {
            try
            {
                using (var client = new WebClient())
                {
                    client.Headers[HttpRequestHeader.Referer] = "http://music.163.com/";
                    client.Headers[HttpRequestHeader.Cookie] = "appver=1.5.0.75771;";
                    var lyricResult = JsonConvert.DeserializeObject<LyricResult>(
                        Encoding.UTF8.GetString(client.DownloadData("http://music.163.com/api/song/lyric?os=pc&id=" +
                                                                    id + "&lv=-1&kv=-1&tv=-1")));
                    return lyricResult.code != 200 ? null : lyricResult;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex);
                return null;
            }
        }
    }
}