using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using Reptile;
using Reptile.Phone;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Networking;
using TMPro;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net;
using System.Text;
using CommonAPI;
using CommonAPI.Phone;
using CommonAPI.UI;
using NetRadio.Apps;
using NetRadio.Metadata;

namespace NetRadio
{
    public class NetRadio {
        public static System.Random rando = new System.Random();
        public static ManualLogSource Log;
        public static NetRadioPlugin PluginInstance { get { return NetRadioPlugin.Instance; }}

        public static MusicPlayer musicPlayer { get { return Core.Instance.AudioManager.MusicPlayer as MusicPlayer; } }
        public static Player player { get { return WorldHandler.instance?.GetCurrentPlayer(); }}
        public static AudioSubSystem audioSubSystem { get { return Reptile.Core.Instance.AudioManager.audioSubSystem as AudioSubSystem; } }
        public static AudioManager audioManager { get { return Reptile.Core.Instance.AudioManager as AudioManager; } }

        public static float radioMusicVolume { get { return audioSubSystem.GetChannelVolumeScale(AudioChannelID.Music, audioManager.musicAudioMaxVolume01Clamped) * audioSubSystem.GetChannelVolumeScale(AudioChannelID.Master, audioManager.masterAudioMaxVolume01Clamped) * Settings.streamVolume.Value; }}
        public static float sfxVolume { get { return audioSubSystem.GetChannelVolumeScale(AudioChannelID.Master, audioManager.masterAudioMaxVolume01Clamped) * audioSubSystem.GetChannelVolumeScale(AudioChannelID.Sfx, audioManager.masterAudioMaxVolume01Clamped); } }

        public static NetRadioManager GlobalRadio;
        //public static AppNetRadio radioApp { get { return AppNetRadio.Instance } }

        public const string PluginName = "NetRadio";
        public const string PluginGUID = "goatgirl.NetRadio";
        public const string PluginVersion = "2.2.3";

        public static bool gameStarted = false;

        public static Color LabelSelectedColorDefault = new Color32(49, 90, 165, 255);
        public static Color LabelUnselectedColorDefault = Color.white;

        public static int waveOutLatency = 50;
        public static float bufferTimeInSeconds { get { return Settings.extraBufferSec.Value; }}

        public static List<string> hasRedir = new List<string>{};

        public static string customSFXpath { get { return Path.Combine(BepInEx.Paths.ConfigPath, PluginName, "CustomSFXPacks"); } }

        public static System.Globalization.CultureInfo culture; 

        public static void Update() {
            if (NetRadio.GlobalRadio is NetRadioManager) {
                if (NetRadio.GlobalRadio.playing) {
                    NetRadio.UpdateGlobalRadioVolume();
                    if (NetRadio.musicPlayer.IsPlaying) { NetRadio.musicPlayer.ForcePaused(); }
                }

                if (AppNetRadio.waveOut != null && AppNetRadio.playing) {
                    if (NetRadio.PlayerUsingApp(typeof(AppNetRadio))) {
                        if (NetRadio.GlobalRadio.failedToLoad) { PluginInstance.StartCoroutine(AppNetRadio.Instance.HandleFailedConnection()); }
                        else if (NetRadio.GlobalRadio.playing) { 
                            AppNetRadio.PlaySFX("success");
                            PluginInstance.StartCoroutine(AppNetRadio.Instance.StopIn(0f)); 
                        }
                    } else if (NetRadio.GlobalRadio.playing || NetRadio.GlobalRadio.failedToLoad) {
                        NetRadio.GlobalRadio.failedToLoad = false;
                        AppNetRadio.waveOut.Stop();
                        AppNetRadio.playing = false;
                    }
                }
            }

            //if (NetRadio.pressedAnyButtonIn(Settings.keybindsReload)) {
            //    NetRadioManager.ReloadAllStations();
            //}
        }

        public static bool PlayerUsingMusicApp() { return PlayerUsingApp(typeof(AppMusicPlayer)); }

        public static bool PlayerUsingApp(Type checkAppType) {
            if (player == null || player.phone == null)  { return false; }
            try {
                Type currentAppType = player.phone.m_CurrentApp.GetType();
                bool usingPhone = player.phone.IsOn && player.phoneLayerWeight >= 1f;
                return currentAppType == checkAppType && usingPhone;
            } catch (System.Exception) { return false; }
        }

        public static bool pressedAnyButtonIn(List<KeyCode> keybinds) {
            foreach (KeyCode key in keybinds) { if (UnityEngine.Input.GetKeyUp(key)) { return true; } } 
            return false;
        }

        public static Sprite LoadSprite(String path) {
            Sprite returnSprite = CommonAPI.TextureUtility.LoadSprite(path);
            returnSprite.texture.filterMode = FilterMode.Bilinear;
            returnSprite.texture.wrapMode = TextureWrapMode.Clamp;
            return returnSprite;
        }

        public static async Task MuteUntilRadioPlaying() {
            while (!GlobalRadio.playing) {
                musicPlayer.ForcePaused();
                await Task.Yield();
            }

            AppNetRadio.waveOut.Stop();
            AppNetRadio.playing = false;
            AppNetRadio.PlaySFX("success");
        }

        public static bool IsStationButton(SimplePhoneButton button) {
            if (button == null || button.Label == null) { return false; }
            int childCount = button.Label.gameObject.transform.childCount;
            bool textMatches = button.Label.text.Contains("Custom Station") || button.Label.text == PluginName || Settings.streamTitles.Contains(button.Label.text);
            return childCount > 2 && textMatches;
        }

        public static bool IsHeaderButton(SimplePhoneButton button) {
            if (button == null || button.Label == null) { return false; }
            int childCount = button.Label.gameObject.transform.childCount;
            if (childCount < 1) { return false; }
            return button.Label.gameObject.transform.GetChild(0).gameObject.name.Contains("Header");
        }

        public static string StandardizeURL(string originalURL) { // for app display and savedata matching
            string shortURL = originalURL.Replace("https://", "").Replace("http://", "").Trim().ToLower().TrimEnd('/'); 
              //originalURL.Replace("https://", "").Replace("http://", "").Replace("www.", "").Trim();
            return shortURL;
        }

        public static string ReadIcyHeader(HttpResponseMessage response, string header) {
            string icyheader = header.StartsWith("icy-") ? header : "icy-" + header;
            IEnumerable<string> headerValues;
            if (response.Headers.TryGetValues(icyheader, out headerValues)) {
                return headerValues.First();
            }
            return null;
        }

        public static string GetRedirectedURL(string url)
        {
            try
            {
                var request = (HttpWebRequest)HttpWebRequest.Create(url);
                request.Method = "POST";
                request.AllowAutoRedirect = true;
                request.Timeout = 2000; // 2 sec
                //request.ContentType = "application/x-www-form-urlencoded";
                var response = request.GetResponse();
                string uri = response.ResponseUri.AbsoluteUri.ToString();
                hasRedir.Add(uri);
                if (!uri.Equals(url)) { Log.LogInfo("Updating internal station URL " + url + " - redirects to " + uri); }
                return uri;
            }
            catch(Exception) { 
                return null; 
            }
        }

        public static string GetParentUriString(Uri uri)
        {            
            StringBuilder parentName = new StringBuilder();
            parentName.Append(uri.Scheme);            
            parentName.Append("://");
            parentName.Append(uri.Host);
            if (uri.Port != -1) {
                parentName.Append(":"); 
                parentName.Append(uri.Port); 
            }
            for (int i = 0; i < uri.Segments.Length - 1; i++) {
                parentName.Append(uri.Segments[i]);
            }
            return parentName.ToString();
        }

        public static void UpdateGlobalRadioVolume() {
            string urlForCurrent = NetRadio.StandardizeURL(NetRadio.GlobalRadio.currentStationURL);
            float volumeMultiplier = SaveData.stationSettingsByURL.ContainsKey(urlForCurrent) 
                    ? (float)SaveData.stationSettingsByURL[urlForCurrent].volume : 1f;
            NetRadio.GlobalRadio.radioVolume = NetRadio.radioMusicVolume * volumeMultiplier;
        }

        public static void UpdateCurrentSong(bool skipCheck = false) {
            if (!NetRadio.GlobalRadio.playing) { return; }

            var originalCulture = System.Threading.Thread.CurrentThread.CurrentCulture;
            System.Threading.Thread.CurrentThread.CurrentCulture = culture;
            
            try {
                DateTime currentDateTime = DateTime.Now;
                string songName = GlobalRadio.currentSong;
                if (!AppTrackHistory.trackHistory.ContainsKey(GlobalRadio.currentStation)) {
                    SortedDictionary<DateTime, string> sortedDictionary = new SortedDictionary<DateTime, string>();
                    sortedDictionary.Add(currentDateTime, songName);
                    AppTrackHistory.trackHistory.Add(GlobalRadio.currentStation, sortedDictionary);
                } else {
                    AppTrackHistory.trackHistory[GlobalRadio.currentStation].Add(currentDateTime, songName); 
                }
            } catch (System.Exception ex) {
                Log.LogError($"Error updating track history: {ex.Message}"); 
                Log.LogError(ex.StackTrace);
            }

            System.Threading.Thread.CurrentThread.CurrentCulture = originalCulture;

            NewTrackEventArgs args = new NewTrackEventArgs { SongName = GlobalRadio.currentSong };
            NetRadioPlugin.Instance.OnNewTrack(args);
            
            if (skipCheck || NetRadio.PlayerUsingMusicApp()) {
                AppMusicPlayer musicApp = NetRadio.player.phone.m_CurrentApp as AppMusicPlayer;
                
                MusicTrack dummyTrack = ScriptableObject.CreateInstance<MusicTrack>(); //new MusicTrack();
                dummyTrack.AudioClip = null;
                dummyTrack.Artist = NetRadio.GlobalRadio.GetStationTitle();
                dummyTrack.Title = NetRadio.GlobalRadio.currentSong; 
                
                musicApp.m_StatusPanel.OnNewTrack(dummyTrack);
            }
        }

        public static Source GetSource(IcecastStatus icecastStatus, string url = "") {
            List<Source> sources = icecastStatus.icestats.source;
            if (!(sources.Count == 1 || string.IsNullOrWhiteSpace(url))) {
                try {
                    string mountPoint = url.Split(["/"], StringSplitOptions.RemoveEmptyEntries).Last().Trim();
                    foreach (Source source in sources) {
                        if (source.listenurl.Contains(mountPoint)) { return source; }
                    }
                } catch (System.Exception) { } // don't do anything
            }
            return sources[0];
        }

        public static string ShortenNumber(int number) {
            if (number < 1000) { return number.ToString(); } 
            else if (number < 100000) {
                double num = number / 1000.0;
                return num < 10 ? num.ToString("0.#") + "k" :
                       num.ToString("0") + "k";
            } else {
                double num = number / 1000000.0;
                return num < 10 ? num.ToString("0.#") + "M" :
                       num.ToString("0") + "M";
            }
        }
    }
}