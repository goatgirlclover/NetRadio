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

namespace NetRadio
{
    public class NetRadioManager : MonoBehaviour
    {
        public List<string> streamURLs = new List<string> {}; /*
            "https://stream2.magic-media.nl:1100/stream",
        }; */

        public FfmpegDecoder ffmpegReader;
        public DirectSoundOut directSoundOut;
        public VolumeSource volumeSource;

        /*public VorbisWaveReader vorbisReader;
        public VorbisWaveReader vorbisReaderMono;*/
        public IWaveSource mediaFoundationReader;

        // for spatial audio emulation
        /*public FfmpegDecoder ffmpegReaderMono;
        public DirectSoundOut waveOutMono;
        public SampleAggregatorBase centerSource;
        public VolumeSource monoSource; 
        public VolumeSource monoVolumeSource; 
        public PanSource pannedMonoSource; 
        public bool spatialize = true;
        public float minDistance = 0.5f;
        public float maxDistance = 10f;
        public float pan { get; private set; } = 0f;
*/
        public PeakMeter meter;
        public float streamSampleVolume { get; private set; } = 0f; //{ get { return meter.StreamVolume; }}

        public float radioVolume { get { return volumeSource != null ? volumeSource.Volume : 0f; } 
        set { 
            if (volumeSource != null) { volumeSource.Volume = Mathf.Clamp01(value)*volume; }
        }}

        public float volume = 1.0f; 
        public bool playing { 
            get { return directSoundOut != null ? directSoundOut.PlaybackState == PlaybackState.Playing : false; } 
            set { if (value == true) { Resume(); } else { Stop(); } } 
        }

        public bool stopped = false;

        public int currentStation { get; private set; } = -1;
        public int previousStation { get; private set; } = -1;
        public string currentStationURL { get { return streamURLs[currentStation]; }}
        
        public Thread playURLThread;
        public Thread playURLChildThread;
        public bool useThread = true;
        public bool threadRunning { get { return playURLThread is Thread ? playURLThread.IsAlive : false; }}
        public bool failedToLoad = false;
        
        private static System.Net.Http.HttpClient m_httpClient = null;
        public IcecastStatus currentMetadata;
        public string currentSong { get; private set; }
        public bool trackingMetadata { get; private set; } = false;

        public float connectionTime = 0f;
        public float metadataTimeOffset = 0f;

        //public static bool deviceChanged = false;
        //private bool trackingAudioDevice = false;

        public static bool enableMetadataTracking = true;
        private Task trackingMetadataTask;

        private long oldPosition = -100;
        private int amountOfTimesFoundAtSamePosition = 0;
        public bool skipDisposal = false;
        
        private void Start() { 
            Log.LogInfo("Loaded radio manager");
            if (GlobalRadio == this) { 
                NetRadioSettings.LoadURLs(); 
                new NetRadioSaveData();
            }
            //UnityEngine.AudioSettings.OnAudioConfigurationChanged += (v) => deviceChanged = v;
        }

        void FixedUpdate() {
            if (directSoundOut != null && ffmpegReader != null) {
                if (playing) {
                    if ((ffmpegReader.Position + 150 >= ffmpegReader.Length && ffmpegReader.Length != 0 && ffmpegReader.CanSeek)) {
                        StopRadio();
                    }
                    
                    if (ffmpegReader.Position == oldPosition && ffmpegReader.Position > 300000) { 
                        amountOfTimesFoundAtSamePosition++;
                        if (amountOfTimesFoundAtSamePosition > 10) { 
                            StopRadio(); 
                            amountOfTimesFoundAtSamePosition = 0;
                            skipDisposal = true;
                        }
                    } else {
                        amountOfTimesFoundAtSamePosition = 0;
                    }
                    oldPosition = ffmpegReader.Position;
                }
            }
        }

        void OnDisable() {
            //Instances.Remove(this);
            StopRadio();
            /*if (playURLChildThread != null) { playURLChildThread.Abort(); }
            if (playURLThread != null) { playURLThread.Abort(); }*/
            trackingMetadata = false;
            stopped = true;
            
            trackingMetadataTask.Wait();
            trackingMetadataTask.Dispose(); 
            DisposeOfReaders();
        }

        private void DisposeOfReaders() {
            try { if (mediaFoundationReader != null) { mediaFoundationReader.Dispose(); }
            } catch (System.Exception ex) { Log.LogError(ex); } 
            mediaFoundationReader = null;
            try { if (ffmpegReader != null) { ffmpegReader.Dispose(); }
            } catch (System.Exception ex) { Log.LogError(ex); } 
            ffmpegReader = null;
            try { if (directSoundOut != null) { directSoundOut.Dispose(); }
            } catch (System.Exception ex) { Log.LogError(ex); }
            directSoundOut = null;
        
            if (m_httpClient != null) { m_httpClient.Dispose(); }
        }

        public void Play(int streamIndex = -999) { 
            PlayRadioStation(streamIndex == -999 ? currentStation : streamIndex); 
        }
        
        public void Stop() { StopRadio(); }

        public void Resume() { 
            if (directSoundOut != null) { directSoundOut.Play(); }
            //if (waveOutMono != null) { waveOutMono.Play(); }
        }

        public void Pause() {
            stopped = true;
            if (directSoundOut != null) { directSoundOut.Pause(); }
            //if (waveOutMono != null) { waveOutMono.Pause(); }
        }

        public static void ReloadAllStations() { // backup option for fixing syncing issues 
            NetRadioSettings.LoadURLs();
            NetRadioSettings.RefreshMusicApp();
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
                //yield return new WaitForSeconds(0.2f);
                previousStation = currentStation;
                currentStation = streamIndex;

                if (useThread && !NetRadioSettings.noThreads.Value) { // no freeze
                    playURLThread = new Thread(new ThreadStart(StartPlayURL));
                    playURLThread.Start();
                } else { PlayURL(); } // yes freeze
            }
        }

        private void StartPlayURL() {
            //CheckForRedirection();    
            var threadStart = NetRadioSettings.moreCodecs.Value ? new ThreadStart(PlayURLMF) : new ThreadStart(PlayURL);
            playURLChildThread = new Thread(threadStart);
            playURLChildThread.Start();
            if (!playURLChildThread.Join(new TimeSpan(0, 0, 11)))
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

        // NAUDIO TO CSCORE TRANSLATION
        // using CSCore.Streams; using CSCore.Ffmpeg; using CSCore.SoundOut; using CSCore; using CSCore.Streams.SampleConverter;
        // mediaFoundationReader = ffmpegReader (cross-platform, more format support)
        // volumeSampleProvider = volumeSource
        // waveprovider.ToSampleProvider() = WaveToSampleBase.CreateConverter(waveprovider)
        // StereoToMonoSampleProvider = StereoToMonoSource
        // PanningSampleProvider = PanSource
        // MeteringSampleProvider = PeakMeter
        // directSoundOut.Init() = directSoundOut.Initialize()
        // waveout functions and variables seem identical 

        private void PlayURLMF() {
            CheckForRedirection();

            try {
                DisposeOfReaders();
                m_httpClient = CreateHTTPClient();

                float realtimeAtStart = Time.realtimeSinceStartup;
                var mfReader = new CSCore.MediaFoundation.MediaFoundationDecoder(currentStationURL);
                connectionTime = Time.realtimeSinceStartup - realtimeAtStart;
                mediaFoundationReader = (IWaveSource)mfReader;

                int bufferInt = mfReader.WaveFormat.SampleRate * mfReader.WaveFormat.BlockAlign;
                bufferInt = (int)Mathf.Round((float)bufferInt * (float)NetRadio.bufferTimeInSeconds);
                var buffer = new BufferSource(ffmpegReader, bufferInt);
                
                meter = new PeakMeter(WaveToSampleBase.CreateConverter(buffer));
                meter.Interval = 50;
                if (GlobalRadio == this) {
                    meter.PeakCalculated += (s,e) => streamSampleVolume = meter.PeakValue;
                }

                volumeSource = new VolumeSource(meter);

                directSoundOut = InitializeSoundOut(volumeSource);
                NetRadioPlugin.UpdateGlobalRadioVolume();
                directSoundOut.Play();
            } 
            catch (System.Exception exception) { 
                if (currentStationURL.StartsWith("http://")) {
                    streamURLs[currentStation] = currentStationURL.Replace("http://", "https://");
                    PlayURLMF();
                    return;
                } else { 
                    Log.LogError($"(Media Foundation) Error playing radio: {exception.Message}"); 
                    Log.LogError(exception.StackTrace); 
                    streamURLs[currentStation] = currentStationURL.Replace("https://", "http://");
                    PlayURL();
                    return;
                }
            }

            if (GlobalRadio == this && !failedToLoad && !trackingMetadata) {
                // metadata on separate connection, so let them run together
                //StartCoroutine("TrackAudioDevice");
                if (trackingMetadataTask != null) { trackingMetadataTask.Wait(); } 
                trackingMetadataTask = TrackMetadata(); //StartCoroutine(metadataCoroutine);
                
            }

            stopped = false;
        }

        private void PlayURL() {
            CheckForRedirection();

            try {
                if (!skipDisposal) { DisposeOfReaders(); }
                skipDisposal = false;
                m_httpClient = CreateHTTPClient();

                float realtimeAtStart = Time.realtimeSinceStartup;
                var ffr = new FfmpegDecoder(currentStationURL);
                connectionTime = Time.realtimeSinceStartup - realtimeAtStart;
                ffmpegReader = ffr;
                
                int bufferInt = ffmpegReader.WaveFormat.SampleRate * ffmpegReader.WaveFormat.BlockAlign;
                bufferInt = (int)Mathf.Round((float)bufferInt * (float)NetRadio.bufferTimeInSeconds);
                var buffer = new BufferSource(ffmpegReader, bufferInt);
                //var mfReader = new CSCore.MediaFoundation.MediaFoundationDecoder(currentStationURL);

                meter = new PeakMeter(WaveToSampleBase.CreateConverter(buffer));
                meter.Interval = 50;
                if (GlobalRadio == this) {
                    meter.PeakCalculated += (s,e) => streamSampleVolume = meter.PeakValue;
                    // Log.LogInfo(meter.PeakValue);
                }

                volumeSource = new VolumeSource(meter); //spatialize ? new VolumeSource(centerSource) : new VolumeSource(meter);
                directSoundOut = InitializeSoundOut(volumeSource);
                NetRadioPlugin.UpdateGlobalRadioVolume();
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
                //currentStation = -1;
            }

            if (GlobalRadio == this && !failedToLoad && !trackingMetadata) {
                // metadata on separate connection, so let them run together
                //StartCoroutine("TrackAudioDevice");
                if (trackingMetadataTask != null) { trackingMetadataTask.Wait(); } 
                trackingMetadataTask = TrackMetadata(); //StartCoroutine(metadataCoroutine);
                
            }

            stopped = false;
        }

        private async Task OutputStopped(PlaybackStoppedEventArgs args) {
            Log.LogWarning("Stopped DirectSound output");
            if (args.HasError) {
                Exception exception = args.Exception;
                Log.LogWarning(exception.Message);
            }
            
            bool deviceChanged = !stopped; 
            if (deviceChanged) {
                Log.LogWarning("Output device changed");
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
            currentSong = "Unknown Track";
            if (directSoundOut != null) { directSoundOut.Stop(); }
        }

        public String GetStationTitle(int streamIndex = -999) {
            if (GlobalRadio != this) { return "LocalRadio"; } 
            int station = streamIndex == -999 ? currentStation : streamIndex;
            
            if (station < 0 || station >= streamURLs.Count || station >= NetRadioSettings.streamTitles.Count) { return PluginName; }
            else { return NetRadioSettings.streamTitles[station]; }
        }

        public async Task<IcecastStatus> GetMetadata(string url = "") {
            if (m_httpClient == null) { m_httpClient = CreateHTTPClient(); }
            if (url == "") { url = currentStationURL; }
            IcecastStatus metadata = await GetMetaDataFromIceCastStream(url);
            return metadata; 
        }

        private async Task TrackMetadata() {
            if (!enableMetadataTracking) { return; }
            //if (m_httpClient == null) { m_httpClient = CreateHTTPClient(); }
            trackingMetadata = true;

            IcecastStatus oldMetadata = null; 
            int oldStation = currentStation;
            bool looped = false;
            
            while (trackingMetadata && playing) {
                if (!string.IsNullOrWhiteSpace(currentStationURL)) { 
                    try {
                        float realtimeAtStart = Time.realtimeSinceStartup;
                        currentMetadata = await GetMetaDataFromIceCastStream(currentStationURL); 
                        float processingTime = Time.realtimeSinceStartup - realtimeAtStart;
                        if (currentMetadata != oldMetadata) {
                            /*metadataTimeOffset = connectionTime - processingTime;
                            metadataTimeOffset += NetRadio.bufferTimeInSeconds;                        
                            if (metadataTimeOffset > 0f && looped) { 
                                await Task.Delay((int)(metadataTimeOffset*1000)); 
                            }*/
                            HandleMetadata(currentMetadata); 
                            oldMetadata = currentMetadata;
                            looped = true;
                        }
                    } catch (System.Exception exception) {
                        Log.LogError($"Error tracking metadata: {exception.Message}"); 
                        Log.LogError(exception.StackTrace); 
                    }
                    
                }  
                //Log.LogInfo(currentMetadata);
                await Task.Delay(2000); //await Task.Yield(); //yield return null;
            }

            trackingMetadata = false; // cts.Cancel();
        }

        private void HandleMetadata(IcecastStatus originalMetadata) {
            if (!playing) { return; }
            currentSong = originalMetadata.icestats.source[0].title;
            Log.LogInfo("Metadata updated for station " + GetStationTitle() + ": " + currentSong);
            NetRadioPlugin.UpdateCurrentSong();
        }

        private System.Net.Http.HttpClient CreateHTTPClient() { //private async Task<System.Net.Http.HttpClient> CreateHTTPClient() {
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
            Uri uri = new Uri(url); 
            do {   
                string baseUrl = GetParentUriString(uri);
                if (!baseUrl.EndsWith("/")) { baseUrl = baseUrl + "/"; }
                string statusUrl = baseUrl + "status-json.xsl";
                Log.LogInfo(statusUrl); 

                try { 
                    var response = await m_httpClient.GetStringAsync(statusUrl);
                    responseString = response.ToString();
                    connected = true;
                } catch (System.Exception exception) {
                    if (exception is HttpRequestException) {
                        if (uri.ToString() == baseUrl) { return null; } //fallback
                        else { uri = new Uri(baseUrl); }
                    }
                    else { 
                        Log.LogError($"Error getting metadata: {exception.Message}"); 
                        Log.LogError(exception.StackTrace);  
                    }
                }
            } while (connected == false);
            
            try {
                IcecastStatus icecastStatus = JsonConvert.DeserializeObject<IcecastStatus>(responseString);
                return icecastStatus; 
            } catch (System.Exception exception) {
                Log.LogError($"Error getting metadata: {exception.Message}"); 
                Log.LogError(exception.StackTrace); 
            }
            
            return null;
        }

        public void AdjustMusicVolume(float volume, float multiplier = 0.5f) { // make musicPlayer and GlobalRadio quieter while approaching spatialRadio 
            musicPlayer.audioSource.volume = 1 - (volume*multiplier);
            GlobalRadio.volume = 1 - (volume*multiplier);
        }

        // EXPERIMENTAL
        public static NetRadioManager CreateRadio(Transform parent) {
            GameObject radioHolder = new GameObject();
            NetRadioManager NetRadioManager = radioHolder.AddComponent<NetRadioManager>();
            radioHolder.transform.parent = parent;
            //NetRadioManager.gameObject.transform.position = parent.transform.position;
            return NetRadioManager;
        }
    }
}
