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

public class AppNetRadio : NetRadioCustomApp
{
    private static Sprite IconSprite = null;
    public const string appName = PluginName;
    public static AppNetRadio Instance;

    private static bool justCleared = false;
    public static bool playing = false;
    public static bool runPrefix = true;
    
    private static VolumeSource tuningSource;
    private static FfmpegDecoder lastFfmpeg; 
    public static WaveOut waveOut;

    public static Sprite SelectedButtonSprite;
    public static Sprite UnselectedButtonSprite;
    public static List<Sprite> SelectedAntennaSprites = new List<Sprite>{}; // index = number of signal lines
    public static List<Sprite> UnselectedAntennaSprites = new List<Sprite>{}; // when not playing, stay on 0; when playing, bounce from 1-3
    public static Dictionary<string, List<Sprite>> StationIcons = new Dictionary<string, List<Sprite>>();
    public static List<Sprite> ConnectingSprites = new List<Sprite>{}; // index = number of signal lines, index 3 = disconnect
    public static Image connectIcon;
    //public static Sprite DiscSprite;f
    //public static Sprite DiscSpinSprite;
    public static Sprite BlankButtonSprite; 

    public static float time = -1f;
    public static float realTime = 0f;
    public static List<int> urlWrapOffsets = new List<int>{};
    public static List<string> originalURLLabelText = new List<string>{};
    public static List<bool> urlIsTooLong = new List<bool>{};
    //private static List<TextMeshProUGUI> urlLabels;

    public static string dataDirectory = Path.Combine(NetRadioPlugin.Instance.Directory, "RadioApp-res/");

    public const float urlOffsetX = 680f;
    public const float urlOffsetY = -80f;
    public const float titleOffsetX = 230f;
    public const float titleOffsetY = 50f;
    public const float arrowOffsetX = 40f;
    public const float arrowOffsetY = 80f;
    public const float discOffsetX = -260f;
    public const float discOffsetY = 0f;
    public const float stationOffsetX = 525f;
    public const float stationOffsetY = 27.5f;
    public const float stationLogoSize = 220f;
    public const float stationLogoOpacity = 0.5f;
    public const float iconSize = 400f;
    public const float iconOffsetX = 250f;
    public const float iconOffsetY = 170f;

    //public static int normalButtonIndexOffset = 0;
    public static List<SimplePhoneButton> filteredButtons = new List<SimplePhoneButton>{}; // only station buttons
    
    public static bool musicPlayerWasInterrupted = false;
    public static bool globalRadioWasInterrupted = false;
    public static int musicPlayerInterruptSamples = 0;

    public static PhoneButton lastSelectedButton;
    public static bool loaded = false;

    public static string currentNowPlayingText = "Now playing";
    public static float currentNowPlayingHeight = 100f;

    public static string currentSFXPack { get { return Settings.sfxPack.Value.ToLower(); } }

    public static void Initialize() { 
        IconSprite = LoadSprite(Path.Combine(dataDirectory, "icon.png")); 
        PhoneAPI.RegisterApp<AppNetRadio>(appName, IconSprite); 

        waveOut = new WaveOut(NetRadio.waveOutLatency);

        SelectedButtonSprite = LoadSprite(Path.Combine(dataDirectory, "SimpleButton-Selected.png"));
        UnselectedButtonSprite = LoadSprite(Path.Combine(dataDirectory, "SimpleButton.png"));
        BlankButtonSprite = LoadSprite(Path.Combine(dataDirectory, "BlankButton.png"));

        string antennaPath = dataDirectory + "Antenna-";
        foreach (string status in new string[] {"Selected", "Unselected"}) {
            int i = 0;
            while (i <= 3) {
                string path = antennaPath + status + i.ToString() + ".png";
                Sprite sprite = LoadSprite(path); 
                if (status == "Selected") { SelectedAntennaSprites.Add(sprite); }
                else { UnselectedAntennaSprites.Add(sprite); }
                i++;
            }
            if (status == "Selected") { SelectedAntennaSprites.Add(LoadSprite(antennaPath + status + "2.png")); }
            else { UnselectedAntennaSprites.Add(LoadSprite(antennaPath + status + "2.png")); }
        }

        int e = 0;
        while (e <= 3) {
            string path = dataDirectory + "Connecting" + (e + 1).ToString() + ".png";
            ConnectingSprites.Add(LoadSprite(path));
            e++;
        }

        string stationIconsPath = dataDirectory + "StationLogos/";
        List<string> filesInPath = Directory.GetFiles(stationIconsPath).ToList();
        foreach (string file in filesInPath) {
            if (!file.Contains("-Selected")) { 
                string stationName = Path.GetFileNameWithoutExtension(file);
                Sprite normal = LoadSprite(stationIconsPath + stationName + ".png");
                Sprite selected = LoadSprite(stationIconsPath + stationName + "-Selected.png");
                List<Sprite> sprites = new List<Sprite> {normal, selected};
                StationIcons.Add(stationName, sprites);
            }
        }
    }

    public override void OnReleaseLeft() { MyPhone.ReturnToHome(); } // ignore any previous app history

    public override void OnAppInit()
    {
        Instance = this;
        loaded = false;
        base.OnAppInit();
        CreateTitleBar(appName, IconSprite); //CreateIconlessTitleBar(appName);
        ScrollView = PhoneScrollView.Create(this);

        AddURLButtons();
    }   

    public override void OnAppEnable()
    {
        //Settings.LoadURLs(); 
        Instance = this;
        //this.ScrollView.Separation = 80f; 

        if (overlayInstance != null) {
            Destroy(overlayInstance.gameObject);
            overlayInstance = null; 
        }

        if (!GlobalRadio.threadRunning) {  AddURLButtons(); }
        else { StartCoroutine(Instance.ClearButtons()); }

        if (connectIcon != null) { Destroy(connectIcon.gameObject); }
        var connectGameObject = new GameObject("Connecting Icon");
        connectIcon = connectGameObject.AddComponent<Image>();
        connectIcon.sprite = ConnectingSprites[0];
        
        connectIcon.transform.SetParent(ScrollView.gameObject.transform.parent, false);
        connectIcon.RectTransform().sizeDelta = new Vector2(iconSize, iconSize);
        connectIcon.RectTransform().localPosition += new Vector3(iconOffsetX, iconOffsetY, 0f);
        connectIcon.RectTransform().localPosition -= new Vector3(0.5f*iconSize, 0.5f*iconSize, 0); // centered
        connectIcon.enabled = false;

        base.OnAppEnable();
        loaded = true;
    }

    public override void OnAppUpdate() {
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
    }

    public override void OnAppLateUpdate() {
        if (connectIcon != null) { connectIcon.enabled = !ScrollView.Buttons.Any(); }
        if (!ScrollView.Buttons.Any()) {
            //realTime += Time.deltaTime;
            if (!GlobalRadio.threadRunning && !justCleared && !GlobalRadio.failedToLoad) {
                StartCoroutine(Instance.RestoreButtons()); 
                justCleared = true;
            }

            if (connectIcon != null) {
                float frameFloat = (realTime * 5f) % 3f;
                if (connectIcon.sprite != ConnectingSprites[3]) {
                    connectIcon.sprite = ConnectingSprites[(int)frameFloat];
                }
            }
        }
        
        if (tuningSource != null) { tuningSource.Volume = radioMusicVolume; }
        realTime += Time.deltaTime;

        foreach (SimplePhoneButton button in ScrollView.Buttons) {
            if (!IsStationButton(button)) { 
                filteredButtons.Remove(button);
                if (IsHeaderButton(button)) {
                    if (ScrollView.Buttons.IndexOf(button) == 0) {
                        AppNetRadio.UpdateNowPlayingButton(button, ScrollView);
                    }
                    button.PlayDeselectAnimation(true);
                    continue;
                }

                if (ScrollView.Buttons.IndexOf(button) == ScrollView.SelectedIndex) {
                    time += Time.deltaTime;
                } if (button.ButtonImage.sprite.texture.wrapMode != TextureWrapMode.Clamp) { 
                    button.ButtonImage.sprite.texture.wrapMode = TextureWrapMode.Clamp; 
                } 
                
                continue;
            }

            int buttonIndex = filteredButtons.IndexOf(button);
            if (button == null || buttonIndex < 0 || buttonIndex >= filteredButtons.Count) {
                continue;
            }

            bool selected = ScrollView.Buttons.IndexOf(button) == ScrollView.SelectedIndex;
            TextMeshProUGUI urlLabel = button.Label.gameObject.transform.GetChild(0).gameObject.GetComponent<TextMeshProUGUI>();
            if (urlLabel == null) { continue; }

            if (buttonIndex >= urlWrapOffsets.Count) { 
                urlWrapOffsets.Add(0); 
            }

            if (selected && urlIsTooLong[buttonIndex]) {
                time += Time.deltaTime;
                if (time > 0.3f) {
                    time = 0f;
                    urlWrapOffsets[buttonIndex] += urlLabel.text.Substring(0,1) == " " ? 2 : 1;
                }
            } else {
                urlWrapOffsets[buttonIndex] = 0;
            }

            if (buttonIndex >= originalURLLabelText.Count) { originalURLLabelText.Add(urlLabel.text); }
            string originalLabelText = originalURLLabelText[buttonIndex];
            if (urlLabel.text == originalLabelText && urlWrapOffsets[buttonIndex] > 3) { 
                urlWrapOffsets[buttonIndex] = 0; 
                time = -1f;
            }
            urlLabel.text = originalLabelText.Remove(0, urlWrapOffsets[buttonIndex]) + originalLabelText.Substring(0, urlWrapOffsets[buttonIndex]);

            bool tooLong = urlLabel.fontSize <= urlLabel.fontSizeMin;
            if (urlLabel.text == originalLabelText) {
                urlIsTooLong[buttonIndex] = tooLong;
            }

            Image disc = button.Label.gameObject.transform.GetChild(1).gameObject.GetComponent<Image>();
            if (disc == null || !disc.gameObject.name.Contains("Icon")) { continue; }

            bool playingMyTrack = buttonIndex == GlobalRadio.currentStation && GlobalRadio.playing;
            List<Sprite> spriteArray = selected ? SelectedAntennaSprites : UnselectedAntennaSprites; 
            float antennaFrameFloat = (realTime * 5f) % 4f;
            int antennaFrame = !playingMyTrack ? 0 : Mathf.Clamp((int)antennaFrameFloat + 1, 0, spriteArray.Count - 1);
            disc.sprite = spriteArray[antennaFrame];
        } 

        base.OnAppLateUpdate();
    }

    public override void OnAppDisable()
    {
        loaded = false;
        if (connectIcon != null) {
            Destroy(connectIcon.gameObject);
            connectIcon = null; 
        }
        //ScrollView.RemoveAllButtons();
        base.OnAppDisable();
    }

    private void AddURLButtons() {
        if (ScrollView.Buttons.Count > 0) { return; }
        lastSelectedButton = null;
        realTime = 0f;
        filteredButtons.Clear();

        var blankButton = CreateHeaderButton(AppNetRadio.currentNowPlayingText, AppNetRadio.currentNowPlayingHeight); 
        ScrollView.AddButton(blankButton);
        AppNetRadio.UpdateNowPlayingButton(blankButton, ScrollView, false);

        int i = -1;
        int numberOfCustomStreams = 0;
        foreach (string url in Settings.configURLs) {//GlobalRadio.streamURLs) {
            i++;
            string stationTitle = GlobalRadio.GetStationTitle(i);
            if (stationTitle == PluginName) { 
                numberOfCustomStreams++;
                stationTitle = "Custom Station " + numberOfCustomStreams;
            }
            var nextButton = CreateStationButton(stationTitle, url);
            nextButton.OnConfirm += () => {
                if (GlobalRadio.threadRunning) { return; }

                if ((GlobalRadio.playing && filteredButtons.IndexOf(nextButton) == GlobalRadio.currentStation) || !Settings.configureRequireConnection.Value) {
                    AppSelectedStation.currentStationIndex = filteredButtons.IndexOf(nextButton);
                    MyPhone.OpenApp(typeof(AppSelectedStation));
                    return;
                }
                    
                AttemptConnection(filteredButtons.IndexOf(nextButton));
            };
            ScrollView.AddButton(nextButton);
            filteredButtons.Add(nextButton);

            if (i >= urlIsTooLong.Count) { urlIsTooLong.Add(false); }
            else { urlIsTooLong[i] = false; }
        }

        if (connectIcon != null) { 
            connectIcon.sprite = ConnectingSprites[0];
            connectIcon.enabled = false; 
        }
    }

    private IEnumerator ClearButtons() {
        lastSelectedButton = null;
        realTime = 0f;
        justCleared = true;
        //urlLabels.Clear();
        ScrollView.RemoveAllButtons();
        CreateAndSaveIconlessTitleBar("Connecting...");
        yield return new WaitForSeconds(0.5f);
        justCleared = false;
    }

    public void ReloadButtons() {
        if (!ScrollView.Buttons.Any()) { return; }
        ScrollView.RemoveAllButtons();
        AddURLButtons();
    }

    private IEnumerator RestoreButtons() {
        yield return new WaitForSeconds(0.5f);
        AddURLButtons();
        if (overlayInstance != null) {
            Destroy(overlayInstance.gameObject);
            overlayInstance = null; 
        }
        justCleared = false;
        waveOut.Stop(); playing = false; 
    }

    public void AttemptConnection(int index) {
        musicPlayerWasInterrupted = musicPlayer.IsPlaying;
        if (musicPlayerWasInterrupted) { musicPlayerInterruptSamples = musicPlayer.CurrentTrackSamples; }
        globalRadioWasInterrupted = GlobalRadio.playing && !GlobalRadio.failedToLoad;

        musicPlayer.ForcePaused();
        GlobalRadio.Play(index); 
    
        PlayNoise();
        StartCoroutine(Instance.ClearButtons()); 
    }

    public IEnumerator StopIn(float sec) {
        playing = false;
        if (sec > 0f) { yield return new WaitForSeconds(sec); }
        waveOut.Stop();
    }

    public IEnumerator HandleFailedConnection() {
        playing = false;
        waveOut.Stop();
        PlaySFX("fail");

        if (overlayInstance != null) {
            Destroy(overlayInstance.gameObject);
            overlayInstance = null; 
        }
        
        CreateAndSaveIconlessTitleBar("Connection failed!");
        if (connectIcon != null) { connectIcon.sprite = ConnectingSprites[3]; }

        // make these optional via config
        ResumePreviousMusic(true);

        yield return new WaitForSeconds(1f);
        GlobalRadio.failedToLoad = false;
    }

    public static void ResumePreviousMusic(bool includeSamples = false) {
        if (!Settings.restartMusic.Value) { return; }
        int sampleValue = includeSamples ? musicPlayerInterruptSamples : 0; 
        if (musicPlayerWasInterrupted) {
            musicPlayerWasInterrupted = false;
            musicPlayer.PlayFrom(musicPlayer.CurrentTrackIndex, sampleValue);
        } else if (globalRadioWasInterrupted) {
            GlobalRadio.Play(GlobalRadio.previousStation); 
            globalRadioWasInterrupted = false;
            //yield break;
        } else {
            musicPlayer.PlayFrom(0, 0);
        }
    }

    public static void PlayNoise() { PlaySFX("tuning", true); playing = true; }
    public static void PlaySFX(string sfxName, bool startedByPlayNoise = false) {
        PlaySFXFromPack(sfxName, currentSFXPack, startedByPlayNoise);
    }

    public static void PlaySFXFromPack(string sfxName, string sfxPack = "default", bool startedByPlayNoise = false) {
        sfxPack = sfxPack.ToLower(); 
        sfxName = sfxName.ToLower(); 
        waveOut.Stop();
        if (sfxName == "tuning" && !startedByPlayNoise) { PlayNoise(); return; }
        try {
            if (!HasSFX(sfxName, sfxPack, out string[] files)) { throw new ArgumentException("Sound effect does not exist"); }
            if (lastFfmpeg != null) { lastFfmpeg.Dispose(); }
            string filePath = files[0];

            FfmpegDecoder sfxDecoder = new FfmpegDecoder(filePath);
            lastFfmpeg = sfxDecoder;
            sfxDecoder.Position = 0;

            VolumeSource sfxReader = new VolumeSource(WaveToSampleBase.CreateConverter(sfxDecoder)); 
            if (sfxName == "tuning") { tuningSource = sfxReader; }
            sfxReader.Volume = radioMusicVolume;
            waveOut.Initialize(new SampleToIeeeFloat32(sfxReader));
            waveOut.Play();
        } catch (System.Exception ex) {
            if (sfxPack != "default") { 
                Log.LogWarning("Error playing app SFX (" + sfxName + " from pack " + sfxPack + "): " + ex.Message);
                PlaySFXFromPack(sfxName, "default", startedByPlayNoise); 
            } else {
                Log.LogError("Error playing app SFX (" + sfxName + " from pack " + sfxPack + "): " + ex.Message);
                Log.LogError(ex.StackTrace); 
            }
        }
    }

    public static bool HasSFX(string sfxName, string sfxPack, out string[] files) {
        files = Directory.EnumerateFiles(Path.Combine(dataDirectory, "sfx/", (sfxPack + "/")), sfxName + ".*").ToArray();
        return files.Any(); 
    }

    public static bool HasSFX(string sfxName, string sfxPack = "default") {
        return HasSFX(sfxName, sfxPack, out _);
    }

    public static SimplePhoneButton CreateSimpleButton(string label) {
        return PhoneUIUtility.CreateSimpleButton(label); 
    }

    public static SimplePhoneButton CreateHeaderButton(string label, float height = 100f) {
        runPrefix = false;
        var button = PhoneUIUtility.CreateSimpleButton(label);
        var titleLabel = button.Label;
        var headerSig = new GameObject("Header");
        headerSig.transform.SetParent(titleLabel.gameObject.transform, false);
        button.ButtonImage.gameObject.RectTransform().sizeDelta = new Vector2(530f * 2f, height);
        titleLabel.transform.localPosition += new Vector3 (-70f, 0f, 0f);
        runPrefix = true;
        return button;
    }

    public static void UpdateNowPlayingButton(SimplePhoneButton button, PhoneScrollView scrollView, bool skipCheck = false) {
        string previousText = button.Label.text;
        bool playingAnything = GlobalRadio.playing || musicPlayer.IsPlaying;
        if (!playingAnything) { 
            button.Label.text = "";
        } else {
            string nowPlaying = GlobalRadio.playing ? GlobalRadio.currentSong 
                : (musicPlayer.musicTrackQueue.CurrentMusicTrack.Artist + " - " + musicPlayer.musicTrackQueue.CurrentMusicTrack.Title);
            button.Label.text = "Now playing " + nowPlaying;
            button.Label.alignment = TextAlignmentOptions.Center;
        }

        currentNowPlayingText = button.Label.text;
        UpdateDynamicButtonHeight(button, scrollView, skipCheck);
        if (button.ButtonImage.gameObject.RectTransform().sizeDelta.y > 0) { currentNowPlayingHeight = button.Height; }
    }

    public static void UpdateDynamicButtonHeight(SimplePhoneButton button, PhoneScrollView scrollView, bool skipCheck = false) {
        var rTransform = button.ButtonImage.gameObject.RectTransform();
        float previousHeight = rTransform.sizeDelta.y;
        float width = rTransform.sizeDelta.x;
        float height = button.Label.fontSizeMin*button.Label.textInfo.lineCount;
        if (height == 0) { height -= scrollView.Separation; }
        rTransform.sizeDelta = new Vector2(width, height);
        
        if (height != previousHeight && !skipCheck) { 
            scrollView.UpdateButtons(); 
        }
    }

    private static SimplePhoneButton CreateStationButton(string label, string url) {
        runPrefix = false;
        var button = PhoneUIUtility.CreateSimpleButton(label); 
        var buttonAnimationParent = button.gameObject.transform.Find("Animation Parent");

        url = StandardizeURL(url);
        url += "        ";

        var labelGO = new GameObject("URLLabel");
        var tmp = labelGO.AddComponent<TextMeshProUGUI>();
        tmp.text = url;
        tmp.alignment = TextAlignmentOptions.Left;
        tmp.font = AppUtility.GetAppFont();
        tmp.fontSize = 55f;
        tmp.fontSizeMax = 55f;
        tmp.fontSizeMin = 50f;
        tmp.enableAutoSizing = true;
        tmp.enableWordWrapping = false;
        //tmp.maxVisibleLines = 2;

        var titleLabel = button.Label;
        labelGO.transform.SetParent(titleLabel.gameObject.transform, false);
        labelGO.transform.localPosition = new Vector3(urlOffsetX, urlOffsetY, 0f);
        var labelRect = labelGO.RectTransform();
        labelRect.SetAnchorAndPivot(0f, 0.5f);
        labelRect.sizeDelta = new Vector2(850f, 100f);
        
        Vector3 titleOffset = new Vector3(titleOffsetX, titleOffsetY, 0f);
        titleLabel.transform.localPosition += titleOffset;
        labelGO.transform.localPosition -= titleOffset;
        labelRect.sizeDelta -= new Vector2(0.3f*(urlOffsetX - titleOffsetX), 0);
        titleLabel.RectTransform().sizeDelta -= new Vector2(titleOffsetX - 50f, 0);
        // crappy way of hiding arrow //new Vector3(arrowOffsetX, arrowOffsetY, 0f);
        button.ConfirmArrow.RectTransform().localPosition += new Vector3(arrowOffsetX, -9999f, 0f);

        // disc = antenna
        var disc = new GameObject("Station Icon"); 
        var discImage = disc.AddComponent<Image>();
        disc.transform.SetParent(titleLabel.gameObject.transform, false);
        disc.RectTransform().localPosition -= titleOffset;
        discImage.sprite = UnselectedAntennaSprites[0]; //DiscSprite;
        disc.RectTransform().sizeDelta = new Vector2(280f, 280f);
        disc.RectTransform().localPosition += new Vector3(discOffsetX, discOffsetY, 0f);

        var logo = new GameObject("Station Logo");
        var logoImage = logo.AddComponent<Image>();
        logo.transform.SetParent(titleLabel.gameObject.transform, false); 
        logo.RectTransform().localPosition -= titleOffset;
        logoImage.sprite = GetStationLogo(label, false);
        logo.RectTransform().sizeDelta = new Vector2(stationLogoSize, stationLogoSize);
        logo.RectTransform().localPosition += new Vector3(stationOffsetX, stationOffsetY, 0f);

        Color c = logoImage.color;
        c.a = stationLogoOpacity;
        logoImage.color = c;

        runPrefix = true;
        return button;
    }

    public static Sprite GetStationLogo(string requestedStation, bool selected) {
        int index = selected ? 1 : 0;
        string station = StationIcons.ContainsKey(requestedStation) ? requestedStation : "Custom";
        return StationIcons[station][index];
    }
}