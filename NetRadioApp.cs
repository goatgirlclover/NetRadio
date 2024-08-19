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

namespace NetRadio
{
    public class TracksHeaderApp : CustomApp {
        public UnityEngine.Transform overlayInstance = null; 
        // Allow us to save overlay so we can delete it later
        // useful for making headers with playlist/track names
        // code nearly entirely copy-pasted from CommonAPI github. thanks lazy duchess!
        public void CreateAndSaveIconlessTitleBar(string title, float fontSize = 80f) {
            var newOverlay = GameObject.Instantiate(MyPhone.GetAppInstance<Reptile.Phone.AppGraffiti>().transform.Find("Overlay"));
            var icons = newOverlay.transform.Find("Icons");
            Destroy(icons.Find("GraffitiIcon").gameObject);
            var header = icons.Find("HeaderLabel");
            header.localPosition = new Vector3(140f, header.localPosition.y, header.localPosition.z);
            Component.Destroy(header.GetComponent<TMProLocalizationAddOn>());
            var tmpro = header.GetComponent<TMPro.TextMeshProUGUI>(); 
            tmpro.text = title; // overlayInstance.transform.Find("Icons").Find("HeaderLabel").GetComponent<TMPro.TextMeshProUGUI>().text = title;
            tmpro.fontSize = fontSize;
            tmpro.fontSizeMax = fontSize;
            //tmpro.fontSizeMin = fontSize;
            tmpro.enableAutoSizing = true;
            newOverlay.SetParent(transform, false);

            overlayInstance = newOverlay;
        }

        public override void OnPressUp() { // if selected the 2nd one and the top one is a blank header, don't bother
            int currentIndex = ScrollView.SelectedIndex;
            int nextIndex = currentIndex - 1;
            int edgeValue = 0;

            if (!(nextIndex < 0 || nextIndex >= ScrollView.Buttons.Count)) { 
                if (IsHeaderButton((SimplePhoneButton)ScrollView.Buttons[nextIndex]) && nextIndex == edgeValue) {
                    return;
                }
            }
            
            base.OnPressUp();
        } 

        public override void OnPressDown() { // if selected the 2nd-to-last one and the bottom one is blank, don't bother
            int currentIndex = ScrollView.SelectedIndex;
            int nextIndex = currentIndex + 1;
            int edgeValue = ScrollView.Buttons.Count - 1;
            
            if (!(nextIndex < 0 || nextIndex >= ScrollView.Buttons.Count)) { 
                if (IsHeaderButton((SimplePhoneButton)ScrollView.Buttons[nextIndex]) && nextIndex == edgeValue) {
                    return;
                }
            }
            
            base.OnPressDown();
        } 
    }

    public class AppSelectedStation : TracksHeaderApp {
        public static AppSelectedStation Instance;
        public static PhoneButton lastSelectedButton;
        public static int currentStationIndex;

        public static float time = 0.0f;
        public static bool justCopied = false;
        public static bool justFailedCopy = false;
        
        public static int volumeInPercent = 100;
        public static bool changingVolume = false;

        public override bool Available => false;

        public static void Initialize() { 
            PhoneAPI.RegisterApp<AppSelectedStation>("selected station"); 
        }

        public override void OnAppEnable()
        {
            Instance = this;
            base.OnAppEnable();
            lastSelectedButton = null;
            CreateAndSaveIconlessTitleBar(GlobalRadio.GetStationTitle(currentStationIndex));//CreateIconlessTitleBar(appName);
            justCopied = false;
            changingVolume = false;
            time = 0.0f;

            string urlForCurrent = NetRadio.StandardizeURL(NetRadioSettings.configURLs[currentStationIndex]);
            float volumeMultiplier = NetRadioSaveData.stationVolumesByURL.ContainsKey(urlForCurrent) ? (float)NetRadioSaveData.stationVolumesByURL[urlForCurrent] : 1f;
            float volumeHund = volumeMultiplier * 100f;
            volumeInPercent = 5 * (int)Math.Round(volumeHund / 5.0);

            var blankButton = AppNetRadio.CreateHeaderButton("Now playing"); 
            ScrollView.AddButton(blankButton);

            if (GlobalRadio.playing && GlobalRadio.currentStation == currentStationIndex) {
                var firstButton = PhoneUIUtility.CreateSimpleButton("Disconnect...");
                firstButton.OnConfirm += () => {
                    GlobalRadio.Stop();
                    musicPlayer.PlayFrom(musicPlayer.CurrentTrackIndex, 0);
                    MyPhone.OpenApp(typeof(AppNetRadio));
                };
                ScrollView.AddButton(firstButton);
            } else {
                var firstButton = PhoneUIUtility.CreateSimpleButton("Connect...");
                firstButton.OnConfirm += () => {
                    StartCoroutine(Instance.ConnectToStation());
                };
                ScrollView.AddButton(firstButton);
            }

            var nextButton = PhoneUIUtility.CreateSimpleButton("Volume: ");
            nextButton.OnConfirm += () => {
                changingVolume = !changingVolume;
            };
            ScrollView.AddButton(nextButton);

            nextButton = PhoneUIUtility.CreateSimpleButton("Copy URL to clipboard");
            nextButton.OnConfirm += () => {
                time = 0.0f;
                try { 
                    GUIUtility.systemCopyBuffer = NetRadioSettings.configURLs[currentStationIndex]; 
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
                        if (indexOfLastButton > ScrollView.SelectedIndex) { // 
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
                    if (button.Label.text.Contains("Now playing")) {
                        string musicPlayerTrack = musicPlayer.musicTrackQueue.CurrentMusicTrack.Artist + " - " + musicPlayer.musicTrackQueue.CurrentMusicTrack.Title;
                        string nowPlaying = GlobalRadio.playing ? GlobalRadio.currentSong : musicPlayerTrack;
                        button.Label.text = "Now playing " + nowPlaying;
                        button.Label.alignment = TextAlignmentOptions.Center;
                        button.ButtonImage.gameObject.RectTransform().sizeDelta = new Vector2(530f * 2f, button.Label.fontSize*button.Label.textInfo.lineCount - 10f);
                    }
                    button.PlayDeselectAnimation(true);
                } else if (button.Label.text.Contains("Volume:")) {
                    button.Label.text = "Volume: " + volumeInPercent + "%"; 
                    SetLabelColor(button, changingVolume ? Color.green : Color.clear);
                } else if (button.Label.text.Contains("clipboard")) {
                    button.Label.text = justFailedCopy ? "Copy to clipboard failed!" : (justCopied ? "Copied to clipboard!" : "Copy URL to clipboard");
                    SetLabelColor(button, justFailedCopy ? Color.red : (justCopied ? Color.green : Color.clear));
                }
            }

            if (changingVolume) {
                string key = StandardizeURL(NetRadioSettings.configURLs[currentStationIndex]);
                decimal value = (decimal)(((decimal)volumeInPercent)/((decimal)100.00));
                if (NetRadioSaveData.stationVolumesByURL.ContainsKey(key)) {
                    NetRadioSaveData.stationVolumesByURL[key] = value;
                } else { NetRadioSaveData.stationVolumesByURL.Add(key, value); }
            }
        }

        public override void OnAppInit()
        {
            base.OnAppInit();
            ScrollView = PhoneScrollView.Create(this);
        }   

        public override void OnAppDisable()
        {
            ScrollView.RemoveAllButtons();
            base.OnAppDisable();
            Destroy(overlayInstance.gameObject);
            justCopied = false;
            changingVolume = false;
            time = 0.0f;
            
            NetRadioSaveData.Instance.Save();
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

        public override void OnPressUp() { // if selected the 2nd one and the top one is a blank header, don't bother
            if (changingVolume) {
                volumeInPercent = Mathf.Clamp(volumeInPercent + 5, 0, 200);
                return;
            }
            
            base.OnPressUp();
        } 

        public override void OnPressDown() { // if selected the 2nd-to-last one and the bottom one is blank, don't bother
            if (changingVolume) {
                volumeInPercent = Mathf.Clamp(volumeInPercent - 5, 0, 200);
                return;
            }
            
            base.OnPressDown();
        } 

        public override void OnHoldUp() {
            if (changingVolume) {return;}
            base.OnHoldUp();
        }

        public override void OnHoldDown() {
            if (changingVolume) {return;}
            base.OnHoldDown();
        }
    }

    public class AppNetRadio : TracksHeaderApp
    {
        private static Sprite IconSprite = null;
        public const string appName = PluginName;
        public static AppNetRadio Instance;

        private static bool justCleared = false;
        public static bool playing = false;
        public static bool runPrefix = true;
        
        private static FfmpegDecoder ffmpegReader;
        public static WaveOut waveOut;
        private static VolumeSource volumeSource; 

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

        public static void Initialize() { 
            IconSprite = LoadSprite(Path.Combine(dataDirectory, "icon.png")); 
            PhoneAPI.RegisterApp<AppNetRadio>(appName, IconSprite); 

            ffmpegReader = new FfmpegDecoder(Path.Combine(dataDirectory, "Tuning.mp3"));
            volumeSource = new VolumeSource(WaveToSampleBase.CreateConverter(ffmpegReader));
            waveOut = new WaveOut(NetRadio.waveOutLatency);
            waveOut.Initialize(new SampleToIeeeFloat32(volumeSource));

            SelectedButtonSprite = LoadSprite(Path.Combine(dataDirectory, "SimpleButton-Selected.png"));
            UnselectedButtonSprite = LoadSprite(Path.Combine(dataDirectory, "SimpleButton.png"));
            //DiscSprite = LoadSprite(Path.Combine(dataDirectory, "StationDisc.png"));
            //DiscSpinSprite = LoadSprite(Path.Combine(dataDirectory, "StationDisc-Spinning.png"));
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

        public static Sprite GetStationLogo(string requestedStation, bool selected) {
            int index = selected ? 1 : 0;
            string station = StationIcons.ContainsKey(requestedStation) ? requestedStation : "Custom";
            return StationIcons[station][index];
        }

        /* public static List<Sprite> GetStationLogos(string requestedStation) {
            string station = StationIcons.ContainsKey(requestedStation) ? requestedStation : "Custom";
            return StationIcons[station];
        } */

        public override void OnReleaseLeft() { MyPhone.ReturnToHome(); } // ignore any previous app history

        public override void OnAppEnable()
        {
            //NetRadioSettings.LoadURLs(); 
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

        private void AddURLButtons() {
            lastSelectedButton = null;
            realTime = 0f;
            //normalButtonIndexOffset = 0;
            filteredButtons.Clear();
            runPrefix = false;
            // add non-station buttons
            /* var normalButton = CreateSimpleButton("TEST");
            normalButton.OnConfirm += () => {};
            ScrollView.AddButton(normalButton); */
            runPrefix = true;

            var blankButton = CreateHeaderButton("Now playing"); 
            ScrollView.AddButton(blankButton);

            int i = -1;
            int numberOfCustomStreams = 0;
            foreach (string url in NetRadioSettings.configURLs) {//GlobalRadio.streamURLs) {
                i++;
                string stationTitle = GlobalRadio.GetStationTitle(i);
                if (stationTitle == PluginName) { 
                    numberOfCustomStreams++;
                    stationTitle = "Custom Station " + numberOfCustomStreams;
                }
                var nextButton = CreateStationButton(stationTitle, url);
                nextButton.OnConfirm += () => {
                    if (GlobalRadio.threadRunning) { return; }

                    if (filteredButtons.IndexOf(nextButton) == GlobalRadio.currentStation || !NetRadioSettings.configureRequireConnection.Value) {
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

        public void AttemptConnection(int index) {
            musicPlayerWasInterrupted = musicPlayer.IsPlaying;
            if (musicPlayerWasInterrupted) { musicPlayerInterruptSamples = musicPlayer.CurrentTrackSamples; }
            globalRadioWasInterrupted = GlobalRadio.playing && !GlobalRadio.failedToLoad;

            musicPlayer.ForcePaused();
            GlobalRadio.Play(index); //NetRadioPlugin.GlobalRadio.streamURLs.IndexOf(urlLabels[i].text));//(nextButton.Label.text));
            
            ffmpegReader.Position = 0;
            playing = true;
            waveOut.Play();
            StartCoroutine(Instance.ClearButtons()); 
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

        public IEnumerator StopIn(float sec) {
            playing = false;
            yield return new WaitForSeconds(sec);
            waveOut.Stop();
        }

        public IEnumerator HandleFailedConnection() {
            playing = false;
            waveOut.Stop();

            if (overlayInstance != null) {
                Destroy(overlayInstance.gameObject);
                overlayInstance = null; 
            }
            
            CreateAndSaveIconlessTitleBar("Connection failed!");
            if (connectIcon != null) { connectIcon.sprite = ConnectingSprites[3]; }

            if (musicPlayerWasInterrupted) {
                musicPlayerWasInterrupted = false;
                musicPlayer.PlayFrom(musicPlayer.CurrentTrackIndex, musicPlayerInterruptSamples);
            } else if (globalRadioWasInterrupted) {
                GlobalRadio.Play(GlobalRadio.previousStation); 
                globalRadioWasInterrupted = false;
                yield break;
            }

            yield return new WaitForSeconds(1f);
            //Destroy(overlayInstance.gameObject);
            //overlayInstance = null;
            GlobalRadio.failedToLoad = false;
        }

        public override void OnAppUpdate() {
            if (ScrollView.Buttons.Any()) {
                if (ScrollView.SelectedIndex >= 0) {
                    if (IsHeaderButton((SimplePhoneButton)ScrollView.Buttons[ScrollView.SelectedIndex])) {
                        m_AudioManager.audioSources[3].Stop();
                        int indexOfLastButton = ScrollView.Buttons.IndexOf(lastSelectedButton);
                        if (indexOfLastButton > ScrollView.SelectedIndex) { // 
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
            
            if (volumeSource != null) { //if (waveOut.PlaybackState == PlaybackState.Playing) {
                volumeSource.Volume = radioMusicVolume;
            } 

            realTime += Time.deltaTime;

            foreach (SimplePhoneButton button in ScrollView.Buttons) {
                if (!IsStationButton(button)) { 
                    filteredButtons.Remove(button);
                    if (IsHeaderButton(button)) {
                        if (button.Label.text.Contains("Now playing")) {
                            string musicPlayerTrack = musicPlayer.musicTrackQueue.CurrentMusicTrack.Artist + " - " + musicPlayer.musicTrackQueue.CurrentMusicTrack.Title;
                            string nowPlaying = GlobalRadio.playing ? GlobalRadio.currentSong : musicPlayerTrack;
                            button.Label.text = "Now playing " + nowPlaying;
                            button.Label.alignment = TextAlignmentOptions.Center;
                            button.ButtonImage.gameObject.RectTransform().sizeDelta = new Vector2(530f * 2f, button.Label.fontSize*button.Label.textInfo.lineCount - 10f);
                        }
                        button.PlayDeselectAnimation(true);
                        //button.AnimationParent.transform.localPosition = new Vector3 (0f, button.gameObject.transform.localPosition.y, 0f); 
                        continue;
                    }

                    if (ScrollView.Buttons.IndexOf(button) == ScrollView.SelectedIndex) {
                        time += Time.deltaTime;
                        //realTime += Time.deltaTime;
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
                //foreach (TextMeshProUGUI child in button.Label.GetComponentsInChildren<TextMeshProUGUI>()) { if (child != button.Label && child.name.Contains("URL")) { urlLabel = child; } }
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
                
                //Image disc = null;
                /* foreach (Image child in button.Label.GetComponentsInChildren<Image>()) { 
                    if (SelectedAntennaSprites.Contains(child.sprite) || UnselectedAntennaSprites.Contains(child.sprite)) { disc = child; } 
                    //else if (GetStationLogos(button.Label.text).Contains(child.sprite)) { stationIcon = child; }
                    //if (child.sprite == DiscSprite || child.sprite == DiscSpinSprite) { disc = child; } 
                } if (disc == null) { continue; } */

                bool playingMyTrack = buttonIndex == GlobalRadio.currentStation && GlobalRadio.playing;
                List<Sprite> spriteArray = selected ? SelectedAntennaSprites : UnselectedAntennaSprites; 
                float antennaFrameFloat = (realTime * 5f) % 4f;
                int antennaFrame = !playingMyTrack ? 0 : Mathf.Clamp((int)antennaFrameFloat + 1, 0, spriteArray.Count - 1);
                disc.sprite = spriteArray[antennaFrame];

                //stationIcon.sprite = GetStationLogo(button.Label.text, selected);

                /*if (playingMyTrack) {
                    disc.rectTransform.localEulerAngles = new Vector3 { z = realTime * 2000f };
                } else {
                    disc.rectTransform.localEulerAngles = new Vector3 { z = 0f }; //{ z = Mathf.LerpAngle(disc.rectTransform.localEulerAngles.z, 0.0f, 0.5f) };
                }
                disc.sprite = playingMyTrack ? DiscSpinSprite : DiscSprite; */
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
            ScrollView.RemoveAllButtons();
            base.OnAppDisable();
        }

        public override void OnAppInit()
        {
            loaded = false;
            base.OnAppInit();
            CreateTitleBar(appName, IconSprite);//CreateIconlessTitleBar(appName);
            ScrollView = PhoneScrollView.Create(this);
        }   

        public static SimplePhoneButton CreateSimpleButton(string label) {
            //normalButtonIndexOffset++; 
            return PhoneUIUtility.CreateSimpleButton(label); 
        }

        public static SimplePhoneButton CreateHeaderButton(string label) {
            runPrefix = false;
            var button = PhoneUIUtility.CreateSimpleButton(label);
            var titleLabel = button.Label;
            var headerSig = new GameObject("Header");
            headerSig.transform.SetParent(titleLabel.gameObject.transform, false);
            //normalButtonIndexOffset++;
            titleLabel.transform.localPosition += new Vector3 (-70f, 0f, 0f);
            button.ButtonImage.gameObject.RectTransform().sizeDelta = new Vector2(530f * 2f, 100f);
            runPrefix = true;
            return button;
        }

        private static SimplePhoneButton CreateStationButton(string label, string url) {
            runPrefix = false;
            var button = PhoneUIUtility.CreateSimpleButton(label); 
            var buttonAnimationParent = button.gameObject.transform.Find("Animation Parent");

            url = StandardizeURL(url); //url.Replace("https://", "").Replace("http://", "").Replace("www.", "").Trim();
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
            button.ConfirmArrow.RectTransform().localPosition += new Vector3(arrowOffsetX, -9999f, 0f); // crappy way of hiding arrow //new Vector3(arrowOffsetX, arrowOffsetY, 0f);

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
    }
}