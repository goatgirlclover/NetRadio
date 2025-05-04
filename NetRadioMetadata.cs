using UnityEngine;

namespace NetRadio.Metadata {
    [System.Serializable]
    public class Icestats
    {
        public string admin;
        public string host;
        public string location;
        public string server_id;
        public string server_start;
        public string server_start_iso8601;
        public Source source;
    }

    [System.Serializable]
    public class IcecastStatus
    {
        public Icestats icestats;
    }

    [System.Serializable]
    public class Source
    {
        public string audio_info;
        public int bitrate;
        public int channels;
        public string genre;
        public int listener_peak;
        public int listeners;
        public string listenurl;
        public int samplerate;
        public string server_description;
        public string server_name;
        public string server_type;
        public string stream_start;
        public string stream_start_iso8601;
        public string title;
        public object dummy;
    }


}