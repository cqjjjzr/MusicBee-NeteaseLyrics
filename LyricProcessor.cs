using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text.RegularExpressions;

namespace MusicBeePlugin
{
    [SuppressMessage("ReSharper", "InconsistentNaming")]
    static class LyricProcessor
    {
        private static readonly Regex LyricLineRegex = new Regex(@"((\[.+?])+)(.*)", RegexOptions.Compiled);
        public static string InjectTranslation(string originalLrc, string translationLrc)
        {
            var originalEntries = ExpandEntries(Parse(originalLrc));
            var translationEntries = ExpandEntries(Parse(translationLrc));
            foreach (var originalEntry in originalEntries)
            {
                var translationEntry = translationEntries.FirstOrDefault(entry => entry.timeLabel == originalEntry.timeLabel);
                if (translationEntry != null)
                    originalEntry.content += "/" + translationEntry.content;
            }

            originalEntries.Sort();
            return string.Join("\n", originalEntries);
        }

        private static List<LyricEntry> Parse(string lrc)
        {
            return (
                from line in lrc.Split('\n')
                where !string.IsNullOrWhiteSpace(line)
                select LyricLineRegex.Matches(line) into matches
                where matches.Count >= 1
                select matches[0] into match
                where match.Groups.Count >= 3
                let content = match.Groups[3].Value
                from Capture capture in match.Groups[1].Captures
                select new LyricEntry(capture.Value, content)).ToList();
        }

        private static List<LyricEntry> ExpandEntries(List<LyricEntry> entries)
        {
            return entries.SelectMany(entry => entry.ExpandTimeLabel()).ToList();
        }
    }

    [SuppressMessage("ReSharper", "InconsistentNaming")]
    internal class LyricEntry : IComparable<LyricEntry>
    {
        private static readonly Regex LyricTimeRegex = new Regex(@"(\[[0-9.:]*])", RegexOptions.Compiled);

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

        public IEnumerable<LyricEntry> ExpandTimeLabel()
        {
            var matches = LyricTimeRegex.Matches(timeLabel);
            foreach (var match in matches.Cast<Match>())
                yield return new LyricEntry(match.Value, content);
        }

        public int CompareTo(LyricEntry other)
        {
            if (ReferenceEquals(this, other)) return 0;
            if (ReferenceEquals(null, other)) return 1;
            return string.Compare(timeLabel, other.timeLabel, StringComparison.Ordinal);
        }
    }
}
