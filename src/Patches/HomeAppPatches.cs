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

[HarmonyPatch(typeof(AppHomeScreen))]
internal class HomeAppPatches {
    [HarmonyPostfix]
    [HarmonyPatch(nameof(AppHomeScreen.OpenApp))]
    public static void Postfix_StartupSound(HomescreenButton appToOpen, AppHomeScreen __instance) {
        if (Type.GetType("Reptile.Phone." + appToOpen.AssignedApp.AppName) == typeof(AppNetRadio)) {
            if (AppNetRadio.HasSFX("start", AppNetRadio.currentSFXPack)) {
                __instance.m_AudioManager.audioSources[3].Stop(); 
                AppNetRadio.PlaySFXFromPack("start", AppNetRadio.currentSFXPack); 
            }
        }
    }
}