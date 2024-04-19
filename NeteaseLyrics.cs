using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using System.Collections.Specialized;
using System.Net;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Newtonsoft.Json;
using System.Text.RegularExpressions;
using F23.StringSimilarity;
using F23.StringSimilarity.Interfaces;

namespace MusicBeePlugin
{
    public class NeteaseConfig
    {
        public enum OutputFormat
        {
            Original = 0,
            Both = 1,
            Translation = 2
        }

        public OutputFormat format { get; set; } = OutputFormat.Both;
        public bool fuzzy { get; set; } = false;
    }

    public partial class Plugin
    {
        private const string ProviderName = "Netease Cloud Music(网易云音乐)";
        private const string ConfigFilename = "netease_config";
        private const string NoTranslateFilename = "netease_notranslate";
        private NeteaseConfig _config = new NeteaseConfig();
        private ComboBox _formatComboBox = null;
        private CheckBox _fuzzyCheckBox = null;

        private MusicBeeApiInterface _mbApiInterface;
        private readonly PluginInfo _about = new PluginInfo();

        // ReSharper disable once UnusedMember.Global
        public PluginInfo Initialise(IntPtr apiInterfacePtr)
        {
            var versions = Assembly.GetExecutingAssembly().GetName().Version.ToString().Split('.');

            _mbApiInterface = new MusicBeeApiInterface();
            _mbApiInterface.Initialise(apiInterfacePtr);
            _about.PluginInfoVersion = PluginInfoVersion;
            _about.Name = "Netease Lyrics";
            _about.Description = "A plugin to retrieve lyrics from Netease Cloud Music.(从网易云音乐获取歌词的插件。)";
            _about.Author = "Charlie Jiang";
            _about.TargetApplication = "";   // current only applies to artwork, lyrics or instant messenger name that appears in the provider drop down selector or target Instant Messenger
            _about.Type = PluginType.LyricsRetrieval;
            _about.VersionMajor = short.Parse(versions[0]);  // your plugin version
            _about.VersionMinor = short.Parse(versions[1]);
            _about.Revision = short.Parse(versions[2]);
            _about.MinInterfaceVersion = MinInterfaceVersion;
            _about.MinApiRevision = MinApiRevision;
            _about.ReceiveNotifications = ReceiveNotificationFlags.DownloadEvents;
            _about.ConfigurationPanelHeight = 90;   // height in pixels that musicbee should reserve in a panel for config settings. When set, a handle to an empty panel will be passed to the Configure function

            string noTranslatePath = Path.Combine(_mbApiInterface.Setting_GetPersistentStoragePath(), NoTranslateFilename);
            string configPath = Path.Combine(_mbApiInterface.Setting_GetPersistentStoragePath(), ConfigFilename);
            if (File.Exists(configPath))
            {
                try
                {
                    _config = JsonConvert.DeserializeObject<NeteaseConfig>(File.ReadAllText(configPath, Encoding.UTF8));
                }
                catch (Exception ex)
                {
                    _mbApiInterface.MB_Trace("[NeteaseMusic] Failed to load config" + ex);
                }
            }
            if (File.Exists(noTranslatePath))
            {
                File.Delete(noTranslatePath);
                _config.format = NeteaseConfig.OutputFormat.Original;
                SaveSettingsInternal();
            }

            return _about;
        }

        // ReSharper disable once UnusedMember.Global
        public bool Configure(IntPtr panelHandle)
        {
            if (panelHandle == IntPtr.Zero) return false;
            var configPanel = (Panel)Control.FromHandle(panelHandle);
            // Components are automatically disposed when this is called.
            configPanel.Controls.Clear();

            // MB_AddPanel doesn't skin the component correctly either
            //_formatComboBox = (ComboBox)_mbApiInterface.MB_AddPanel(null, PluginPanelDock.ComboBox);
            _formatComboBox = new ComboBox();
            _formatComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
            _formatComboBox.Items.Add("Only original text");
            _formatComboBox.Items.Add("Original text and translation");
            _formatComboBox.Items.Add("Only translation");
            _formatComboBox.AutoSize = true;
            _formatComboBox.Location = new Point(0, 0);
            _formatComboBox.Width = 300;
            _formatComboBox.SelectedIndex = (int)_config.format;
            configPanel.Controls.Add(_formatComboBox);

            _fuzzyCheckBox = new CheckBox();
            _fuzzyCheckBox.Text = "Fuzzy matching (Don't double check match and use first result directly)";
            _fuzzyCheckBox.Location = new Point(0, 50);
            _fuzzyCheckBox.Checked = _config.fuzzy;
            _fuzzyCheckBox.AutoSize = true;
            configPanel.Controls.Add(_fuzzyCheckBox);
            return false;
        }

        // called by MusicBee when the user clicks Apply or Save in the MusicBee Preferences screen.
        // its up to you to figure out whether anything has changed and needs updating
        // ReSharper disable once UnusedMember.Global
        public void SaveSettings()
        {
            if (_formatComboBox.SelectedIndex < 0 || _formatComboBox.SelectedIndex > 2)
                _config.format = NeteaseConfig.OutputFormat.Both;
            else
                _config.format = (NeteaseConfig.OutputFormat)_formatComboBox.SelectedIndex;
            _config.fuzzy = _fuzzyCheckBox.Checked;
            SaveSettingsInternal();
        }

        private void SaveSettingsInternal()
        {
            string configPath = Path.Combine(_mbApiInterface.Setting_GetPersistentStoragePath(), ConfigFilename);
            var json = JsonConvert.SerializeObject(_config);
            File.WriteAllText(configPath, json, Encoding.UTF8);
        }

        // MusicBee is closing the plugin (plugin is being disabled by user or MusicBee is shutting down)
        // ReSharper disable once UnusedMember.Global
        public void Close(PluginCloseReason reason)
        {
        }

        // uninstall this plugin - clean up any persisted files
        // ReSharper disable once UnusedMember.Global
        public void Uninstall()
        {
            var dataPath = _mbApiInterface.Setting_GetPersistentStoragePath();
            var p = Path.Combine(dataPath, NoTranslateFilename);
            if (File.Exists(p)) File.Delete(p);
            string configPath = Path.Combine(_mbApiInterface.Setting_GetPersistentStoragePath(), ConfigFilename);
            if (File.Exists(configPath)) File.Delete(configPath);
        }

        // ReSharper disable once UnusedMember.Global
        public string RetrieveLyrics(string sourceFileUrl, string artist, string trackTitle, string album,
            bool synchronisedPreferred, string provider)
        {
            if (provider != ProviderName) return null;

            var id = 0L;
            var specifiedId = _mbApiInterface.Library_GetFileTag(sourceFileUrl, MetaDataType.Custom10)
                              ?? _mbApiInterface.NowPlaying_GetFileTag(MetaDataType.Custom10);

            // MusicBee 是纯**，会自动去掉标题内的各种东西
            // 问题在于有些歌曲括号里的内容比歌名还长
            // 所以我们这里手动获取给他加回来
            var realTitle = _mbApiInterface.NowPlaying_GetFileTag(MetaDataType.TrackTitle);

            id = TryParseNeteaseURL(specifiedId);

            if (id == 0)
            {
                var searchResult = QueryBestMatch(realTitle, artist, album);
                if (searchResult.Count == 0) return null;
                Console.Error.WriteLine(string.Join(", ", searchResult));
                id = searchResult.First().id;
            }

            
            if (id == 0)
                return null;

            var lyricResult = RequestLyric(id);

            if (lyricResult.lrc?.lyric == null) return null;
            if (lyricResult.tlyric?.lyric == null || _config.format == NeteaseConfig.OutputFormat.Original)
                return lyricResult.lrc.lyric; // No need to process translation

            if (_config.format == NeteaseConfig.OutputFormat.Translation)
                return lyricResult.tlyric?.lyric ?? lyricResult.lrc.lyric;
            // translation
            return LyricProcessor.InjectTranslation(lyricResult.lrc.lyric, lyricResult.tlyric.lyric);
        }

        public SearchResultSong QueryWithFeatRemoved(string trackTitle, string artist)
        {
            var ret = Query(trackTitle, artist);
            if (ret != null) return ret;

            ret = Query(RemoveLeadingNumber(RemoveFeat(trackTitle)), artist);
            return ret;
        }

        public SearchResultSong Query(string trackTitle, string artist)
        {
            var ret = Query(trackTitle + " " + artist)?.result?.songs?.Where(rst =>
                _config.fuzzy || string.Equals(GetFirstSeq(RemoveLeadingNumber(rst.name)), GetFirstSeq(trackTitle),
                    StringComparison.OrdinalIgnoreCase)).ToList();
            if (ret != null && ret.Count > 0) return ret[0];

            ret = Query(trackTitle)?.result?.songs?.Where(rst =>
                _config.fuzzy || string.Equals(GetFirstSeq(RemoveLeadingNumber(rst.name)), GetFirstSeq(trackTitle),
                    StringComparison.OrdinalIgnoreCase)).ToList();
            return ret != null && ret.Count > 0 ? ret[0] : null;
        }

        public static double Similarity(INormalizedStringSimilarity algorithm, SearchResultSong song, string trackTitle, string artist, string album)
        {
            return algorithm.Similarity(song.artists[0].name, artist) * 0.1 +
                   algorithm.Similarity(song.album.name, album) * 0.25 +
                   algorithm.Similarity(song.name, trackTitle) * 0.65;
        }

        public List<SearchResultSong> QueryBestMatch(string trackTitle, string artist, string album)
        {
            var algorithm = new JaroWinkler();
            var result = (Query($"{trackTitle} {artist} {album}")?.result?.songs
                ?.Concat(Query(trackTitle)?.result?.songs ?? Enumerable.Empty<SearchResultSong>()) ?? Enumerable.Empty<SearchResultSong>()).ToHashSet();
            return result.Where(song => Similarity(algorithm, song, trackTitle, artist, album) > 0.65)
                .OrderByDescending(song =>
                Similarity(algorithm, song, trackTitle, artist, album)
            ).ToList();
        }

        public static SearchResult Query(string s)
        {
            using (var client = new WebClient())
            {
                client.Headers.Add(HttpRequestHeader.Referer, "http://music.163.com/");
                //client.Headers.Add(HttpRequestHeader.Cookie, "appver=1.5.0.75771;");

                //var searchPost = new NameValueCollection
                //{
                //    ["s"] = s,
                //    ["limit"] = "6",
                //    ["offset"] = "0",
                //    ["type"] = "1"
                //};
                var nameEncoded = Uri.EscapeUriString(s);
                var searchResult = JsonConvert.DeserializeObject<SearchResult>(
                    Encoding.UTF8.GetString(
                        client.DownloadData(
                            $"http://music.163.com/api/search/get/?csrf_token=hlpretag=&hlposttag=&s={nameEncoded}&type=1&offset=0&total=true&limit=8")
                    )
                );
                //var searchResult = JsonConvert.DeserializeObject<SearchResult>(Encoding.UTF8.GetString(client.UploadValues("http://music.163.com/api/search/pc", searchPost)));
                if (searchResult.code != 200) return null;
                return searchResult.result.songCount <= 0 ? null : searchResult;
            }
        }

        private static LyricResult RequestLyric(long id)
        {
            using (var client = new WebClient())
            {
                client.Headers.Add(HttpRequestHeader.Referer, "http://music.163.com/");
                client.Headers.Add(HttpRequestHeader.Cookie, "appver=1.5.0.75771;");
                var lyricResult = JsonConvert.DeserializeObject<LyricResult>(Encoding.UTF8.GetString(client.DownloadData("http://music.163.com/api/song/lyric?os=pc&id=" + id + "&lv=-1&kv=-1&tv=-1")));
                return lyricResult.code != 200 ? null : lyricResult;
            }
        }

        private static string GetFirstSeq(string s)
        {
            s = s.Replace("\u00A0", " ");
            var pos = s.IndexOf(' ');
            return s.Substring(0, pos == -1 ? s.Length : pos).Trim();
        }

        private string RemoveFeat(string name)
        {
            return Regex.Replace(name, "\\s*\\(feat.+\\)", "", RegexOptions.IgnoreCase);
        }

        private static string RemoveLeadingNumber(string name)
        {
            return Regex.Replace(name, "^\\d+\\.?\\s*", "", RegexOptions.IgnoreCase);
        }

        private static long TryParseNeteaseURL(string input)
        {
            if (input == null)
                return 0;
            if (input.StartsWith("netease="))
            {
                input = input.Substring("netease=".Length);
                long.TryParse(input, out var id);
                return id;
            }

            if (input.Contains("music.163.com"))
            {
                var matches = Regex.Matches(input, "id=(\\d+)");
                if (matches.Count <= 0)
                    return 0;

                var groups = matches[0].Groups;
                if (groups.Count <= 1)
                    return 0;

                var idString = groups[1].Captures[0].Value;
                long.TryParse(idString, out var id);
                return id;
            }

            return 0;
        }

        public string[] GetProviders()
        {
            return new []{ProviderName};
        }
        public void ReceiveNotification(string sourceFileUrl, NotificationType type)
        {
        }
    }
}