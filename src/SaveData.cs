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
    public class SaveData : CustomSaveData
    {
        public static SaveData Instance { get; private set; }
        
        public static readonly int saveVersion = 1; // current save file version (Write())
        private static int readSaveVersion = 0; // save version from file (Read())
        
        public static Dictionary<string, StationSettings> stationSettingsByURL = new Dictionary<string, StationSettings>();

        // save location: %localappdata%\Bomb Rush Cyberfunk Modding\NetRadio\saves
        public SaveData() : base("NetRadio", "Slot{0}.data", SaveLocations.LocalAppData)
        {
            Instance = this;
            AutoSave = false; 
        }

        // Starting a new save - start from zero.
        public override void Initialize()
        {
            stationSettingsByURL = new Dictionary<string, StationSettings>();
            Settings.LoadURLs();
            foreach (string streamURL in Settings.configURLs) {
                if (!stationSettingsByURL.ContainsKey(StandardizeURL(streamURL))) {
                    stationSettingsByURL.Add(StandardizeURL(streamURL), new StationSettings(streamURL));
                }
            }
        }

        public override void Read(BinaryReader reader)
        {
            stationSettingsByURL.Clear();
            readSaveVersion = reader.ReadByte(); // save file version
            var stationCount = reader.ReadInt32();

            if (readSaveVersion == 0) {
                for(var i = 0; i < stationCount; i++) {
                    var key = reader.ReadString();
                    var value = reader.ReadDecimal();
                    StationSettings stationSettings = new StationSettings(key); 
                    stationSettings.volume = value;
                    stationSettingsByURL.Add(key, stationSettings);
                }
            } else if (readSaveVersion == 1) {
                for(var i = 0; i < stationCount; i++) {
                    StationSettings value = new StationSettings(); 
                    var key = reader.ReadString();
                    value.volume = reader.ReadDecimal();
                    value.metadataMode = reader.ReadInt32();
                    value.metadataTimeOffsetSeconds = reader.ReadDecimal(); 
                    stationSettingsByURL.Add(key, value);
                }
            }
            
        }

        public override void Write(BinaryWriter writer)
        {
            if (saveVersion == 0) {
                writer.Write((byte)saveVersion);
                writer.Write((Int32)stationSettingsByURL.Count);
                foreach (string stationURL in stationSettingsByURL.Keys) {
                    writer.Write((string)stationURL);
                    writer.Write((decimal)stationSettingsByURL[stationURL].volume);
                }
            } else if (saveVersion == 1) {
                writer.Write((byte)saveVersion);
                writer.Write((Int32)stationSettingsByURL.Count);
                foreach (string stationURL in stationSettingsByURL.Keys) {
                    writer.Write((string)stationURL);
                    writer.Write((decimal)stationSettingsByURL[stationURL].volume);
                    writer.Write((Int32)stationSettingsByURL[stationURL].metadataMode);
                    writer.Write((decimal)stationSettingsByURL[stationURL].metadataTimeOffsetSeconds);
                }
            }
        }
    }

    public class StationSettings
    {
        public decimal volume = (decimal)1.00;
        public int metadataMode = 1; // 0 = off, 1 = status-json.xsl, 2 = old metaint method (not implemented)
        public decimal metadataTimeOffsetSeconds = (decimal)0.00;

        public StationSettings() { }
        public StationSettings(string associatedURL) {
            foreach (string partnerURL in Settings.partneredStations.Keys.ToList()) {
                if (partnerURL.Contains(associatedURL)) {
                    StationSettings target = Settings.partneredStations[partnerURL];
                    volume = target.volume;
                    metadataMode = target.metadataMode;
                    metadataTimeOffsetSeconds = target.metadataTimeOffsetSeconds;
                }
            }

            foreach (string extraURL in Settings.extraStations.Keys.ToList()) {
                if (extraURL.Contains(associatedURL)) {
                    StationSettings target = Settings.extraStations[extraURL];
                    volume = target.volume;
                    metadataMode = target.metadataMode;
                    metadataTimeOffsetSeconds = target.metadataTimeOffsetSeconds;
                }
            }
        }
    }
}