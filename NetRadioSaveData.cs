using CommonAPI;
using Reptile;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using static NetRadio.NetRadio;

namespace NetRadio
{
    public class NetRadioSaveData : CustomSaveData
    {
        public static NetRadioSaveData Instance { get; private set; }
        
        public static readonly int saveVersion = 0; // current save file version (Write())
        private static int readSaveVersion = 0; // save version from file (Read())

        //public static List<decimal> stationVolumes = new List<decimal>{}; 
        public static Dictionary<string, decimal> stationVolumesByURL = new Dictionary<string, decimal>();

        // save location: %localappdata%\Bomb Rush Cyberfunk Modding\NetRadio\saves
        public NetRadioSaveData() : base("NetRadio", "Slot{0}.data", SaveLocations.LocalAppData)
        {
            Instance = this;
            AutoSave = false; 
        }

        // Starting a new save - start from zero.
        public override void Initialize()
        {
            stationVolumesByURL = new Dictionary<string, decimal>();
            NetRadioSettings.LoadURLs();
            foreach (string streamURL in NetRadioSettings.configURLs) {
                if (!stationVolumesByURL.ContainsKey(StandardizeURL(streamURL))) {
                    stationVolumesByURL.Add(StandardizeURL(streamURL), (decimal)1.00);
                }
            }
        }

        public override void Read(BinaryReader reader)
        {
            stationVolumesByURL.Clear();
            readSaveVersion = reader.ReadByte(); // save file version
            var pairCount = reader.ReadInt32();

            for(var i = 0; i < pairCount; i++) {
                var key = reader.ReadString();
                var value = reader.ReadDecimal();
                if (!stationVolumesByURL.ContainsKey(key)) {
                    stationVolumesByURL.Add(key, value);
                }
            }
        }

        public override void Write(BinaryWriter writer)
        {
            writer.Write((byte)saveVersion);
            writer.Write((Int32)stationVolumesByURL.Count);
            foreach (string stationURL in stationVolumesByURL.Keys) {
                writer.Write((string)stationURL);
                writer.Write((decimal)stationVolumesByURL[stationURL]);
            }
        }
    }
}