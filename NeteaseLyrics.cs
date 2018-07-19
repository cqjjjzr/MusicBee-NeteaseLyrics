using System;
using System.Runtime.InteropServices;
using System.Drawing;
using System.Windows.Forms;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Net;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using System.Text.RegularExpressions;

namespace MusicBeePlugin
{
    public partial class Plugin
    {
        private MusicBeeApiInterface mbApiInterface;
        private PluginInfo about = new PluginInfo();

        public PluginInfo Initialise(IntPtr apiInterfacePtr)
        {
            mbApiInterface = new MusicBeeApiInterface();
            mbApiInterface.Initialise(apiInterfacePtr);
            about.PluginInfoVersion = PluginInfoVersion;
            about.Name = "Netease Lyrics";
            about.Description = "A plugin to retrieve lyrics from Netease Cloud Music.";
            about.Author = "Charlie Jiang";
            about.TargetApplication = "";   // current only applies to artwork, lyrics or instant messenger name that appears in the provider drop down selector or target Instant Messenger
            about.Type = PluginType.LyricsRetrieval;
            about.VersionMajor = 1;  // your plugin version
            about.VersionMinor = 0;
            about.Revision = 1;
            about.MinInterfaceVersion = MinInterfaceVersion;
            about.MinApiRevision = MinApiRevision;
            about.ReceiveNotifications = ReceiveNotificationFlags.DownloadEvents;
            about.ConfigurationPanelHeight = 50;   // height in pixels that musicbee should reserve in a panel for config settings. When set, a handle to an empty panel will be passed to the Configure function
            
            return about;
        }

        private CheckBox noTranslate = new CheckBox();
        private string _noTranslateFilename = "netease_notranslate";

        public bool Configure(IntPtr panelHandle)
        {
            if (panelHandle != IntPtr.Zero)
            {
                Panel configPanel = (Panel)Control.FromHandle(panelHandle);
                configPanel.Controls.Clear();
                noTranslate.Text = "Don't process translate";
                noTranslate.AutoSize = true;
                noTranslate.Location = new Point(0, 0);
                noTranslate.Checked = File.Exists(Path.Combine(mbApiInterface.Setting_GetPersistentStoragePath(), _noTranslateFilename));
                configPanel.Controls.Add(noTranslate);
            }
            return false;
        }
       
        // called by MusicBee when the user clicks Apply or Save in the MusicBee Preferences screen.
        // its up to you to figure out whether anything has changed and needs updating
        public void SaveSettings()
        {
            string dataPath = mbApiInterface.Setting_GetPersistentStoragePath();
            string p = Path.Combine(dataPath, _noTranslateFilename);
            if (noTranslate.Checked)
            {
                File.Create(p);
            } else
            {
                if (File.Exists(p)) File.Delete(p);
            }
        }

        // MusicBee is closing the plugin (plugin is being disabled by user or MusicBee is shutting down)
        public void Close(PluginCloseReason reason)
        {
        }

        // uninstall this plugin - clean up any persisted files
        public void Uninstall()
        {
            string dataPath = mbApiInterface.Setting_GetPersistentStoragePath();
            string p = Path.Combine(dataPath, _noTranslateFilename);
            if (File.Exists(p)) File.Delete(p);
        }

        private const string PROVIDER_NAME = "Netease Cloud Music";
        public string RetrieveLyrics(string sourceFileUrl, string artist, string trackTitle, string album,
            bool synchronisedPreferred, string provider)
        {
            if (provider != PROVIDER_NAME) return null;
            var isNoTranslate = File.Exists(Path.Combine(mbApiInterface.Setting_GetPersistentStoragePath(), _noTranslateFilename));

            LyricResult lyricResult;
            try
            {
                var searchResult = QueryWithFeatRemoved(trackTitle, artist);
                lyricResult = RequestLyric(searchResult.id);
            }
            catch (Exception e)
            {
                throw e;
            }
            
            if (lyricResult.lrc?.lyric == null) return null;
            if (lyricResult.tlyric?.lyric == null || isNoTranslate)
                return lyricResult.lrc.lyric; // No need to process translation

            // translation
            return LyricProcessor.injectTranslation(lyricResult.lrc.lyric, lyricResult.tlyric.lyric);
        }

        private SearchResultSong QueryWithFeatRemoved(string trackTitle, string artist)
        {
            SearchResultSong ret;
            ret = Query(trackTitle, artist);
            if (ret != null) return ret;

            ret = Query(RemoveLeadingNumber(RemoveFeat(trackTitle)), artist);
            if (ret != null) return ret;

            return null;
        }

        private SearchResultSong Query(string trackTitle, string artist)
        {
            List<SearchResultSong> ret;
            ret = Query(trackTitle + " " + artist).result.songs.Where(rst =>
                    string.Equals(GetFirstSeq(rst.name), GetFirstSeq(trackTitle),
                        StringComparison.OrdinalIgnoreCase)).ToList();
            if (ret.Count > 0) return ret[0];

            ret = Query(trackTitle).result.songs.Where(rst =>
                string.Equals(GetFirstSeq(rst.name), GetFirstSeq(trackTitle),
                    StringComparison.OrdinalIgnoreCase)).ToList();
            if (ret.Count > 0) return ret[0];

            return null;
        }

        private SearchResult Query(string s)
        {
            using (var client = new WebClient())
            {
                client.Headers.Add(HttpRequestHeader.Referer, "http://music.163.com/");
                client.Headers.Add(HttpRequestHeader.Cookie, "appver=1.5.0.75771;");

                var searchPost = new NameValueCollection();
                searchPost["s"] = s;
                searchPost["limit"] = "1";
                searchPost["offset"] = "0";
                searchPost["type"] = "1";
                var searchResult = JsonConvert.DeserializeObject<SearchResult>(Encoding.UTF8.GetString(client.UploadValues("http://music.163.com/api/search/pc", searchPost)));
                if (searchResult.code != 200) return null;
                if (searchResult.result.songCount <= 0) return null;

                return searchResult;
            }
        }

        private LyricResult RequestLyric(int id)
        {
            using (var client = new WebClient())
            {
                client.Headers.Add(HttpRequestHeader.Referer, "http://music.163.com/");
                client.Headers.Add(HttpRequestHeader.Cookie, "appver=1.5.0.75771;");
                var lyricResult = JsonConvert.DeserializeObject<LyricResult>(Encoding.UTF8.GetString(client.DownloadData("http://music.163.com/api/song/lyric?os=pc&id=" + id + "&lv=-1&kv=-1&tv=-1")));
                if (lyricResult.code != 200) return null;
                return lyricResult;
            }
        }

        private string GetFirstSeq(string s)
        {
            var pos = s.IndexOf(' ');
            return s.Trim().Substring(0, pos == -1 ? s.Length : pos);
        }
		
		private string RemoveFeat(string name)
        {
			return Regex.Replace(name, "\\s*\\(feat.+\\)", "", RegexOptions.IgnoreCase);
		}

        private string RemoveLeadingNumber(string name)
        {
            return Regex.Replace(name, "^\\d+\\.?\\s*", "", RegexOptions.IgnoreCase);
        }

        public string[] GetProviders()
        {
            return new []{PROVIDER_NAME};
        }
        public void ReceiveNotification(string sourceFileUrl, NotificationType type)
        {
        }
    }
}