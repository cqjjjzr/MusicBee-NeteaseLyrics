using Newtonsoft.Json;
using System;
using System.Net;
using System.Text;

namespace MusicBeePlugin
{
    internal static class NeteaseApi
    {
        public static SearchResult Search(string s)
        {
            using (var client = new WebClient())
            {
                client.Headers[HttpRequestHeader.Referer] = "https://music.163.com/";
                client.Headers[HttpRequestHeader.UserAgent] = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36";

                var nameEncoded = Uri.EscapeDataString(s);
                var ib = Encoding.UTF8.GetString(
                        client.DownloadData(
                            $"http://music.163.com/api/search/get/?csrf_token=hlpretag=&hlposttag=&s={nameEncoded}&type=1&offset=0&total=true&limit=6")
                    );
                var searchResult = JsonConvert.DeserializeObject<SearchResult>(
                    ib
                );
                if (searchResult.code != 200) return null;
                return searchResult.result.songCount <= 0 ? null : searchResult;
            }
        }

        public static LyricResult RequestLyric(long id)
        {
            using (var client = new WebClient())
            {
                client.Headers[HttpRequestHeader.Referer] = "http://music.163.com/";
                client.Headers[HttpRequestHeader.Cookie] = "appver=1.5.0.75771;";
                var lyricResult = JsonConvert.DeserializeObject<LyricResult>(Encoding.UTF8.GetString(client.DownloadData("http://music.163.com/api/song/lyric?os=pc&id=" + id + "&lv=-1&kv=-1&tv=-1")));
                return lyricResult.code != 200 ? null : lyricResult;
            }
        }
    }
}
