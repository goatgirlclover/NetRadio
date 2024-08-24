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
using System.Diagnostics;
using CSCore;
using CSCore.Ffmpeg;
using CSCore.Streams;
using CommonAPI;

namespace NetRadio
{
    [BepInPlugin(NetRadio.PluginGUID, NetRadio.PluginName, NetRadio.PluginVersion)]
    [BepInProcess("Bomb Rush Cyberfunk.exe")]
    [BepInDependency("CommonAPI", BepInDependency.DependencyFlags.SoftDependency)]
    [BepInDependency("kade.bombrushradio", BepInDependency.DependencyFlags.SoftDependency)]
    [BepInDependency("goatgirl.MusicCurator", BepInDependency.DependencyFlags.SoftDependency)]
    [BepInDependency("goatgirl.NetRadioPOC", BepInDependency.DependencyFlags.SoftDependency)]
    public class NetRadioPlugin : BaseUnityPlugin
    {
        //internal static ManualLogSource Log; // LiveRadio.Log
        internal static Harmony Harmony = new Harmony(NetRadio.PluginGUID);
        public static NetRadioPlugin Instance { get; private set; }
        public string Directory => Path.GetDirectoryName(Info.Location);

        public static bool hasBRR = false;
        public static bool hasMusicCurator = false;

        private void Awake()
        {
            Instance = this;
            NetRadio.Log = base.Logger;
            Harmony.PatchAll(); 
            Logger.LogInfo($"Plugin " + NetRadio.PluginName + " is loaded!");

            NetRadio.GlobalRadio = NetRadioManager.CreateRadio(this.transform, false, false);
            NetRadio.GlobalRadio.volume = 1.0f;
            
            AppNetRadio.Initialize();
            AppSelectedStation.Initialize();
            NetRadioSettings.BindSettings(Config);
            //NetRadioSettings.LoadURLs(); // moved to global radiomanager start    

            foreach (var plugin in BepInEx.Bootstrap.Chainloader.PluginInfos) { 
                if (plugin.Value.Metadata.GUID.Contains("BombRushRadio")) { hasBRR = true; } 
                if (plugin.Value.Metadata.GUID.Contains("MusicCurator")) { hasMusicCurator = true; }
                if (plugin.Value.Metadata.GUID.Contains("LiveRadioPOC")) {
                    NetRadio.Log.LogError("Development version of " + NetRadio.PluginGUID + " detected! Please delete the development version (goatgirl.LiveRadioPOC)!");
                }
            }       

            if (Debugger.IsAttached)
            {
                FfmpegUtils.LogToDefaultLogger = false;
                FfmpegUtils.FfmpegLogReceived += (s, e) =>
                {
                    Logger.LogError(e.Message);
                };
            }
        }

        /* private void Start() {
            new AudioDeviceMonitor();
        } */

        private void OnDestroy() {
            Harmony.UnpatchSelf(); 
        }

        private void Update() {
            if (NetRadio.GlobalRadio is NetRadioManager) {
                if (NetRadio.GlobalRadio.playing) {
                    UpdateGlobalRadioVolume();
                    if (NetRadio.musicPlayer.IsPlaying) { NetRadio.musicPlayer.ForcePaused(); }
                }

                if (AppNetRadio.waveOut != null && AppNetRadio.playing) {
                    if (NetRadio.PlayerUsingApp(typeof(AppNetRadio))) {
                        if (NetRadio.GlobalRadio.failedToLoad) { StartCoroutine(AppNetRadio.Instance.HandleFailedConnection()); }
                        else if (NetRadio.GlobalRadio.playing) { StartCoroutine(AppNetRadio.Instance.StopIn(1f)); }
                    } else if (NetRadio.GlobalRadio.playing || NetRadio.GlobalRadio.failedToLoad) {
                        NetRadio.GlobalRadio.failedToLoad = false;
                        AppNetRadio.waveOut.Stop();
                        AppNetRadio.playing = false;
                    }
                }
            }

            if (NetRadio.pressedAnyButtonIn(NetRadioSettings.keybindsReload)) {
                NetRadioManager.ReloadAllStations();
            }
        }

        public static void UpdateGlobalRadioVolume() {
            string urlForCurrent = NetRadio.StandardizeURL(NetRadio.GlobalRadio.currentStationURL);
            float volumeMultiplier = NetRadioSaveData.stationVolumesByURL.ContainsKey(urlForCurrent) ? (float)NetRadioSaveData.stationVolumesByURL[urlForCurrent] : 1f;
            NetRadio.GlobalRadio.radioVolume = NetRadio.radioMusicVolume * volumeMultiplier;
        }

        public static void UpdateCurrentSong(bool skipCheck = false) {
            if (!NetRadio.GlobalRadio.playing) { return; }
            if (skipCheck || NetRadio.PlayerUsingMusicApp()) {
                AppMusicPlayer musicApp = NetRadio.player.phone.m_CurrentApp as AppMusicPlayer;
                
                MusicTrack dummyTrack = ScriptableObject.CreateInstance<MusicTrack>(); //new MusicTrack();
                dummyTrack.AudioClip = null;
                dummyTrack.Artist = NetRadio.GlobalRadio.GetStationTitle();
                dummyTrack.Title = NetRadio.GlobalRadio.currentSong; 
                
                musicApp.m_StatusPanel.OnNewTrack(dummyTrack);
            }
        }
    }
}