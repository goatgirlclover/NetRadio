using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using UnityEngine;
using UnityEngine.Networking;
using TMPro;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.IO;
using System.Text.RegularExpressions;
using System.Net.Http;
using Newtonsoft.Json;

using CommonAPI;
using CSCore; 
using CSCore.Streams; 
using CSCore.Streams.SampleConverter;
using CSCore.Ffmpeg; 
using CSCore.SoundOut; 

using static NetRadio.NetRadio;
using NetRadio.Metadata; 
using NetRadio.Apps;

namespace NetRadio
{
    public class NetRadioManager : MonoBehaviour
    {
        public List<string> streamURLs = new List<string> {};

        public FfmpegDecoder ffmpegReader { get; private set; } 
        //public IWaveSource mediaFoundationReader { get; private set; } 
        public DirectSoundOut directSoundOut { get; private set; } 
        public VolumeSource volumeSource { get; private set; } 
        public PeakMeter meter { get; private set; } 

        public float volume = 1.0f; 
        public float radioVolume { 
            get { return volumeSource != null ? volumeSource.Volume : 0f; } 
            set { if (volumeSource != null) { volumeSource.Volume = Mathf.Clamp01(value)*volume; } }
        }
        public float streamSampleVolume { get; private set; } = 0f; //{ get { return meter.StreamVolume; }}
        

        public bool playing { 
            get { return directSoundOut != null ? directSoundOut.PlaybackState == PlaybackState.Playing : false; } 
            set { if (value == true) { Resume(); } else { Stop(); } } 
        }
        private bool stopped = false;

        public int currentStation { get; private set; } = -1;
        public int previousStation { get; private set; } = -1;
        public string currentStationURL { get { return streamURLs[currentStation]; }}
        
        private Thread playURLThread;
        private Thread playURLChildThread;
        public bool useThread = true;
        public bool threadRunning { get { return playURLThread is Thread ? playURLThread.IsAlive : false; }}
        public bool failedToLoad = false;
        
        private static System.Net.Http.HttpClient m_httpClient = null;
        public IcecastStatus currentMetadata { get; private set; }
        public string currentSong { get; private set; }
        private float connectionTime = 0f;
        private float metadataTimeOffset = 0f;

        public static bool enableMetadataTracking { get; private set; } = true;
        private Task trackingMetadataTask;
        public bool trackingMetadata { get; private set; } = false;

        private long oldPosition = -100;
        private int amountOfTimesFoundAtSamePosition = 0;
        public bool skipDisposal { get; private set; } = false;

        private Dictionary<string, string> knownStatusXSLs = new Dictionary<string, string>();
        
        private void Start() { 
            if (GlobalRadio == this) { 
                Settings.LoadURLs(); 
                new SaveData();
            }
            Log.LogInfo("Loaded radio manager");
        }

        void FixedUpdate() {
            if (directSoundOut != null && ffmpegReader != null) {
                if (playing) {
                    if ((ffmpegReader.Position + 150 >= ffmpegReader.Length && ffmpegReader.Length != 0 && ffmpegReader.CanSeek)) {
                        StopRadio();
                        AppNetRadio.PlaySFX("lost");
                    }
                    
                    if (ffmpegReader.Position == oldPosition && ffmpegReader.Position > 300000) { 
                        amountOfTimesFoundAtSamePosition++;
                        if (amountOfTimesFoundAtSamePosition > (int)Mathf.Round(NetRadio.bufferTimeInSeconds*30.0f)) { 
                            StopRadio(); 
                            AppNetRadio.PlaySFX("lost");
                            amountOfTimesFoundAtSamePosition = 0;
                            skipDisposal = true;
                        }
                    } else { amountOfTimesFoundAtSamePosition = 0; }
                    oldPosition = ffmpegReader.Position;
                }
            }
        }

        void OnDisable() {
            skipDisposal = false;
            CleanUp();
        }

        public void CleanUp() {
            StopRadio();
            stopped = true;
            try { StopTrackingMetadata(); DisposeOfReaders(); } 
            catch (System.Exception) { return; }
        }

        public void ResetMetadata() {
            currentSong = "Unknown Track"; 
            currentMetadata = null;
        }

        public void StopTrackingMetadata(bool wait = true) {
            trackingMetadata = false;
            if (trackingMetadataTask != null && wait) {
                trackingMetadataTask.Wait();
                trackingMetadataTask.Dispose(); 
            }
        }

        private void DisposeOfReaders() {
            /*try { if (mediaFoundationReader != null) { mediaFoundationReader.Dispose(); }
            } catch (System.Exception ex) { Log.LogError(ex); } 
            mediaFoundationReader = null;*/
            try { if (ffmpegReader != null) { ffmpegReader.Dispose(); }
            } catch (System.Exception ex) { Log.LogError(ex); } 
            ffmpegReader = null;
            try { if (directSoundOut != null) { directSoundOut.Dispose(); }
            } catch (System.Exception ex) { Log.LogError(ex); }
            directSoundOut = null;
        
            if (m_httpClient != null) { m_httpClient.Dispose(); }
        }

        public void Play(int streamIndex = -999) { PlayRadioStation(streamIndex == -999 ? currentStation : streamIndex); }
        public void Stop() { StopRadio(); }
        public void Resume() { if (directSoundOut != null) { directSoundOut.Play(); } }
        public void Pause() { stopped = true; if (directSoundOut != null) { directSoundOut.Pause(); } }

        public static void ReloadAllStations() { // backup option for fixing syncing issues 
            Settings.LoadURLs();
            Settings.RefreshMusicApp();
            NetRadioManager radioManager = NetRadio.GlobalRadio; 
            //foreach (NetRadioManager radioManager in Resources.FindObjectsOfTypeAll<NetRadioManager>()) { 
                if (radioManager != null && radioManager.currentStation >= 0 && radioManager.directSoundOut.PlaybackState != PlaybackState.Stopped) { 
                    radioManager.PlayRadioStation(radioManager.currentStation); 
                }
            //}
        }

        public void PlayRandomStation() {
            int randomStation = rando.Next(0, streamURLs.Count);
            PlayRadioStation(randomStation);
        }

        public void PlayRadioStation(int streamIndex) {
            failedToLoad = false;
            if (streamIndex < 0 || streamIndex >= streamURLs.Count) {
                Log.LogError("PlayRadioStation: Invalid stream index!");
                failedToLoad = true;
            } else {
                if (playURLThread is Thread) {
                    if (playURLThread.IsAlive) { 
                        playURLThread.Join(); 
                        playURLChildThread.Join();
                    }
                }
                
                StopRadio();
                previousStation = currentStation;
                currentStation = streamIndex;

                if (useThread && !Settings.noThreads.Value) { // no freeze
                    playURLThread = new Thread(new ThreadStart(StartPlayURL));
                    playURLThread.Start();
                } else { PlayURL(); } // yes freeze
            }
        }

        private void StartPlayURL() {
            var threadStart = new ThreadStart(PlayURL); 
            playURLChildThread = new Thread(threadStart);
            playURLChildThread.Start();
            if (!playURLChildThread.Join(new TimeSpan(0, 0, 15)))
            {    
                failedToLoad = true;
                playURLChildThread.Abort();
            }
        }

        private void CheckForRedirection() {
            try {
                if (!NetRadio.hasRedir.Contains(currentStationURL)) {
                    string redirURL = NetRadio.GetRedirectedURL(currentStationURL);
                    if (redirURL != null) { streamURLs[currentStation] = redirURL; }
                }    
            } catch (Exception) {
                return;
            }  
        }

        private void PlayURL() {
            CheckForRedirection();

            try {
                if (!skipDisposal) { DisposeOfReaders(); }
                skipDisposal = false;
                m_httpClient = CreateHTTPClient();

                float realtimeAtStart = Time.realtimeSinceStartup;
                ffmpegReader = new FfmpegDecoder(currentStationURL);
                connectionTime = Time.realtimeSinceStartup - realtimeAtStart;
                
                //int bufferInt = ffmpegReader.WaveFormat.SampleRate * ffmpegReader.WaveFormat.BlockAlign;
                //bufferInt = (int)Mathf.Round((float)bufferInt * (float)NetRadio.bufferTimeInSeconds);
                int bufferInt = (int)Mathf.Round((float)ffmpegReader.WaveFormat.BytesPerSecond * (float)NetRadio.bufferTimeInSeconds);
                var buffer = new BufferSource(ffmpegReader, bufferInt);

                meter = new PeakMeter(WaveToSampleBase.CreateConverter(buffer));
                meter.Interval = 50;
                if (GlobalRadio == this) {
                    meter.PeakCalculated += (s,e) => streamSampleVolume = meter.PeakValue;
                }

                volumeSource = new VolumeSource(meter); 
                directSoundOut = InitializeSoundOut(volumeSource);
                NetRadio.UpdateGlobalRadioVolume();
                directSoundOut.Play();

                connectionTime = Time.realtimeSinceStartup - realtimeAtStart;
            } 
            catch (System.Exception exception) { 
                if (currentStationURL.StartsWith("http://")) {
                    streamURLs[currentStation] = currentStationURL.Replace("http://", "https://");
                    PlayURL();
                    return;
                }

                failedToLoad = true;
                Log.LogError($"Error playing radio: {exception.Message}"); 
                Log.LogError(exception.StackTrace); 
            }

            if (GlobalRadio == this && !failedToLoad && !trackingMetadata) {
                StartTrackingMetadata();
            }

            stopped = false;
        }

        public void StartTrackingMetadata(bool overrideCancel = false) {
            if (trackingMetadataTask != null) { trackingMetadataTask.Wait(); } 
            trackingMetadataTask = TrackMetadata(overrideCancel); 
        }

        private async Task OutputStopped(PlaybackStoppedEventArgs args) {
            Log.LogWarning("Stopped DirectSound output");
            if (args.HasError) {
                Exception exception = args.Exception;
                Log.LogWarning(exception.Message);
            }
            
            bool deviceChanged = !stopped; 
            if (deviceChanged && !(AppNetRadio.playing || threadRunning)) {
                Log.LogWarning("Output device changed?");
                //var ogSoundOut = directSoundOut;
                await Task.Delay(500);
                Log.LogWarning("Reconnecting?");
                PlayRadioStation(currentStation);
                AppNetRadio.PlayNoise();
                //ogSoundOut.Dispose();
            }
        }

        private DirectSoundOut InitializeSoundOut(ISampleSource source, DirectSoundOut original = null) { 
            return InitializeSoundOut(new SampleToIeeeFloat32(source), original); 
        }

        private DirectSoundOut InitializeSoundOut(IWaveSource source, DirectSoundOut original = null) {
            DirectSoundOut returnOut = original == null ? new DirectSoundOut(NetRadio.waveOutLatency) : original;
            returnOut.Stopped += (a, b) => _=OutputStopped(b);
            returnOut.Initialize(source);
            return returnOut;
        }

        public void StopRadio() {
            stopped = true;
            trackingMetadata = false;
            ResetMetadata();
            if (directSoundOut != null) { directSoundOut.Stop(); }
        }

        public String GetStationTitle(int streamIndex = -999) {
            if (GlobalRadio != this) { return "LocalRadio"; } 
            int station = streamIndex == -999 ? currentStation : streamIndex;
            
            if (station < 0 || station >= streamURLs.Count || station >= Settings.streamTitles.Count) { return PluginName; }
            else { return Settings.streamTitles[station]; }
        }

        public async Task<IcecastStatus> GetMetadata(string url = "") {
            if (m_httpClient == null) { m_httpClient = CreateHTTPClient(); }
            if (url == "") { url = currentStationURL; }
            IcecastStatus metadata = await GetMetaDataFromIceCastStream(url);
            return metadata; 
        }

        public async Task TrackMetadata(bool overrideCancel = false) {
            string urlForCurrent = NetRadio.StandardizeURL(Settings.configURLs[currentStation]);
            bool cancelTracking = SaveData.stationSettingsByURL.ContainsKey(urlForCurrent) 
                                    ? SaveData.stationSettingsByURL[urlForCurrent].metadataMode == 0 : false;
            if (!overrideCancel && (!enableMetadataTracking || cancelTracking)) { return; }
            trackingMetadata = true;

            IcecastStatus oldMetadata = null; 
            int oldStation = currentStation;
            bool looped = false;
            
            while (trackingMetadata && playing) {
                int awaitTime = 1000; 
                if (!string.IsNullOrWhiteSpace(currentStationURL)) { 
                    try {
                        float realtimeAtStart = Time.realtimeSinceStartup;
                        currentMetadata = await GetMetaDataFromIceCastStream(currentStationURL); 
                        float processingTime = Time.realtimeSinceStartup - realtimeAtStart;

                        if (currentMetadata == null) {
                            Log.LogError($"Null metadata. Cancelling tracking"); 
                            trackingMetadata = false;
                            return;
                        } else if (oldMetadata == null || GetSource(currentMetadata, currentStationURL).title != GetSource(oldMetadata, currentStationURL).title) {
                            decimal savedTime = SaveData.stationSettingsByURL.ContainsKey(urlForCurrent) 
                                    ? SaveData.stationSettingsByURL[urlForCurrent].metadataTimeOffsetSeconds : (decimal)0.0;
                            metadataTimeOffset = (float)savedTime;
                            if (metadataTimeOffset > 0f && looped) { 
                                awaitTime = (int)Mathf.Clamp(awaitTime - metadataTimeOffset*1000.0f, 20.0f, 2000.0f); 
                                await DelayTrackingInSteps((int)(metadataTimeOffset*1000.0f)); 
                            }
                            looped = true;
                            HandleMetadata(currentMetadata); 
                            oldMetadata = currentMetadata;
                        }
                    } catch (System.Exception exception) {
                        Log.LogError($"Error tracking metadata: {exception.Message}"); 
                        Log.LogError(exception.StackTrace); 
                        trackingMetadata = false;
                    }
                    
                }  
                await DelayTrackingInSteps(awaitTime); //await Task.Yield(); 
            }
            trackingMetadata = false;
        }

        private void HandleMetadata(IcecastStatus originalMetadata) {
            if (!playing) { return; }
            string oldSong = currentSong;
            Source source = GetSource(originalMetadata, currentStationURL);
            currentSong = source.title;
            if (!string.IsNullOrWhiteSpace(source.artist)) { 
                currentSong = source.artist + " - " + source.title;
                currentSong = currentSong.Trim();
                if (currentSong.StartsWith("- " + source.title)) { 
                    currentSong = source.title; 
                }
            }

            Log.LogInfo("Metadata updated for station " + GetStationTitle() + ": " + currentSong);
            NetRadio.UpdateCurrentSong();
        }

        private System.Net.Http.HttpClient CreateHTTPClient() { 
            var httpClient = new System.Net.Http.HttpClient();
            httpClient.DefaultRequestHeaders.Add("Icy-MetaData", "1");
            httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("C# App (BRC NetRadio)");
            httpClient.Timeout = TimeSpan.FromSeconds(10);
            return httpClient;
        }

        private async Task<IcecastStatus> GetMetaDataFromIceCastStream(string url) {
            if (url == null) { return null; }

            bool connected = false;
            string responseString = "";
            string prevStatusURL = "";
            string statusUrl = "";
            Uri uri = new Uri(url); 
            do {  
                string baseUrl = GetParentUriString(uri);
                if (knownStatusXSLs.ContainsKey(url)) { statusUrl = knownStatusXSLs[url]; }
                else {
                    if (!baseUrl.EndsWith("/")) { baseUrl = baseUrl + "/"; }
                    statusUrl = baseUrl + "status-json.xsl";
                    Log.LogInfo("Checking for status-json.xsl: " + statusUrl);
                    if (statusUrl == prevStatusURL) {
                        Log.LogWarning("Already checked!");
                        return null; 
                    } 
                }
                
                try { 
                    var response = await m_httpClient.GetStringAsync(statusUrl);
                    responseString = response.ToString();
                    connected = true;
                } catch (System.Exception exception) {
                    if (exception is HttpRequestException) {
                        if (knownStatusXSLs.ContainsKey(url)) { knownStatusXSLs.Remove(url); }
                        if (uri.ToString() == baseUrl || statusUrl == prevStatusURL) { return null; } //fallback
                        else { uri = new Uri(baseUrl); }
                    }
                    else { 
                        Log.LogError($"Error getting metadata: {exception.Message}"); 
                        Log.LogError(exception.StackTrace);  
                        return null;
                    }
                }
                prevStatusURL = statusUrl;
            } while (connected == false);
            
            try {
                IcecastStatus icecastStatus = JsonConvert.DeserializeObject<IcecastStatus>(responseString);
                if (!knownStatusXSLs.ContainsKey(url)) { knownStatusXSLs.Add(url, statusUrl); }
                return icecastStatus; 
            } catch (System.Exception exception) {
                Log.LogError($"Error getting metadata: {exception.Message}"); 
                Log.LogError(exception.StackTrace); 
            }
            
            return null;
        }

        public static NetRadioManager CreateRadio(Transform parent) {
            GameObject radioHolder = new GameObject();
            NetRadioManager NetRadioManager = radioHolder.AddComponent<NetRadioManager>();
            radioHolder.transform.parent = parent;
            //NetRadioManager.gameObject.transform.position = parent.transform.position;
            return NetRadioManager;
        }

        public async Task DelayTrackingInSteps(int delayTime, int step = 20) {
            int awaitTime = delayTime;
            int i = 0;
            while (i*step < delayTime) { 
                await Task.Delay(step);
                i++;
                if (!trackingMetadata) { 
                    Log.LogWarning("Delay ending early (" + (i*step).ToString() + "/" + delayTime.ToString() + ")");
                    return; 
                }
            }
            return;
        }
    }
}
