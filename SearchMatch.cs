using F23.StringSimilarity;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace MusicBeePlugin
{
    internal static class SearchMatch
    {
        /// <summary>
        /// 有多个 artist 时，分割各 artist 的符号。
        /// 部分圈子的人喜欢用比较有个性的分割符，比如“ x ”，此事在《みんなみくみくにしてあげる♪》中亦有记载
        /// </summary>
        private static readonly string[] Delimiters = {"/", "&", ",", "，", " x ", " * ", "\u00d7", "\u00B7"};
        private static readonly Regex FeatPatternWithoutParenthesis = new Regex(@"\s+feat(.+)");
        private static readonly Regex FeatPatternWithParenthesis = new Regex(@"\s*\(feat(.+)\)");

        public static long SearchAndMatch(string title, string artist, string album, long duration)
        {
            var (titleWithoutArtist, artists) = SplitTitleArtist(title, artist);
            var artistsStr = string.Join(" ", artists);
            var results = new HashSet<SearchResultSong>(new IdOnlyEqualityComparer());
            results.UnionWith(NeteaseApi.Search(titleWithoutArtist));
            results.UnionWith(NeteaseApi.Search($"{titleWithoutArtist} {artistsStr}"));
            results.UnionWith(NeteaseApi.Search($"{titleWithoutArtist} {artistsStr} {album}"));

            if (results.Count <= 0)
                return 0;
            var ranked = results.Select(it => (
                rank: CalculateMatchScore(it, titleWithoutArtist, artistsStr, album, duration),
                it.id, // prevent comparer from checking `song`, because SearchResultSong is not comparable.
                song: it)
            ).ToList();
            ranked.Sort();
            return ranked.Max().song.id;
        }

        private static double CalculateMatchScore(
            SearchResultSong song, string titleWithoutArtist, string artistsStr,
            string album, long duration)
        {
            var resultArtists = song.artists.Select(it => it.name).ToList();
            resultArtists.Sort();
            var resultArtistsStr = string.Join(" ", resultArtists);

            // “距离”公式：
            // 歌曲长度距离^2 + 标题距离 * 2 + 表演者距离 * 0.7 + 专辑距离 * 1
            // 因为长度是比较重要的 metrics，并且当长度差得超过一定距离的时候应该起到“一票否决”的效果，因此使用了平方
            var l = new Levenshtein();
            var durationDiff = (duration / 1000.0 - song.duration / 1000.0);
            var score = -(durationDiff * durationDiff);
            score -= l.Distance(titleWithoutArtist, song.name) * 2;
            score -= l.Distance(artistsStr, resultArtistsStr) * 0.7;
            score -= l.Distance(album, song.album.name);
            return score;
        }

        /// <summary>
        /// 有些人会把 (feat. Somebody) 这样的信息写在曲目标题里面，
        /// 并且网易云不会做特殊处理（网易云本身支持多 artist），因此会搜出来一堆奇怪的结果。
        /// 因此需要先把这些 feat. 的子句提出来放到 artist 里面。
        /// 当然 Artist 里面的也需要处理
        /// </summary>
        /// <returns></returns>
        public static (string, IEnumerable<string>) SplitTitleArtist(string title, string artist)
        {
            var (artistsWithoutFeat, featArtists) = ExtractFeat(SanitizeString(artist));

            var artists = artistsWithoutFeat.Split(Delimiters, StringSplitOptions.RemoveEmptyEntries)
                .Select(it => it.Trim())
                .ToList();
            artists.AddRange(featArtists);
            
            var (titleWithoutFeat, featArtists2) = ExtractFeat(SanitizeString(title));
            artists.AddRange(featArtists2);
            
            artists.Sort();
            return (titleWithoutFeat, artists);
        }

        private static string SanitizeString(string str)
        {
            return str.Replace('（', '(').Replace('）', ')').Replace('\u00A0', ' ');
        }

        private static (string, IEnumerable<string>) ExtractFeat(string str)
        {
            var match = FeatPatternWithParenthesis.Match(str);
            if (!match.Success)
                match = FeatPatternWithoutParenthesis.Match(str);
            if (!match.Success) return (str, Enumerable.Empty<string>());

            str = str.Remove(match.Index, match.Length).Trim();
            if (match.Groups.Count <= 0) return (str, Enumerable.Empty<string>());

            var featClause = match.Groups[1].Captures[0].Value;
            featClause = featClause.TrimStart('.', ' ');
            if (featClause.EndsWith(")"))
                featClause = featClause.Substring(0, featClause.Length - 1);

            return (str, featClause.Split(Delimiters, StringSplitOptions.RemoveEmptyEntries).Select(it => it.Trim()));
        }

        private class IdOnlyEqualityComparer : EqualityComparer<SearchResultSong>
        {
            public override bool Equals(SearchResultSong x, SearchResultSong y)
            {
                if (ReferenceEquals(x, y)) return true;
                if (x is null) return false;
                if (y is null) return false;
                if (x.GetType() != y.GetType()) return false;
                return x.id == y.id;
            }

            public override int GetHashCode(SearchResultSong obj)
            {
                return obj.id.GetHashCode();
            }
        }
    }
}
