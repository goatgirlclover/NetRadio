using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using Reptile;
using Reptile.Phone;
using UnityEngine;
using TMPro;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.IO;
using CommonAPI;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

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
        //public static AppNetRadio radioApp { get { return player.phone.GetAppInstance<AppNetRadio>(); } set { radioApp = value as AppNetRadio; } }

        public const string PluginName = "NetRadio";
        public const string PluginGUID = "goatgirl.NetRadio";
        public const string PluginVersion = "1.0.0";

        public static bool gameStarted = false;

        public static bool PlayerUsingMusicApp() {
            if (player == null || player.phone == null)  { return false; }
            return (player.phone.m_CurrentApp is AppMusicPlayer && player.phone.IsOn && player.phoneLayerWeight >= 1f);
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
        }
    }
}