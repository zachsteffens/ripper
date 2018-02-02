using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace dvdrip
{
    public class QueuedItem
    {
        public QueuedItem(String _fullPath, String _title, String _selectedTrackIndex)
        {
            title = _title;
            fullPath = _fullPath;
            selectedTrackIndex = _selectedTrackIndex;
        }

        public String title { get; set; }
        public Boolean ripping { get; set; }
        public Boolean ripped { get; set; }
        public Boolean failedRip { get; set; }
        public Boolean compressing { get; set; }
        public Boolean compressed { get; set; }
        public Boolean failedCompression { get; set; }
        public Boolean copying { get; set; }
        public Boolean copied { get; set; }
        public Boolean failedCopy { get; set; }
        public Boolean removed { get; set; }
        public String fullPath { get; set; }
        public String selectedTrackIndex { get; set; }
        public String pathToRip { get; set; }
        public String pathToCompression { get; set; }
        public Boolean isTV { get; set; }
        public int tvSeason { get; set; }
        public int tvEpisode { get; set; }
        public string tvShowTitle { get; set; }
        public string failedRipTextFile { get; set; }
        public string failedCompressTextFile { get; set; }
    }
    public class Disc
    {
        public Disc()
        {
            foundTitles = new List<string>();
            tracks = new List<track>();
        }

        public List<string> foundTitles { get; set; }
        public List<track> tracks { get; set; }
    }
    public class track
    {
        public string length { get; set; }
        public float size { get; set; }
    }

    public class tmdbTVSearchResult
    {
        public int page { get; set; }
        public int total_results { get; set; }
        public int total_pages { get; set; }
        public IList<tmdbTvShowResult> results { get; set; }
    }
    public class tmdbTvShowResult
    {
        public string original_name { get; set; }
        public int id { get; set; }
        public string name { get; set; }
        public string poster_path { get; set; }
        public string backdrop_path { get; set; }
        public string overview { get; set; }
        public List<String> origin_country { get; set; }
        public string first_air_date { get; set; }
    }
    public class tmdbSeasonSearchResult
    {
        public string _id { get; set; }
        public IList<tmdbTvEpisode> episodes { get; set; }
        public string name { get; set; }
        public string overview { get; set; }
        public int id { get; set; }
        public string poster_path { get; set; }
        public int season_number { get; set; }
    }
    public class tmdbTvEpisode
    {
        public string air_date { get; set; }
        public int episode_number { get; set; }
        public string name { get; set; }
        public string production_code { get; set; }
        public string still_path { get; set; }
        public string overview { get; set; }

    }

    public class tmdbMovieSearchResult
    {
        public int page { get; set; }
        public int total_results { get; set; }
        public int total_pages { get; set; }
        public IList<tmdbMovieResult> results { get; set; }
    }
    public class tmdbMovieResult
    {
        public int vote_count { get; set; }
        public int id { get; set; }
        public bool video { get; set; }
        public float vote_average { get; set; }
        private string _title;
        public string title
        {
            get { return _title; }
            set
            {
                string temp = value;
                foreach (char c in System.IO.Path.GetInvalidFileNameChars())
                {
                    temp = temp.Replace(c, '-');
                }
                _title = temp;
            }
        }
        public float popularity { get; set; }
        public string poster_path { get; set; }
        public string original_title { get; set; }
        public IList<int> genre_ids { get; set; }
        public string backdrop_path { get; set; }
        public bool adult { get; set; }
        public string overview { get; set; }
        public string release_date { get; set; }
    }
    public class tmdbMovieDetails
    {
        public bool adult { get; set; }
        public string backdrop_path { get; set; }
        public int budget { get; set; }
        public string homepage { get; set; }
        public int id { get; set; }
        public string imdb_id { get; set; }
        public string original_language { get; set; }
        public string origingl_title { get; set; }
        public string overview { get; set; }
        public string poster_path { get; set; }
        public string release_date { get; set; }
        public int runtime { get; set; }
        public string tagline { get; set; }
        public string title { get; set; }
    }

}
