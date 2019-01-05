using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text.RegularExpressions;

namespace MusicBeePlugin
{
    [SuppressMessage("ReSharper", "InconsistentNaming")]
    static class LyricProcessor
    {
        public static string InjectTranslation(string originalLrc, string translationLrc)
        {
            var originalEntries = Parse(originalLrc);
            var translationEntries = Parse(translationLrc);
            foreach (var originalEntry in originalEntries)
            {
                var translationEntry = translationEntries.FirstOrDefault(entry => entry.timeLabel == originalEntry.timeLabel);
                if (translationEntry != null)
                    originalEntry.content += "/" + translationEntry.content;
            }

            return string.Join("\n", originalEntries);
        }

        private static List<LyricEntry> Parse(string lrc)
        {
            return (
                from line in lrc.Split('\n')
                where !string.IsNullOrWhiteSpace(line)
                select Regex.Matches(line, "((\\[.+?])+)(.+)")
                into matches
                where matches.Count >= 1
                select matches[0]
                into match
                where match.Groups.Count >= 3
                let content = match.Groups[3].Value
                from Capture capture in match.Groups[1].Captures
                select new LyricEntry(capture.Value, content)).ToList();
        }
    }

    [SuppressMessage("ReSharper", "InconsistentNaming")]
    internal class LyricEntry
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
