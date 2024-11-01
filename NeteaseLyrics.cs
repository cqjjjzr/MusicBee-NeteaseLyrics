using System;
using System.Diagnostics.CodeAnalysis;
using System.Drawing;
using System.Windows.Forms;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Newtonsoft.Json;
using System.Text.RegularExpressions;
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("NeteaseLyricsTest")]

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

        public OutputFormat Format { get; set; } = OutputFormat.Both;
        public bool Fuzzy { get; set; }
        public bool UseLegacyMatch { get; set; }
    }

    public partial class Plugin
    {
        private const string ProviderName = "Netease Cloud Music(网易云音乐)";
        private const string ConfigFilename = "netease_config";
        private const string NoTranslateFilename = "netease_notranslate";
        private NeteaseConfig _config = new NeteaseConfig();
        private ComboBox _formatComboBox;
        private CheckBox _fuzzyCheckBox;
        private CheckBox _useLegacyCheckBox;

        private MusicBeeApiInterface _mbApiInterface;
        private readonly PluginInfo _about = new PluginInfo();

        [SuppressMessage("ReSharper", "UnusedMember.Global")]
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
            _about.ConfigurationPanelHeight = 120;   // height in pixels that musicbee should reserve in a panel for config settings. When set, a handle to an empty panel will be passed to the Configure function

            ReadConfig();
            MigrateLegacySetting();
            return _about;
        }

        [SuppressMessage("ReSharper", "UnusedMember.Global")]
        public bool Configure(IntPtr panelHandle)
        {
            if (panelHandle == IntPtr.Zero) return false;
            var configPanel = (Panel)Control.FromHandle(panelHandle);
            // Components are automatically disposed when this is called.
            configPanel.Controls.Clear();

            // MB_AddPanel doesn't skin the component correctly either
            //_formatComboBox = (ComboBox)_mbApiInterface.MB_AddPanel(null, PluginPanelDock.ComboBox);
            _formatComboBox = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                AutoSize = true,
                Location = new Point(0, 0),
                Width = 300
            };
            _formatComboBox.Items.Add("Only original text");
            _formatComboBox.Items.Add("Original text and translation");
            _formatComboBox.Items.Add("Only translation");
            _formatComboBox.SelectedIndex = (int)_config.Format;
            configPanel.Controls.Add(_formatComboBox);

            _useLegacyCheckBox = new CheckBox
            {
                Text = "Use legacy matching strategy",
                Location = new Point(0, 50),
                Checked = _config.UseLegacyMatch,
                AutoSize = true
            };

            _fuzzyCheckBox = new CheckBox
            {
                Text = "Fuzzy matching (Don't double check match and use first result directly)",
                Location = new Point(0, 80),
                Checked = _config.Fuzzy,
                AutoSize = true
            };

            _useLegacyCheckBox.CheckedChanged += (sender, e) =>
            {
                // "Fuzzy" not available when using new strategy
                _fuzzyCheckBox.Enabled = !_useLegacyCheckBox.Checked;
            };

            configPanel.Controls.Add(_useLegacyCheckBox);
            configPanel.Controls.Add(_fuzzyCheckBox);

            return false;
        }

        // called by MusicBee when the user clicks Apply or Save in the MusicBee Preferences screen.
        // It's up to you to figure out whether anything has changed and needs updating
        [SuppressMessage("ReSharper", "UnusedMember.Global")]
        public void SaveSettings()
        {
            if (_formatComboBox.SelectedIndex < 0 || _formatComboBox.SelectedIndex > 2)
                _config.Format = NeteaseConfig.OutputFormat.Both;
            else
                _config.Format = (NeteaseConfig.OutputFormat)_formatComboBox.SelectedIndex;
            _config.Fuzzy = _fuzzyCheckBox.Checked;
            _config.UseLegacyMatch = _useLegacyCheckBox.Checked;
            SaveSettingsInternal();
        }

        private void SaveSettingsInternal()
        {
            var configPath = Path.Combine(_mbApiInterface.Setting_GetPersistentStoragePath(), ConfigFilename);
            var json = JsonConvert.SerializeObject(_config);
            File.WriteAllText(configPath, json, Encoding.UTF8);
        }

        // MusicBee is closing the plugin (plugin is being disabled by user or MusicBee is shutting down)
        [SuppressMessage("ReSharper", "UnusedMember.Global")]
        [SuppressMessage("ReSharper", "UnusedParameter.Global")]
        public void Close(PluginCloseReason reason)
        {
        }

        // uninstall this plugin - clean up any persisted files
        [SuppressMessage("ReSharper", "UnusedMember.Global")]
        public void Uninstall()
        {
            var dataPath = _mbApiInterface.Setting_GetPersistentStoragePath();
            var p = Path.Combine(dataPath, NoTranslateFilename);
            if (File.Exists(p)) File.Delete(p);
            var configPath = Path.Combine(_mbApiInterface.Setting_GetPersistentStoragePath(), ConfigFilename);
            if (File.Exists(configPath)) File.Delete(configPath);
        }

        [SuppressMessage("ReSharper", "UnusedMember.Global")]
        [SuppressMessage("ReSharper", "UnusedParameter.Global")]
        public string RetrieveLyrics(string sourceFileUrl, 
            string artist, string trackTitle, string album,
            bool synchronisedPreferred, string provider)
        {
            if (provider != ProviderName) return null;

            var specifiedId = _mbApiInterface.Library_GetFileTag(sourceFileUrl, MetaDataType.Custom10)
                              ?? _mbApiInterface.NowPlaying_GetFileTag(MetaDataType.Custom10);

            var id = TryParseNeteaseUrl(specifiedId);
            if (id == 0)
            {
                var realTitle = _mbApiInterface.Library_GetFileTag(sourceFileUrl, MetaDataType.TrackTitle);
                var realArtist = _mbApiInterface.Library_GetFileTag(sourceFileUrl, MetaDataType.Artist);
                var realAlbum = _mbApiInterface.Library_GetFileTag(sourceFileUrl, MetaDataType.Album);
                var durationStr = _mbApiInterface.Library_GetFileProperty(sourceFileUrl, FilePropertyType.Duration);
                id = !_config.UseLegacyMatch 
                    ? SearchMatch.SearchAndMatch(realTitle, realArtist, realAlbum, ParseDurationString(durationStr))
                    : SearchMatchLegacy.QueryWithFeatRemoved(realTitle, realArtist, _config.Fuzzy);
            }

            if (id == 0)
                return null;

            var lyricResult = NeteaseApi.RequestLyric(id);

            if (lyricResult.lrc?.lyric == null) return null;
            if (lyricResult.tlyric?.lyric == null || _config.Format == NeteaseConfig.OutputFormat.Original)
                return lyricResult.lrc.lyric; // No need to process translation

            if (_config.Format == NeteaseConfig.OutputFormat.Translation)
                return lyricResult.tlyric?.lyric ?? lyricResult.lrc.lyric;
            // translation
            return LyricProcessor.InjectTranslation(lyricResult.lrc.lyric, lyricResult.tlyric.lyric);
        }

        private void ReadConfig()
        {
            var configPath = Path.Combine(_mbApiInterface.Setting_GetPersistentStoragePath(), ConfigFilename);
            if (!File.Exists(configPath)) 
                return;
            try
            {
                _config = JsonConvert.DeserializeObject<NeteaseConfig>(File.ReadAllText(configPath, Encoding.UTF8));
            }
            catch (Exception ex)
            {
                _mbApiInterface.MB_Trace("[NeteaseMusic] Failed to load config" + ex);
            }
        }

        private void MigrateLegacySetting()
        {
            var noTranslatePath = Path.Combine(_mbApiInterface.Setting_GetPersistentStoragePath(), NoTranslateFilename);
            if (!File.Exists(noTranslatePath))
                return;
            File.Delete(noTranslatePath);
            _config.Format = NeteaseConfig.OutputFormat.Original;
            SaveSettingsInternal();
        }

        private static long TryParseNeteaseUrl(string input)
        {
            if (input == null)
                return 0;
            if (input.StartsWith("netease="))
            {
                input = input.Substring("netease=".Length);
                long.TryParse(input, out var id);
                return id;
            }

            if (!input.Contains("music.163.com"))
                return 0;

            var matches = Regex.Matches(input, "id=(\\d+)");
            if (matches.Count <= 0)
                return 0;

            var groups = matches[0].Groups;
            if (groups.Count <= 1)
                return 0;

            var idString = groups[1].Captures[0].Value;
            long.TryParse(idString, out var id2);
            return id2;
        }

        private static long ParseDurationString(string durationStr)
        {
            var multiplier = 1000L;
            var sum = 0L;
            foreach (var part in durationStr.Split(':').Reverse())
            {
                if (part.Length > 0)
                    sum += multiplier * long.Parse(part);
                multiplier *= 60;
            }
            return sum;
        }

        [SuppressMessage("ReSharper", "UnusedMember.Global")]
        public string[] GetProviders()
        {
            return new []{ProviderName};
        }
        [SuppressMessage("ReSharper", "UnusedMember.Global")]
        public void ReceiveNotification(string sourceFileUrl, NotificationType type)
        {
        }
    }
}