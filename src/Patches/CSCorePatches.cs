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

[HarmonyPatch(typeof(CSCore.Ffmpeg.FfmpegCalls))]
internal class CSCorePatches {
    [HarmonyPrefix]
    [HarmonyPatch(typeof(CSCore.Ffmpeg.FfmpegCalls), "AvformatOpenInput")]
    [HarmonyPatch(new Type[] { typeof(CSCore.Ffmpeg.Interops.AVFormatContext**), typeof(string) })]
    public static unsafe bool Prefix(CSCore.Ffmpeg.Interops.AVFormatContext** formatContext, string url) {
        string useragent = NetRadio.PluginName + "/" + NetRadio.PluginVersion + " Lavf/57.56.100";
        CSCore.Ffmpeg.Interops.AVDictionary* options = null;
        CSCore.Ffmpeg.Interops.ffmpeg.av_dict_set(&options, "user-agent", useragent, 0);
        int result = CSCore.Ffmpeg.Interops.ffmpeg.avformat_open_input(formatContext, url, null, &options);
        CSCore.Ffmpeg.FfmpegException.Try(result, "avformat_open_input");
        return false;
    }
}