using BepInEx;
using BepInEx.Configuration;
using UnityEngine;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using NetRadio.Apps;

namespace NetRadio
{
    internal class Settings {
        public static ConfigEntry<string> streamURLsUnsplit;
        public static ConfigEntry<float> streamVolume;
        public static ConfigEntry<bool> playOnStartup;
        public static ConfigEntry<int> startupIndex; 
        public static ConfigEntry<bool> configureRequireConnection;

        public static ConfigEntry<float> extraBufferSec;
        public static ConfigEntry<string> keybindsReloadUnsplit;
        public static ConfigEntry<bool> noThreads;
        public static ConfigEntry<bool> restartMusic; 

        public static ConfigEntry<string> sfxPack;

        public static List<KeyCode> keybindsReload;
        public static List<string> streamTitles = new List<string>{}; 

        // appended to start of station list
        public const string FUFMurl = @"https://funkyunclefm.net:8443/fufm";
        public static readonly List<string> previousFUFMurls = new List<string>() {
            "fancynoise.net:8443/fufm.mp3", 
        };

        public static readonly Dictionary<string, StationSettings> partneredStations = new Dictionary<string, StationSettings> {
            { "(FunkyUncleFM)" + FUFMurl + ",", new StationSettings() { metadataTimeOffsetSeconds = (decimal)7.00 } }
        };
        

        // appended to end of station list
        // potentially useful for radio expansion mods
        public static Dictionary<string, StationSettings> extraStations = new Dictionary<string, StationSettings>();

        // TEMPORARILY REMOVED // 
        //public static ConfigEntry<bool> moreCodecs; 

        public static List<string> configURLs;

        public static void BindSettings(ConfigFile Config) {
            streamURLsUnsplit = Config.Bind(
                "Settings",          // The section under which the option is shown
                "Custom Stream URLs",     // The key of the configuration option in the configuration file
                "",    // The default value
                "List of custom radio station URLs, separated by commas. To add a display name, format your link like: (StationName)https://stationlink.com/stream.mp3"); // Description of the option 
            streamURLsUnsplit.SettingChanged += UpdateSettingsEvent;

            streamVolume = Config.Bind("Settings", "Radio Volume", 1f, "Relative volume of all radio streams compared to in-game music. Note that you can set individual stations' volumes in the NetRadio app.");
            extraBufferSec = Config.Bind("Settings", "Buffer Time", 0.5f, "The length of the station's buffer in seconds. A longer buffer delays audio playback, but reduces stuttering.");

            playOnStartup = Config.Bind("Settings", "Play Radio on Startup", false, "Whether or not a radio station should automatically play upon loading a save.");
            startupIndex = Config.Bind("Settings", "Startup Radio Station", 0, "The index of the station to play automatically if Play Radio on Startup is enabled (the first station in the NetRadio app is at index 0). If set to -1, the station will be chosen randomly.");
            configureRequireConnection = Config.Bind("Settings", "Configuring Stations Requires Connection", false, "Whether it is possible to change a station's settings within the NetRadio app without connecting to the station.");
            restartMusic = Config.Bind("Settings", "Restart Interrupted Music", true, "Whether to either restart the in-game music or reconnect to a previous station when disconnecting from (or failing to connect to) a radio station.");
            
            sfxPack = Config.Bind("Settings", "App SFX Pack", "Default", new ConfigDescription(@"The sound effect pack to use within the NetRadio app. Affects sound effects for tuning into a station, connecting, disconnecting, failing to connect, and losing connection mid-stream. Custom SFX packs placed in /RadioApp-res/sfx are also supported.", GetSFXFolders())); 
            
            keybindsReloadUnsplit = Config.Bind("Settings", "Reload Radios Keybinds", "F11", "Press to reload all in-game radio streams.");
            keybindsReloadUnsplit.SettingChanged += UpdateSettingsEvent;

            noThreads = Config.Bind("Settings", "Disable Multithreading", false, "EXPERIMENTAL! Prevent multithreading for radio streaming, causing the game to freeze when starting a radio. Not recommended.");
            //moreCodecs = Config.Bind("Settings", "Enable Media Foundation Codecs", false, "EXPERIMENTAL! Windows only. Allow the use of Windows Media Foundation codecs instead of FFmpeg. Note that if the Media Foundation player fails, the mod will fallback to FFmpeg.");


        }

        public static void UpdateSettingsEvent(object sender, EventArgs args) {
            LoadURLs();
            RefreshMusicApp();
        }

        public static void RefreshMusicApp() {
            if (NetRadio.player != null && NetRadio.player.phone != null)  { 
                if (NetRadio.player.phone.m_CurrentApp is AppNetRadio && NetRadio.player.phone.IsOn && NetRadio.player.phoneLayerWeight >= 1f) {
                    AppNetRadio.Instance.ReloadButtons();
                }
            }
        }

        public static void LoadURLs() {
            string partneredStationURLs = string.Concat(partneredStations.Keys.ToArray());
            string extraStationURLs = string.Concat(extraStations.Keys.ToArray()); 
            string trueStreamURLs =  partneredStationURLs + streamURLsUnsplit.Value + extraStationURLs;

            List<string> stations = SplitStringByCommas(trueStreamURLs).Distinct().ToList();
            List<string> newStreams = new List<string>{}; 
            streamTitles.Clear();
            
            foreach (string station in stations) {
                if (string.IsNullOrWhiteSpace(station)) { continue; }

                string[] splitStation = station.Split(new [] { ")" }, StringSplitOptions.RemoveEmptyEntries);
                string streamTitle = splitStation.Length > 1 ? splitStation[0].Split(new [] { "(" }, StringSplitOptions.None)[1] : NetRadio.PluginName;
                string streamURL = splitStation.Length > 1 ? splitStation[1].Trim() : station.Trim();

                if (!streamURL.StartsWith("https://") && !streamURL.StartsWith("http://")) {
                    NetRadio.Log.LogInfo("adding HTTP to stream " + streamURL);
                    streamURL = "http://" + streamURL;
                }

                newStreams.Add(streamURL);
                streamTitles.Add(streamTitle.Trim());
                NetRadio.Log.LogInfo("Added stream " + streamURL);
            }

            NetRadio.GlobalRadio.streamURLs = newStreams;
            configURLs = newStreams;
            keybindsReload = KeyCodeListFromList(SplitStringByCommas(keybindsReloadUnsplit.Value));

            AppNetRadio.originalURLLabelText.Clear();
            AppNetRadio.urlWrapOffsets.Clear();
            AppNetRadio.urlIsTooLong.Clear();
        }

        public static List<string> SplitStringByCommas(string input) {
            return input.Replace("\n", "").Replace("\r", "").Replace("\t", "").Replace(",  ", ",").Replace(", ", ",").Split(new [] { "," }, StringSplitOptions.RemoveEmptyEntries).ToList();
        }

        public static KeyCode StringToKeyCode(string input) {
            KeyCode returnValue;
            if (KeyCode.TryParse(input, true, out returnValue)) { 
                return (KeyCode)returnValue;
            }
            return (KeyCode)0;
        }

        public static List<KeyCode> KeyCodeListFromList(List<string> input) {
            List<KeyCode> returnList = new List<KeyCode>();
            foreach (string keycodeAsString in input) {
                returnList.Add(StringToKeyCode(keycodeAsString)); 
            }
            return returnList;
        }

        public static AcceptableValueList<string> GetSFXFolders() {
            List<string> sfxFolders = new List<string>(["Default", "Legacy", "Dial-up", "Skype", "Discord", "Wii", "3DS"]); 
            try {
                foreach (string directory in Directory.GetDirectories(Path.Combine(AppNetRadio.dataDirectory, "sfx/"))) {
                    string trimmedDirectory = directory.Split(["/"], StringSplitOptions.RemoveEmptyEntries).Last().Trim();                 
                    bool unique = true; 
                    foreach (string already in sfxFolders) {
                        if (already.ToLower() == trimmedDirectory.ToLower()) { unique = false; }
                    }

                    if (unique) {
                        if (trimmedDirectory == trimmedDirectory.ToLower()) {
                            System.Globalization.TextInfo textInfo = new System.Globalization.CultureInfo("en-US", false).TextInfo;
                            trimmedDirectory = textInfo.ToTitleCase(trimmedDirectory); 
                        }
                        sfxFolders.Add(trimmedDirectory);
                    }
                }
                return new AcceptableValueList<string>(sfxFolders.ToArray());
            } catch (System.Exception) {
                return new AcceptableValueList<string>(sfxFolders.ToArray());
            }
        }
    }
}