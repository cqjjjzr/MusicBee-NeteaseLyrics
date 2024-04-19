using System;
using System.Collections.Generic;
#pragma warning disable 649 // Suppresses: ___ is never assigned to

// ReSharper disable All

namespace MusicBeePlugin
{
    public class SearchResult
    {
        public SearchResultResult result;
        public int code;
    }

    public class SearchResultResult
    {
        public int songCount;
        public IEnumerable<SearchResultSong> songs;
    }

    public class SearchResultSong : IEquatable<SearchResultSong>
    {
        public string name;
        public long id;
        public List<SearchResultArtist> artists;
        public SearchResultAlbum album;

        public bool Equals(SearchResultSong other)
        {
            if (ReferenceEquals(null, other)) return false;
            if (ReferenceEquals(this, other)) return true;
            return id == other.id;
        }

        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            if (ReferenceEquals(this, obj)) return true;
            if (obj.GetType() != this.GetType()) return false;
            return Equals((SearchResultSong) obj);
        }

        public override int GetHashCode()
        {
            return id.GetHashCode();
        }

        public static bool operator ==(SearchResultSong left, SearchResultSong right)
        {
            return Equals(left, right);
        }

        public static bool operator !=(SearchResultSong left, SearchResultSong right)
        {
            return !Equals(left, right);
        }

        public override string ToString()
        {
            return $"Name: {name}, Id: {id}, Artists: {string.Join(",", artists)}, Album: {album}";
        }
    }

    public class SearchResultArtist
    {
        public long id;
        public string name;

        public override string ToString()
        {
            return $"Id: {id}, Name: {name}";
        }
    }

    public class SearchResultAlbum
    {
        public long id;
        public string name;

        public override string ToString()
        {
            return $"Id: {id}, Name: {name}";
        }
    }

    public class LyricResult
    {
        public LyricInner lrc;
        public LyricInner tlyric;
        public int code;
    }

    public class LyricInner
    {
        public string lyric;
    }
}
