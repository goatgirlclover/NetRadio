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

[HarmonyPatch(typeof(BaseModule))]
internal class ReloadMainMenu {
    [HarmonyPatch(nameof(BaseModule.LoadMainMenuScene))]
    [HarmonyPostfix]
    static void LoadingMenu_ResetVars() {
        NetRadio.gameStarted = false;
        GlobalRadio.CleanUp();//GlobalRadio.Stop();
    }
}

[HarmonyPatch(typeof(BaseModule))]
internal class StartupPatch {
    [HarmonyPostfix]
    [HarmonyPatch(typeof(BaseModule), "StartGameFromSaveSlot")]
    public static void StartupRadio() {
        if (NetRadio.gameStarted) { return; }
        NetRadio.gameStarted = true;

        if (Settings.playOnStartup.Value) {
            _= NetRadio.MuteUntilRadioPlaying();
            if (Settings.startupIndex.Value == -1) { NetRadio.GlobalRadio.PlayRandomStation(); }
            else { NetRadio.GlobalRadio.PlayRadioStation(Settings.startupIndex.Value); }
            AppNetRadio.PlayNoise(); //AppNetRadio.waveOut.Play();
            AppNetRadio.musicPlayerWasInterrupted = false;
        }
    }
}