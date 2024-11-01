using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text.RegularExpressions;

namespace MusicBeePlugin
{
    [SuppressMessage("ReSharper", "InconsistentNaming")]
    internal static class LyricProcessor
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
            var result = lrc.Split('\n')
                .Where(line => !string.IsNullOrWhiteSpace(line))
                .Select(line => LyricLineRegex.Matches(line))
                .Where(matches => matches.Count >= 1)
                .Select(matches => matches[0])
                .Where(match => match.Groups.Count >= 3)
                .SelectMany(match => match.Groups[1].Captures.Cast<Capture>(),
                    (match, capture) => new LyricEntry(capture.Value, match.Groups[3].Value))
                .ToList();

            return result;
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
