using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using Reptile;
using Reptile.Phone;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.IO;
using CommonAPI;
using CommonAPI.Phone;

using static NetRadio.NetRadio;
using NetRadio.Apps;

namespace NetRadio.Patches;

[HarmonyPatch(typeof(AppMusicPlayer))]
internal class MusicAppPatches {
    [HarmonyPrefix]
    [HarmonyPatch(nameof(AppMusicPlayer.OnReleaseRight))]
    public static bool OnReleaseRight_ClearCurrent(AppMusicPlayer __instance) {
        if (GlobalRadio.playing && !__instance.shuffleControlSelected) {
            GlobalRadio.Stop();
            __instance.m_StatusPanel.m_CurrentTrack = null;
            (__instance.GameMusicPlayer as MusicPlayer).musicTrackQueue.currentTrackIndex = -1;
            __instance.m_StatusPanel.OnNewTrack((__instance.m_TrackList.SelectedButtton as MusicPlayerTrackButton).AssignedContent as MusicTrack);
        }
        return true;
    }

    [HarmonyPostfix]
    [HarmonyPatch(nameof(AppMusicPlayer.OnAppEnable))]
    public static void OnEnable_Refresh() {
        NetRadio.UpdateCurrentSong(true); 
    }
}