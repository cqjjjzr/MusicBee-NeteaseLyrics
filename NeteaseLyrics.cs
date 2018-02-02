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
            about.ConfigurationPanelHeight = 0;   // height in pixels that musicbee should reserve in a panel for config settings. When set, a handle to an empty panel will be passed to the Configure function
            return about;
        }

        public bool Configure(IntPtr panelHandle)
        {
            return false;
        }
       
        // called by MusicBee when the user clicks Apply or Save in the MusicBee Preferences screen.
        // its up to you to figure out whether anything has changed and needs updating
        public void SaveSettings()
        {
        }

        // MusicBee is closing the plugin (plugin is being disabled by user or MusicBee is shutting down)
        public void Close(PluginCloseReason reason)
        {
        }

        // uninstall this plugin - clean up any persisted files
        public void Uninstall()
        {
        }

        private const string PROVIDER_NAME = "Netease Cloud Music";
        public string RetrieveLyrics(string sourceFileUrl, string artist, string trackTitle, string album,
            bool synchronisedPreferred, string provider)
        {
            if (provider != PROVIDER_NAME) return null;
            using (var client = new WebClient())
            {
                client.Headers.Add(HttpRequestHeader.Referer, "http://music.163.com/");
                client.Headers.Add(HttpRequestHeader.Cookie, "appver=1.5.0.75771;");

                var searchPost = new NameValueCollection();
                searchPost["s"] = trackTitle + " " + artist;
                searchPost["limit"] = "1";
                searchPost["offset"] = "0";
                searchPost["type"] = "1";
                var searchResult = JsonConvert.DeserializeObject<SearchResult>(Encoding.UTF8.GetString(client.UploadValues("http://music.163.com/api/search/pc", searchPost)));
                if (searchResult.code != 200) return null;
                if (searchResult.result.songCount <= 0) return null;

                var id = searchResult.result.songs.First().id;
                var lyricResult = JsonConvert.DeserializeObject<LyricResult>(Encoding.UTF8.GetString(client.DownloadData("http://music.163.com/api/song/lyric?os=pc&id=" + id + "&lv=-1&kv=-1&tv=-1")));
                if (lyricResult.code != 200) return null;
                if (lyricResult.lrc?.lyric == null) return null;
                if (lyricResult.tlyric?.lyric == null)
                    return lyricResult.lrc.lyric; // No need to process translation

                // translation
                return LyricProcessor.injectTranslation(lyricResult.lrc.lyric, lyricResult.tlyric.lyric);
            }
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