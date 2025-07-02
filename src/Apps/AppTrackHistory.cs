using Reptile;
using Reptile.Phone;
using UnityEngine;
using UnityEngine.UI;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using CommonAPI;
using CommonAPI.Phone;
using CommonAPI.UI;
using TMPro;
using static NetRadio.NetRadio;
using NetRadio.Metadata;

namespace NetRadio.Apps;

public class AppTrackHistory : NetRadioCustomApp {
    public static AppTrackHistory Instance;
    public static int currentStationIndex { get { return AppSelectedStation.currentStationIndex; }}

    public static Dictionary<int, SortedDictionary<DateTime, string>> trackHistory = new Dictionary<int, SortedDictionary<DateTime, string>>(); 

    public static float appTime = 1f;
    public static bool refreshedButtonColors = false;

    public override bool Available => false;

    public static void Initialize() { 
        PhoneAPI.RegisterApp<AppTrackHistory>("station track history"); 
    }

    public override void OnAppInit()
    {
        base.OnAppInit();
        Instance = this;
        ScrollView = PhoneScrollView.Create(this);
    }   

    public override void OnAppEnable() {
        CreateAndSaveTitleBar(GlobalRadio.GetStationTitle(currentStationIndex), AppNetRadio.UnselectedAntennaSprites[2]);
        AddButtons();
        base.OnAppEnable();
        appTime = 1f;
    }

    private void AddButtons() {
        var originalCulture = System.Threading.Thread.CurrentThread.CurrentCulture;
        System.Threading.Thread.CurrentThread.CurrentCulture = NetRadio.culture;

        SortedDictionary<DateTime, string> dictionary = trackHistory[currentStationIndex];
        string previousSong = "";

        foreach (DateTime time in dictionary.Keys.Reverse()) {
            string song = dictionary[time]; 
            if (previousSong != song) { 
                string shortTime = time.ToShortTimeString();
                var nextButton = PhoneUIUtility.CreateSimpleButton("(" + shortTime + ") " + song);
                nextButton.OnConfirm += () => { 
                    try {
                        RefreshButtonColors();
                        SimplePhoneButton button = ScrollView.Buttons[ScrollView.SelectedIndex] as SimplePhoneButton;
                        string songName = button.Label.text.Split(new char[] {')'}, 2, StringSplitOptions.RemoveEmptyEntries)[1].Trim();
                        GUIUtility.systemCopyBuffer = songName;
                        SetLabelColor(button, Color.green);
                        appTime = 0f;
                        refreshedButtonColors = false;
                    } catch (System.Exception) { } // do nothing
                };
                ScrollView.AddButton(nextButton);
            }
            previousSong = song;
        }

        System.Threading.Thread.CurrentThread.CurrentCulture = originalCulture;
    }
    public override void OnAppUpdate() {
        appTime += Time.deltaTime;
        if (appTime >= 1f && !refreshedButtonColors) {
            RefreshButtonColors();
        }
        base.OnAppUpdate();
    }

    private void RefreshButtonColors() {
        foreach(SimplePhoneButton button in ScrollView.Buttons) {
            if (button.Label.faceColor == Color.green) {
                SetLabelColor(button, Color.white);
            }
        }
        refreshedButtonColors = true;
    }

    public override void OnAppDisable()
    {
        ScrollView.RemoveAllButtons();
        base.OnAppDisable();
        Destroy(overlayInstance.gameObject);
    }

    public void RefreshButtons() {
        if (!ScrollView.Buttons.Any()) { return; }
        int originalIndex = ScrollView.SelectedIndex;
        int originalCount = ScrollView.Buttons.Count;

        ScrollView.RemoveAllButtons();
        AddButtons();

        int difference = ScrollView.Buttons.Count - originalCount;
        if (originalIndex != 0) { originalIndex += difference; }
        ScrollView.SelectedIndex = originalIndex;
    }
}