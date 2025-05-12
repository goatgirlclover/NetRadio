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

[HarmonyPatch(typeof(MusicPlayerStatusPanel))]
internal class MusicAppPanelPatches {
    [HarmonyPostfix]
    [HarmonyPatch(nameof(MusicPlayerStatusPanel.StatusIconUpdate))]
    public static void UpdateStatusIcon(MusicPlayerStatusPanel __instance) {
        if (GlobalRadio.playing) {
            __instance.m_StatusImage.sprite = __instance.m_StatusIcons[0];
            float intensity = 1f; 
            float speed = 1f; 
            Vector3 targetSize = Vector3.one * (1f + GlobalRadio.streamSampleVolume * intensity - (0.5f*intensity));
            __instance.m_StatusImage.rectTransform.localScale = Vector3.Lerp(__instance.m_StatusImage.rectTransform.localScale, targetSize, 30f * speed * Time.deltaTime);
        }
    }

    [HarmonyPostfix]
    [HarmonyPatch(nameof(MusicPlayerStatusPanel.ProgressbarUpdate))]
    public static void UpdateProgress(MusicPlayerStatusPanel __instance) {
        if (GlobalRadio.playing) {
            __instance.m_ProgressBar.fillAmount = GlobalRadio.streamSampleVolume;
        }
    }

    [HarmonyPostfix]
    [HarmonyPatch(nameof(MusicPlayerStatusPanel.OnNewTrack))]
    public static void OnNewTrack_RadioUpdate(MusicPlayerStatusPanel __instance) {
        if (__instance.m_CurrentTrack.Artist == GlobalRadio.GetStationTitle()) { return; }
        if (GlobalRadio.playing) {
            NetRadio.UpdateCurrentSong();
        }
    }
}