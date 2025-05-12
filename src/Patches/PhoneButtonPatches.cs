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