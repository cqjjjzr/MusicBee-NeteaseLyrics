using System.Collections.Generic;
#pragma warning disable 649 // Suppresses: ___ is never assigned to

// ReSharper disable All

namespace MusicBeePlugin
{
    class SearchResult
    {
        public SearchResultResult result;
        public int code;
    }

    class SearchResultResult
    {
        public int songCount;
        public IEnumerable<SearchResultSong> songs;
    }

    class SearchResultSong
    {
        public string name;
        public int id;
    }

    class LyricResult
    {
        public LyricInner lrc;
        public LyricInner tlyric;
        public int code;
    }

    class LyricInner
    {
        public string lyric;
    }
}
