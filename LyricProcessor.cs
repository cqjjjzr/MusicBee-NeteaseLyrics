using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace MusicBeePlugin
{
    static class LyricProcessor
    {
        public static string injectTranslation(string originalLrc, string translationLrc)
        {
            var originalEntries = parse(originalLrc);
            var translationEntries = parse(translationLrc);
            foreach (var originalEntry in originalEntries)
            {
                var translationEntry = translationEntries.FirstOrDefault(entry => entry.timeLabel == originalEntry.timeLabel);
                if (translationEntry != null)
                    originalEntry.content += "/" + translationEntry.content;
            }

            return string.Join("\n", originalEntries);
        }

        private static List<LyricEntry> parse(string lrc)
        {
            var entries = new List<LyricEntry>();
            foreach (var line in lrc.Split('\n'))
            {
                if (string.IsNullOrWhiteSpace(lrc)) continue;
                var matches = Regex.Matches(line, "((\\[.+?])+)(.+)");
                if (matches.Count < 1) continue;

                var match = matches[0];
                if (match.Groups.Count < 4) continue;
                var content = match.Groups[3].Value;
                
                foreach (Capture capture in match.Groups[1].Captures)
                {
                    entries.Add(new LyricEntry(capture.Value, content));
                }
            }
            return entries;
        }
    }

    class LyricEntry
    {
        public string timeLabel;
        public string content;

        public LyricEntry(string timeLabel, string content)
        {
            this.timeLabel = timeLabel;
            this.content = content;
        }

        public override string ToString()
        {
            return timeLabel + content;
        }
    }
}
