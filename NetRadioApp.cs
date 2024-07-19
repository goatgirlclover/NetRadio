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
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
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
    }

    public class AppNetRadio : TracksHeaderApp
    {
        private static Sprite IconSprite = null;
        public const string appName = PluginName;
        public static AppNetRadio Instance;

        private static bool justCleared = false;
        public static bool playing = false;
        public static bool runPrefix = true;
        
        private static MediaFoundationReader mediaFoundationReader;
        public static WaveOutEvent waveOut;
        private static VolumeSampleProvider volumeSampleProvider;

        public static Sprite SelectedButtonSprite;
        public static Sprite UnselectedButtonSprite;
        public static List<Sprite> SelectedAntennaSprites = new List<Sprite>{}; // index = number of signal lines
        public static List<Sprite> UnselectedAntennaSprites = new List<Sprite>{}; // when not playing, stay on 0; when playing, bounce from 1-3
        public static Dictionary<string, List<Sprite>> StationIcons = new Dictionary<string, List<Sprite>>();
        public static List<Sprite> ConnectingSprites = new List<Sprite>{}; // index = number of signal lines, index 3 = disconnect
        public static Image connectIcon;
        //public static Sprite DiscSprite;
        //public static Sprite DiscSpinSprite;

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

        public static int normalButtonIndexOffset = 0;
        
        public static bool musicPlayerWasInterrupted = false;
        public static bool globalRadioWasInterrupted = false;
        public static int musicPlayerInterruptSamples = 0;

        public static void Initialize() { 
            IconSprite = LoadSprite(Path.Combine(dataDirectory, "icon.png")); 
            PhoneAPI.RegisterApp<AppNetRadio>(appName, IconSprite); 

            mediaFoundationReader = new MediaFoundationReader(Path.Combine(dataDirectory, "Tuning.mp3"));
            volumeSampleProvider = new VolumeSampleProvider(mediaFoundationReader.ToSampleProvider());
            waveOut = new WaveOutEvent();
            waveOut.Init(volumeSampleProvider);

            SelectedButtonSprite = LoadSprite(Path.Combine(dataDirectory, "SimpleButton-Selected.png"));
            UnselectedButtonSprite = LoadSprite(Path.Combine(dataDirectory, "SimpleButton.png"));
            //DiscSprite = LoadSprite(Path.Combine(dataDirectory, "StationDisc.png"));
            //DiscSpinSprite = LoadSprite(Path.Combine(dataDirectory, "StationDisc-Spinning.png"));

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
            NetRadioSettings.LoadURLs(); 
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
        }

        private void AddURLButtons() {
            realTime = 0f;
            normalButtonIndexOffset = 0;
            runPrefix = false;
            // add non-station buttons
            /* var normalButton = CreateSimpleButton("TEST");
            normalButton.OnConfirm += () => {};
            ScrollView.AddButton(normalButton); */
            runPrefix = true;

            int i = -1;
            int numberOfCustomStreams = 0;
            foreach (string url in GlobalRadio.streamURLs) {
                i++;
                string stationTitle = GlobalRadio.GetStationTitle(i);
                if (stationTitle == PluginName) { 
                    numberOfCustomStreams++;
                    stationTitle = "Custom Station " + numberOfCustomStreams;
                }
                var nextButton = CreateStationButton(stationTitle, url);
                nextButton.OnConfirm += () => {
                    if (GlobalRadio.threadRunning) { return; }

                    musicPlayerWasInterrupted = musicPlayer.IsPlaying;
                    if (musicPlayerWasInterrupted) { musicPlayerInterruptSamples = musicPlayer.CurrentTrackSamples; }
                    globalRadioWasInterrupted = GlobalRadio.playing && !GlobalRadio.failedToLoad;

                    musicPlayer.ForcePaused();
                    GlobalRadio.Play(ScrollView.SelectedIndex - normalButtonIndexOffset); //NetRadioPlugin.GlobalRadio.streamURLs.IndexOf(urlLabels[i].text));//(nextButton.Label.text));
                    
                    mediaFoundationReader.Position = 0;
                    playing = true;
                    waveOut.Play();
                    StartCoroutine(Instance.ClearButtons()); 
                };
                ScrollView.AddButton(nextButton);

                if (i >= urlIsTooLong.Count) { urlIsTooLong.Add(false); }
                else { urlIsTooLong[i] = false; }
            }

            if (connectIcon != null) { 
                connectIcon.sprite = ConnectingSprites[0];
                connectIcon.enabled = false; 
            }
        }

        private IEnumerator ClearButtons() {
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
            //waveOut.Stop(); playing = false; 
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

        public override void OnAppLateUpdate() {
            if (connectIcon != null) { connectIcon.enabled = !ScrollView.Buttons.Any(); }
            if (!ScrollView.Buttons.Any()) {
                realTime += Time.deltaTime;
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

            if (volumeSampleProvider != null) { //if (waveOut.PlaybackState == PlaybackState.Playing) {
                volumeSampleProvider.Volume = radioMusicVolume;
            }
            
            foreach (SimplePhoneButton button in ScrollView.Buttons) {
                if (!IsStationButton(button)) { 
                    if (ScrollView.Buttons.IndexOf(button) == ScrollView.SelectedIndex) {
                        time += Time.deltaTime;
                        realTime += Time.deltaTime;
                    } if (button.ButtonImage.sprite.texture.wrapMode != TextureWrapMode.Clamp) { 
                        button.ButtonImage.sprite.texture.wrapMode = TextureWrapMode.Clamp; 
                    } continue;
                }

                int buttonIndex = ScrollView.Buttons.IndexOf(button) - normalButtonIndexOffset;
                if (button == null || buttonIndex < 0 || buttonIndex >= ScrollView.Buttons.Count - normalButtonIndexOffset) {
                    continue;
                }

                bool selected = buttonIndex == ScrollView.SelectedIndex - normalButtonIndexOffset;
                TextMeshProUGUI urlLabel = button.Label.gameObject.transform.GetChild(0).gameObject.GetComponent<TextMeshProUGUI>();
                if (urlLabel == null) { continue; }

                if (buttonIndex >= urlWrapOffsets.Count) { 
                    urlWrapOffsets.Add(0); 
                }
                //foreach (TextMeshProUGUI child in button.Label.GetComponentsInChildren<TextMeshProUGUI>()) { if (child != button.Label && child.name.Contains("URL")) { urlLabel = child; } }
                if (selected && urlIsTooLong[buttonIndex]) {
                    time += Time.deltaTime;
                    realTime += Time.deltaTime;
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
            if (connectIcon != null) {
                Destroy(connectIcon.gameObject);
                connectIcon = null; 
            }
            ScrollView.RemoveAllButtons();
            base.OnAppDisable();
        }

        public override void OnAppInit()
        {
            base.OnAppInit();
            CreateTitleBar(appName, IconSprite);//CreateIconlessTitleBar(appName);
            ScrollView = PhoneScrollView.Create(this);
        }   

        private static SimplePhoneButton CreateSimpleButton(string label) {
            normalButtonIndexOffset++; 
            return PhoneUIUtility.CreateSimpleButton(label); 
        }

        private static SimplePhoneButton CreateStationButton(string label, string url) {
            runPrefix = false;
            var button = PhoneUIUtility.CreateSimpleButton(label); 
            var buttonAnimationParent = button.gameObject.transform.Find("Animation Parent");

            url = url.Replace("https://", "").Replace("http://", "").Replace("www.", "").Trim();
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

        public static bool IsStationButton(SimplePhoneButton button) {
            if (button == null || button.Label == null) { return false; }
            int childCount = button.Label.gameObject.transform.childCount;
            bool textMatches = button.Label.text.Contains("Custom Station") || button.Label.text == PluginName || NetRadioSettings.streamTitles.Contains(button.Label.text);
            return childCount > 2 && textMatches;
        }
    }
}