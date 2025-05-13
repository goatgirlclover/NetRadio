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
using CSCore; 
using CSCore.Streams; 
using CSCore.Streams.SampleConverter;
using CSCore.Ffmpeg; 
using CSCore.SoundOut; 
using TMPro;
using static NetRadio.NetRadio;
using NetRadio.Metadata;

namespace NetRadio.Apps;

public class AppSelectedStation : NetRadioCustomApp {
    public static AppSelectedStation Instance;
    public static PhoneButton lastSelectedButton;
    public static int currentStationIndex;

    public static float time = 0.0f;
    public static bool justCopied = false;
    public static bool justFailedCopy = false;
    
    public static int volumeInPercent = 100;
    public static readonly string[] metadataModeDisplay = new string[] { "Disabled", "Enabled", "Alternate" };
    public static int currentMetadataMode = 1; 
    public static int metadataOffsetInMS = 0; 

    public static bool changingVolume = false;
    public static bool changingMetadataMode = false;
    public static bool changingMetadataOffset = false;
    public static bool changingAny { 
        get { return changingVolume || changingMetadataMode || changingMetadataOffset; } 
        set { changingVolume = false; changingMetadataMode = false; changingMetadataOffset = false; }
    }

    public override bool Available => false;

    public static void Initialize() { 
        PhoneAPI.RegisterApp<AppSelectedStation>("selected station"); 
    }

    public static void SetVariables() {
        string urlForCurrent = NetRadio.StandardizeURL(Settings.configURLs[currentStationIndex]);
        float volumeMultiplier = SaveData.stationSettingsByURL.ContainsKey(urlForCurrent) 
                    ? (float)SaveData.stationSettingsByURL[urlForCurrent].volume : 1f;
        float volumeHund = volumeMultiplier * 100f;
        volumeInPercent = 5 * (int)Math.Round(volumeHund / 5.0);

        currentMetadataMode = SaveData.stationSettingsByURL.ContainsKey(urlForCurrent) 
                    ? (int)SaveData.stationSettingsByURL[urlForCurrent].metadataMode : 1;
        metadataOffsetInMS = SaveData.stationSettingsByURL.ContainsKey(urlForCurrent) 
                    ? (int)(SaveData.stationSettingsByURL[urlForCurrent].metadataTimeOffsetSeconds*((decimal)1000.0)) : 0;
    }

    public override void OnAppInit()
    {
        base.OnAppInit();
        Instance = this;
        ScrollView = PhoneScrollView.Create(this);
        AddUsualButtons(); 
        var nextButton = AppNetRadio.CreateHeaderButton("Listeners: ", 75f);
        ScrollView.AddButton(nextButton);
        nextButton = AppNetRadio.CreateHeaderButton("Peak listeners: ", 75f);
        ScrollView.AddButton(nextButton);
        nextButton = AppNetRadio.CreateHeaderButton("Genre: ", 75f);
        ScrollView.AddButton(nextButton);
    }   

    public override void OnAppUpdate() {
        time += Time.deltaTime;

        if (justCopied && time > 1.0) {
            justCopied = false;
        }

        if (ScrollView.Buttons.Any()) {
            if (ScrollView.SelectedIndex >= 0) {
                if (IsHeaderButton((SimplePhoneButton)ScrollView.Buttons[ScrollView.SelectedIndex])) {
                    m_AudioManager.audioSources[3].Stop();
                    int indexOfLastButton = ScrollView.Buttons.IndexOf(lastSelectedButton);
                    if (indexOfLastButton < ScrollView.SelectedIndex) { // 
                        ScrollView.OnPressUp();
                    } else {
                        ScrollView.OnPressDown();
                    }
                }
                lastSelectedButton = ScrollView.Buttons[ScrollView.SelectedIndex];
            }
        }

        foreach (SimplePhoneButton button in ScrollView.Buttons) {
            if (IsHeaderButton(button)) {
                if (ScrollView.Buttons.IndexOf(button) == 0) { 
                    AppNetRadio.UpdateNowPlayingButton(button, ScrollView); 
                } else { AppNetRadio.UpdateDynamicButtonHeight(button, ScrollView); }
                button.PlayDeselectAnimation(true);
                //Log.LogInfo(button.Height);
            } else if (button.Label.text.EndsWith("nnect...")) {
                bool isStation = GlobalRadio.playing && GlobalRadio.currentStation == currentStationIndex;
                button.Label.text = (isStation ? "Disconnect..." : "Connect..."); 
            } else if (button.Label.text.Contains("Volume:")) {
                button.Label.text = "Volume: " + volumeInPercent + "%"; 
                SetLabelColor(button, changingVolume ? Color.green : Color.clear);
            } else if (button.Label.text.Contains("Metadata:")) {
                button.Label.text = "Metadata: " + metadataModeDisplay[currentMetadataMode]; 
                SetLabelColor(button, changingMetadataMode ? Color.green : Color.clear);
            } else if (button.Label.text.Contains("Metadata offset:")) {
                button.Label.text = "Metadata offset: +" + metadataOffsetInMS.ToString() + "ms"; 
                SetLabelColor(button, changingMetadataOffset ? Color.green : Color.clear);
            } else if (button.Label.text.Contains("clipboard")) {
                button.Label.text = justFailedCopy ? "Copy to clipboard failed!" : (justCopied ? "Copied to clipboard!" : "Copy URL to clipboard");
                SetLabelColor(button, justFailedCopy ? Color.red : (justCopied ? Color.green : Color.clear));
            }
        }

        if (changingVolume) {
            string key = StandardizeURL(Settings.configURLs[currentStationIndex]);
            decimal value = (decimal)(((decimal)volumeInPercent)/((decimal)100.00));
            if (SaveData.stationSettingsByURL.ContainsKey(key)) {
                SaveData.stationSettingsByURL[key].volume = value;
            } else { 
                StationSettings stationSettings = new StationSettings();
                stationSettings.volume = value;
                SaveData.stationSettingsByURL.Add(key, stationSettings); 
            }
        } else if (changingMetadataMode) {
            string key = StandardizeURL(Settings.configURLs[currentStationIndex]);
            int value = currentMetadataMode;
            if (SaveData.stationSettingsByURL.ContainsKey(key)) {
                SaveData.stationSettingsByURL[key].metadataMode = value;
            } else { 
                StationSettings stationSettings = new StationSettings();
                stationSettings.metadataMode = value;
                SaveData.stationSettingsByURL.Add(key, stationSettings); 
            }
        } else if (changingMetadataOffset) {
            string key = StandardizeURL(Settings.configURLs[currentStationIndex]);
            decimal value = (decimal)(((decimal)metadataOffsetInMS) * ((decimal)0.001));
            if (SaveData.stationSettingsByURL.ContainsKey(key)) {
                SaveData.stationSettingsByURL[key].metadataTimeOffsetSeconds = value;
            } else { 
                StationSettings stationSettings = new StationSettings();
                stationSettings.metadataTimeOffsetSeconds = value;
                SaveData.stationSettingsByURL.Add(key, stationSettings); 
            }
        }
    }

    public override void OnAppEnable() {
        lastSelectedButton = null;
        CreateAndSaveTitleBar(GlobalRadio.GetStationTitle(currentStationIndex), AppNetRadio.UnselectedAntennaSprites[2]);//CreateIconlessTitleBar(appName);
        justCopied = false;
        changingAny = false;
        time = 0.0f;

        SetVariables();
        _= GetStationMetadata(); 
        
        base.OnAppEnable();
    }

    public override void OnAppDisable()
    {
        //ScrollView.RemoveAllButtons();
        base.OnAppDisable();
        Destroy(overlayInstance.gameObject);
        justCopied = false;
        changingAny = false;
        time = 0.0f;
        
        SaveData.Instance.Save();
    }

    public override void OnPressUp() { 
        if (changingVolume) {
            volumeInPercent = Mathf.Clamp(volumeInPercent + 5, 0, 200);
        } else if (changingMetadataMode) {
            currentMetadataMode = currentMetadataMode == 1 ? 0 : 1; 
            UpdateGlobalRadioMetaMode();
        } else if (changingMetadataOffset) {
            metadataOffsetInMS = Mathf.Clamp(metadataOffsetInMS + 250, 0, 99000);
        }

        if (!changingAny) { base.OnPressUp(); }
        else { PlaySelectSFX(); }
    } 

    public override void OnPressDown() { 
        if (changingVolume) {
            volumeInPercent = Mathf.Clamp(volumeInPercent - 5, 0, 200);
        } else if (changingMetadataMode) {
            currentMetadataMode = currentMetadataMode == 1 ? 0 : 1; 
            UpdateGlobalRadioMetaMode();
        } else if (changingMetadataOffset) {
            metadataOffsetInMS = Mathf.Clamp(metadataOffsetInMS - 250, 0, 99000);
        }
        
        if (!changingAny) { base.OnPressDown(); }
        else { PlaySelectSFX(); }
    } 

    public override void OnHoldUp() {
        if (changingAny) {return;}
        base.OnHoldUp();
    }

    public override void OnHoldDown() {
        if (changingAny) {return;}
        base.OnHoldDown();
    }

    public void AddUsualButtons() {
        SetVariables();

        ScrollView.RemoveAllButtons();

        var blankButton = AppNetRadio.CreateHeaderButton(AppNetRadio.currentNowPlayingText, AppNetRadio.currentNowPlayingHeight); 
        ScrollView.AddButton(blankButton);
        AppNetRadio.UpdateNowPlayingButton(blankButton, ScrollView, false);

        bool isStation = GlobalRadio.playing && GlobalRadio.currentStation == currentStationIndex;
        var firstButton = PhoneUIUtility.CreateSimpleButton(isStation ? "Disconnect..." : "Connect...");
            firstButton.OnConfirm += () => {
            GlobalRadio.ResetMetadata();
            if (GlobalRadio.playing && GlobalRadio.currentStation == currentStationIndex) {
                GlobalRadio.Stop();
                AppNetRadio.PlaySFX("disconnect");
                AppNetRadio.globalRadioWasInterrupted = false;
                AppNetRadio.ResumePreviousMusic();
                MyPhone.OpenApp(typeof(AppNetRadio));
            } else {
                StartCoroutine(Instance.ConnectToStation());
            }
        };
        ScrollView.AddButton(firstButton);

        var nextButton = PhoneUIUtility.CreateSimpleButton("Volume: ");
        nextButton.OnConfirm += () => {
            changingVolume = !changingVolume;
        };
        ScrollView.AddButton(nextButton);

        nextButton = PhoneUIUtility.CreateSimpleButton("Metadata: ");
        nextButton.OnConfirm += () => {
            changingMetadataMode = !changingMetadataMode;
        };
        ScrollView.AddButton(nextButton);

        nextButton = PhoneUIUtility.CreateSimpleButton("Metadata offset: ");
        nextButton.OnConfirm += () => {
            changingMetadataOffset = !changingMetadataOffset;
        };
        ScrollView.AddButton(nextButton);


        nextButton = PhoneUIUtility.CreateSimpleButton("Copy URL to clipboard");
        nextButton.OnConfirm += () => {
            time = 0.0f;
            try { 
                GUIUtility.systemCopyBuffer = Settings.configURLs[currentStationIndex]; 
                justCopied = true;
            } catch (System.Exception ex) { 
                Log.LogError($"Error copying to clipboard: {ex.Message}"); 
                Log.LogError(ex.StackTrace); 
                justFailedCopy = true;
            }
        };
        ScrollView.AddButton(nextButton);
    }

    public IEnumerator ConnectToStation() {
        int station = currentStationIndex;
        AppNetRadio.loaded = false;
        MyPhone.OpenApp(typeof(AppNetRadio));
        while (!AppNetRadio.loaded) {
            yield return null;
        }
        AppNetRadio.Instance.AttemptConnection(station);
    }

    public async Task GetStationMetadata() {
        string urlForCurrent = NetRadio.StandardizeURL(Settings.configURLs[currentStationIndex]);
        bool cancelTracking = SaveData.stationSettingsByURL.ContainsKey(urlForCurrent) 
                                ? SaveData.stationSettingsByURL[urlForCurrent].metadataMode == 0 : false;
        if (cancelTracking) { return; }

        await Task.Delay(200); 
        IcecastStatus currentInfo = !(GlobalRadio.playing && GlobalRadio.currentStation == currentStationIndex)
                                    ? await GlobalRadio.GetMetadata(GlobalRadio.streamURLs[currentStationIndex])
                                    : GlobalRadio.currentMetadata;
        Metadata.Source stationInfo = GetSource(currentInfo, urlForCurrent); 


        //ScrollView.RemoveAllButtons();
        //AddUsualButtons();
        foreach (SimplePhoneButton button in ScrollView.Buttons) { if (IsHeaderButton(button)) {
            if (button.Label.text.Contains("Peak listeners:")) {
                button.Label.text = "Peak listeners: " + stationInfo.listener_peak;
            } else if (button.Label.text.Contains("Listeners:")) {
                button.Label.text = "Listeners: " + stationInfo.listeners;
            } else if (button.Label.text.Contains("Genre:")) {
                button.Label.text = "Genre: " + stationInfo.genre;
            }
        } }
        /*var nextButton = AppNetRadio.CreateHeaderButton("Listeners: " + stationInfo.listeners, 75f);
        ScrollView.AddButton(nextButton);
        nextButton = AppNetRadio.CreateHeaderButton("Peak listeners: " + stationInfo.listener_peak, 75f);
        ScrollView.AddButton(nextButton);
        nextButton = AppNetRadio.CreateHeaderButton("Genre: " + stationInfo.genre, 75f);
        ScrollView.AddButton(nextButton);
        nextButton = AppNetRadio.CreateHeaderButton("Streaming since " + stationInfo.stream_start);
        ScrollView.AddButton(nextButton);*/
    }

    public void SetLabelColor(SimplePhoneButton nextButton, Color color) {
        if (color == Color.clear) { // treat as default
            nextButton.Label.faceColor = ScrollView.SelectedIndex == ScrollView.Buttons.IndexOf(nextButton) ? LabelSelectedColorDefault : LabelUnselectedColorDefault;
            nextButton.LabelSelectedColor = LabelSelectedColorDefault;
            nextButton.LabelUnselectedColor = LabelUnselectedColorDefault;
        } else {
            nextButton.Label.faceColor = color;
            nextButton.LabelSelectedColor = color;
            nextButton.LabelUnselectedColor = color;
        }
    }

    private void UpdateGlobalRadioMetaMode() {
        bool isStation = GlobalRadio.playing && GlobalRadio.currentStation == currentStationIndex;
        if (!isStation) { return; }
        GlobalRadio.ResetMetadata();
        if (currentMetadataMode == 0) { GlobalRadio.StopTrackingMetadata(false); }
        else if (!GlobalRadio.trackingMetadata) { 
            GlobalRadio.StartTrackingMetadata(true); 
        }
    }
}