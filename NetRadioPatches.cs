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

namespace NetRadio {
    [HarmonyPatch(typeof(AppMusicPlayer))]
    internal class MusicAppPatches {
        //[HarmonyPostfix]
        //[HarmonyPatch(nameof(AppMusicPlayer.PlaySong))]
        //public static void AppPlaySong_StopRadio() {
            //GlobalRadio.Stop();
        //}

        [HarmonyPrefix]
        [HarmonyPatch(nameof(AppMusicPlayer.OnReleaseRight))]
        public static bool OnReleaseRight_ClearCurrent(AppMusicPlayer __instance) {
            if (GlobalRadio.playing) {
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
            NetRadioPlugin.UpdateCurrentSong(true); 
        }
    }

    [HarmonyPatch(typeof(MusicPlayerStatusPanel))]
    internal class MusicAppPanelPatches {
        [HarmonyPostfix]
        [HarmonyPatch(nameof(MusicPlayerStatusPanel.StatusIconUpdate))]
        public static void UpdateStatusIcon(MusicPlayerStatusPanel __instance) {
            if (GlobalRadio.playing) {
                __instance.m_StatusImage.sprite = __instance.m_StatusIcons[0];
                float intensity = 1f; // 0.5f;
                float speed = 0.65f; // 0.5f;
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
                NetRadioPlugin.UpdateCurrentSong();
            }
        }
    }

    [HarmonyPatch(typeof(BaseModule))]
    internal class ReloadMainMenu {
        [HarmonyPatch(nameof(BaseModule.LoadMainMenuScene))]
        [HarmonyPostfix]
        static void LoadingMenu_ResetVars() {
            NetRadio.gameStarted = false;
            GlobalRadio.Stop();
        }
    }

    [HarmonyPatch(typeof(StageManager))]
    internal class SMPatches {
        [HarmonyPostfix]
        [HarmonyPatch(typeof(StageManager), "StartMusicForStage", new Type[] { typeof(Stage), typeof(int) })]
        public static void StartupRadio() {
            if (NetRadio.gameStarted) { return; }
            NetRadio.gameStarted = true;

            if (NetRadioSettings.playOnStartup.Value) {
                NetRadioPlugin.Instance.StartCoroutine(NetRadio.MuteUntilRadioPlaying());
                if (NetRadioSettings.startupIndex.Value == -1) { NetRadio.GlobalRadio.PlayRandomStation(); }
                else { NetRadio.GlobalRadio.PlayRadioStation(NetRadioSettings.startupIndex.Value); }
                //AppNetRadio.waveOut.Play();
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
                if (NetRadio.IsHeaderButton(__instance)) {
                    __instance.ButtonImage.sprite = AppNetRadio.BlankButtonSprite;
                }
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


    // spatial radio testing
    [HarmonyPatch(typeof(ASceneSetupInstruction))]
	internal class patch_StageManager_OnPostRender
	{
        [HarmonyPatch("SetSceneActive")]
		private static void Postfix(string sceneToSetActive)
		{
            //NetRadio.gameStarted = false;
            return; /*
			Junk[] array = UnityEngine.Object.FindObjectsOfType<Junk>();
			Junk[] array2 = array;
			foreach (Junk val in array2)
			{
				if (((UnityEngine.Object)val).name == "TV")
				{
                    NetRadioManager lm = NetRadioManager.CreateRadio(val.transform, true, true, new List<string> {"http://as.fm1.be:8000/media"});
				}
			} */
		}
	}

    
}