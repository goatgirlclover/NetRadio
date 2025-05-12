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

namespace NetRadio.Patches {
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

    [HarmonyPatch(typeof(SimplePhoneButton))]
    internal class ButtonPatches {
        static IEnumerable<MethodBase> TargetMethods() {
            var type = typeof(SimplePhoneButton);
            yield return AccessTools.Method(type, "PlayHoldAnimation");
            yield return AccessTools.Method(type, "PlayHighlightAnimation");
            yield return AccessTools.Method(type, "PlayDeselectAnimation");
            yield return AccessTools.Method(type, "CacheResources");
        }

        public static void Postfix(SimplePhoneButton __instance) {
            if ((player.phone.m_CurrentApp is AppNetRadio && AppNetRadio.runPrefix) || player.phone.m_CurrentApp is AppSelectedStation) {
                if (NetRadio.IsHeaderButton(__instance)) { __instance.ButtonImage.sprite = AppNetRadio.BlankButtonSprite; }
                if (!NetRadio.IsStationButton(__instance)) { return; }
                
                var currentSprite = __instance.ButtonImage.sprite;
                bool selected = currentSprite == __instance.SelectedButtonSprite;

                TextMeshProUGUI urlLabel = __instance.Label.gameObject.transform.GetChild(0).gameObject.GetComponent<TextMeshProUGUI>();
                if (urlLabel == null) { return; }
                urlLabel.faceColor = selected ? __instance.LabelUnselectedColor : __instance.LabelSelectedColor;
                __instance.ButtonImage.sprite = selected ? AppNetRadio.SelectedButtonSprite : AppNetRadio.UnselectedButtonSprite;
                
                __instance.ButtonImage.gameObject.RectTransform().sizeDelta = new Vector2(552f * 2f, 152f * 2f);
                AppNetRadio.time = -1f;

                Image stationIcon = __instance.Label.gameObject.transform.GetChild(2).gameObject.GetComponent<Image>();
                if (stationIcon == null || stationIcon.gameObject.name.Contains("Logo")) {
                    stationIcon.sprite = AppNetRadio.GetStationLogo(__instance.Label.text, selected);
                }
                //AppNetRadio.urlWrapOffsets.Clear();
            }
        }
    }
}