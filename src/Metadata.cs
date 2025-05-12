using UnityEngine;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections;
using System.Collections.Generic;

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
        [JsonConverter(typeof(SingleOrArrayConverter<Source>))]
        public List<Source> source;
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

    public class SingleOrArrayConverter<T> : JsonConverter
    {
        public override bool CanConvert(Type objectType)
        {
            return (objectType == typeof(List<T>));
        }

        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
        {
            JToken token = JToken.Load(reader);
            if (token.Type == JTokenType.Array)
            {
                return token.ToObject<List<T>>();
            }
            if (token.Type == JTokenType.Null)
            {
                return null;
            }
            return new List<T> { token.ToObject<T>() };
        }

        public override bool CanWrite
        {
            get { return false; }
        }

        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            throw new NotImplementedException();
        }
    }

}