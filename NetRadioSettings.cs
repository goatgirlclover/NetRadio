using BepInEx;
using BepInEx.Configuration;
using UnityEngine;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.IO;

namespace NetRadio
{
    internal class NetRadioSettings {
        public static ConfigEntry<string> streamURLsUnsplit;
        public static ConfigEntry<float> streamVolume;
        public static ConfigEntry<string> keybindsReloadUnsplit;
        public static ConfigEntry<bool> noThreads;

        public static List<KeyCode> keybindsReload;

        public static List<string> streamTitles = new List<string>{}; 

        // appended to start of station list
        public const string partneredStations = @"
            (FunkyUncleFM)https://fancynoise.net:8443/fufm.mp3,
        ";

        // appended to end of station list
        public static string extraStations = ""; 

        public static void BindSettings(ConfigFile Config) {
            streamURLsUnsplit = Config.Bind(
                "Settings",          // The section under which the option is shown
                "Custom Stream URLs",     // The key of the configuration option in the configuration file
                "",    // The default value
                "List of custom radio station URLs, separated by commas. MP3 recommended. To add a display name, format your link like: (StationName)https://stationlink.com/stream.mp3"); // Description of the option 
            streamURLsUnsplit.SettingChanged += UpdateSettingsEvent;

            streamVolume = Config.Bind("Settings", "Radio Volume", 1f, "Relative volume of radio stream compared to in-game music.");

            keybindsReloadUnsplit = Config.Bind("Settings", "Reload Radios Keybinds", "F11", "Press to reload all in-game radio streams.");
            keybindsReloadUnsplit.SettingChanged += UpdateSettingsEvent;

            noThreads = Config.Bind("Settings", "Disable Multithreading", false, "EXPERIMENTAL! Prevent multithreading for radio streaming, causing the game to freeze when starting a radio. Not recommended.");
            // fast panning option - just pan the stereo output directly, using only one waveout
        }

        public static void UpdateSettingsEvent(object sender, EventArgs args) {
            LoadURLs();
            if (NetRadio.player != null && NetRadio.player.phone != null)  { 
                if (NetRadio.player.phone.m_CurrentApp is AppNetRadio && NetRadio.player.phone.IsOn && NetRadio.player.phoneLayerWeight >= 1f) {
                    AppNetRadio.Instance.ReloadButtons();
                }
            }
        }

        public static void LoadURLs() {
            // partnered stations + custom stations
            string trueStreamURLs = partneredStations + streamURLsUnsplit.Value + extraStations;
            List<string> stations = SplitStringByCommas(trueStreamURLs).Distinct().ToList();
            List<string> newStreams = new List<string>{}; 
            streamTitles.Clear();
            
            foreach (string station in stations) {
                if (string.IsNullOrWhiteSpace(station)) { continue; }

                string[] splitStation = station.Split(new [] { ")" }, StringSplitOptions.RemoveEmptyEntries);
                string streamTitle = splitStation.Length > 1 ? splitStation[0].Split(new [] { "(" }, StringSplitOptions.None)[1] : NetRadio.PluginName;
                string streamURL = splitStation.Length > 1 ? splitStation[1] : station;

                if (!streamURL.StartsWith("https://") && !streamURL.StartsWith("http://")) {
                    streamURL = "http://" + streamURL;
                }

                newStreams.Add(streamURL);
                streamTitles.Add(streamTitle);
                NetRadio.Log.LogInfo("Added stream " + streamURL);
            }

            NetRadio.GlobalRadio.streamURLs = newStreams;
            keybindsReload = KeyCodeListFromList(SplitStringByCommas(keybindsReloadUnsplit.Value));

            AppNetRadio.originalURLLabelText.Clear();
            AppNetRadio.urlWrapOffsets.Clear();
            AppNetRadio.urlIsTooLong.Clear();
        }

        public static List<string> SplitStringByCommas(string input) {
            return input.Replace(",  ", ",").Replace(", ", ",").Split(new [] { "," }, StringSplitOptions.RemoveEmptyEntries).ToList();
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
    }
}