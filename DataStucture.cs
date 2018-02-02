using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
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
