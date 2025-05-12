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

        public static float radioMusicVolume { get { return audioSubSystem.GetChannelVolumeScale(AudioChannelID.Music, audioManager.musicAudioMaxVolume01Clamped) * audioSubSystem.GetChannelVolumeScale(AudioChannelID.Master, audioManager.masterAudioMaxVolume01Clamped) * NetRadioSettings.streamVolume.Value; }}
        public static float sfxVolume { get { return audioSubSystem.GetChannelVolumeScale(AudioChannelID.Master, audioManager.masterAudioMaxVolume01Clamped) * audioSubSystem.GetChannelVolumeScale(AudioChannelID.Sfx, audioManager.masterAudioMaxVolume01Clamped); } }

        public static NetRadioManager GlobalRadio;
        //public static AppNetRadio radioApp { get { return AppNetRadio.Instance } }

        public const string PluginName = "NetRadio";
        public const string PluginGUID = "goatgirl.NetRadio";
        public const string PluginVersion = "2.0.0";

        public static bool gameStarted = false;

        public static Color LabelSelectedColorDefault = new Color32(49, 90, 165, 255);
        public static Color LabelUnselectedColorDefault = Color.white;

        public static int waveOutLatency = 50;
        public static float bufferTimeInSeconds { get { return NetRadioSettings.extraBufferSec.Value; }}

        public static List<string> hasRedir = new List<string>{};

        public static bool PlayerUsingMusicApp() {
            return PlayerUsingApp(typeof(AppMusicPlayer));
        }

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

        public static IEnumerator MuteUntilRadioPlaying() {
            while (!GlobalRadio.playing) {
                musicPlayer.ForcePaused();
                yield return null;
            }
            AppNetRadio.waveOut.Stop();
            AppNetRadio.playing = false;
            AppNetRadio.PlaySFX("success");
        }

        public static bool IsStationButton(SimplePhoneButton button) {
            if (button == null || button.Label == null) { return false; }
            int childCount = button.Label.gameObject.transform.childCount;
            bool textMatches = button.Label.text.Contains("Custom Station") || button.Label.text == PluginName || NetRadioSettings.streamTitles.Contains(button.Label.text);
            return childCount > 2 && textMatches;
        }

        public static bool IsHeaderButton(SimplePhoneButton button) {
            if (button == null || button.Label == null) { return false; }
            int childCount = button.Label.gameObject.transform.childCount;
            if (childCount < 1) { return false; }
            return button.Label.gameObject.transform.GetChild(0).gameObject.name.Contains("Header");
        }

        public static string StandardizeURL(string originalURL) { // for app display and savedata matching
            string shortURL = originalURL.Replace("https://", "").Replace("http://", "").Trim().ToLower().TrimEnd('/'); ///originalURL.Replace("https://", "").Replace("http://", "").Replace("www.", "").Trim();
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
    }
}