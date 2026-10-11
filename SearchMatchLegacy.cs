using System;
using System.Linq;
using System.Text.RegularExpressions;

namespace MusicBeePlugin
{
    /// <summary>
    /// 旧的匹配设置
    /// </summary>
    internal static class SearchMatchLegacy
    {
        public static long QueryWithFeatRemoved(string trackTitle, string artist, bool fuzzy, LookupSession session)
        {
            var ret = Query(trackTitle, artist, fuzzy, session);
            if (ret != null) return ret.id;

            ret = Query(RemoveLeadingNumber(RemoveFeat(trackTitle)), artist, fuzzy, session);
            return ret?.id ?? 0;
        }

        private static SearchResultSong Query(string trackTitle, string artist, bool fuzzy, LookupSession session)
        {
            var ret = NeteaseApi.Search((trackTitle + " " + artist).Trim(), session).Where(rst =>
                fuzzy || string.Equals(GetFirstSeq(RemoveLeadingNumber(rst.name)), GetFirstSeq(trackTitle),
                    StringComparison.OrdinalIgnoreCase)).ToList();
            if (ret != null && ret.Count > 0) return ret[0];

            ret = NeteaseApi.Search(trackTitle.Trim(), session).Where(rst =>
                fuzzy || string.Equals(GetFirstSeq(RemoveLeadingNumber(rst.name)), GetFirstSeq(trackTitle),
                    StringComparison.OrdinalIgnoreCase)).ToList();
            return ret != null && ret.Count > 0 ? ret[0] : null;
        }

        private static string GetFirstSeq(string s)
        {
            s = s.Replace("\u00A0", " ");
            var pos = s.IndexOf(' ');
            return s.Substring(0, pos == -1 ? s.Length : pos).Trim();
        }

        private static string RemoveFeat(string name)
        {
            return Regex.Replace(name, @"\s*\(feat.+\)", "", RegexOptions.IgnoreCase);
        }

        private static string RemoveLeadingNumber(string name)
        {
            return Regex.Replace(name, @"^\d+\.?\s*", "", RegexOptions.IgnoreCase);
        }
    }
}
