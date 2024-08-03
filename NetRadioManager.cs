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
using CommonAPI;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using NAudio.CoreAudioApi.Interfaces;
using NAudio.MediaFoundation;
using NAudio.Utils;
using System.Text.RegularExpressions;
using static NetRadio.NetRadio;

namespace NetRadio
{
    public class NetRadioManager : MonoBehaviour
    {
        public List<string> streamURLs = new List<string> {
            "https://stream2.magic-media.nl:1100/stream",
        };

        public MediaFoundationReader mediaFoundationReader;
        public WaveOutEvent waveOut;
        public VolumeSampleProvider volumeSampleProvider;

        // for spatial audio emulation
        public MediaFoundationReader mediaFoundationReaderMono;
        public WaveOutEvent waveOutMono;
        public ISampleProvider centerSampleProvider;
        public VolumeSampleProvider monoSampleProvider; 
        public VolumeSampleProvider monoVolumeSampleProvider; 
        public PanningSampleProvider pannedMonoSampleProvider; 
        
        public float minDistance = 0.5f;
        public float maxDistance = 10f;

        public MeteringSampleProvider meter;
        public float streamSampleVolume { get; private set; } = 0f; //{ get { return meter.StreamVolume; }}

        // for audio effects (https://github.com/gregyjames/SlowReverbMaker)
        //private ReverbEffect reverbSampleProvider;
        //private EchoSampleProvider echoSampleProvider;
        //private BoostFreqSampleProvider boostSampleProvider;
        //private FilterProvider filterProvider;

        public bool spatialize = true;

        public float radioVolume { get { return volumeSampleProvider != null ? volumeSampleProvider.Volume : 0f; } 
        set { 
            if (volumeSampleProvider != null) { volumeSampleProvider.Volume = Mathf.Clamp01(value)*volume; }
            if (monoVolumeSampleProvider != null) { monoVolumeSampleProvider.Volume = Mathf.Clamp01(value)*volume; }
        }}

        public float volume = 0.9f; // base volume for radio. set to 1.0 for globalradio

        public bool playing { 
            get { return waveOut != null ? waveOut.PlaybackState == PlaybackState.Playing : false; } 
            set { if (value == true) { Resume(); } else { Stop(); } } 
        }

        public float pan { get; private set; } = 0f;

        public int currentStation { get; private set; } = -1; 
        public int previousStation { get; private set; } = -1;
        public string currentStationURL { get { return streamURLs[currentStation]; }}
        
        public Thread playURLThread;
        public bool useThread = true;
        public bool threadRunning { get { return playURLThread is Thread ? playURLThread.IsAlive : false; }}
        public bool failedToLoad = false;
        
        private static System.Net.Http.HttpClient m_httpClient = null;
        private string currentMetadata;
        public string currentSong { get; private set; }
        public bool trackingMetadata { get; private set; } = false;
        
        //public CancellationTokenSource cts; 
        //Regex metaRegex = new Regex(@"/(?<=\')(.*?)(?=\')/");

        private void Start() { 
            Log.LogInfo("Loaded radio manager");
            if (GlobalRadio == this) { NetRadioSettings.LoadURLs(); }
        }

        void Update() {
            if (spatialize) { CalculateSpatial(); }
        }

        void OnDestroy() {
            //Instances.Remove(this);
            StopRadio();
            if (playURLThread != null) { playURLThread.Abort(); }
            trackingMetadata = false;
            if (waveOut != null) { waveOut.Dispose(); }
            if (waveOutMono != null) { waveOutMono.Dispose(); }
            if (mediaFoundationReader != null) { mediaFoundationReader.Dispose(); }
            if (mediaFoundationReaderMono != null) { mediaFoundationReaderMono.Dispose(); }
            //if (volumeSampleProvider != null) { volumeSampleProvider.Dispose(); }
        }

        public void Play(int streamIndex = -999) { 
            PlayRadioStation(streamIndex == -999 ? currentStation : streamIndex); 
        }
        
        public void Stop() { StopRadio(); }

        public void Resume() { 
            if (waveOut != null) { waveOut.Play(); }
            if (waveOutMono != null) { waveOutMono.Play(); }
        }
        public void Pause() {
            if (waveOut != null) { waveOut.Pause(); }
            if (waveOutMono != null) { waveOutMono.Pause(); }
        }

        public static void ReloadAllStations() { // backup option for fixing syncing issues 
            foreach (NetRadioManager radioManager in Resources.FindObjectsOfTypeAll<NetRadioManager>()) { 
                if (radioManager != null && radioManager.currentStation >= 0 && radioManager.waveOut.PlaybackState != PlaybackState.Stopped) { 
                    radioManager.PlayRadioStation(radioManager.currentStation); 
                }
            }
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
                    if (playURLThread.IsAlive) { playURLThread.Join(); }
                }
                
                StopRadio();
                //yield return new WaitForSeconds(0.2f);
                previousStation = currentStation;
                currentStation = streamIndex;

                if (useThread && !NetRadioSettings.noThreads.Value) { // no freeze
                    playURLThread = new Thread(new ThreadStart(PlayURL));
                    playURLThread.Start();
                } else { PlayURL(); } // yes freeze
            }
        }

        private void PlayURL() {
            try {
                MediaFoundationReader.MediaFoundationReaderSettings mediaFoundationReaderSettings = new MediaFoundationReader.MediaFoundationReaderSettings() {
                    RepositionInRead = true,
                    RequestFloatOutput = true
                };

                mediaFoundationReader = new MediaFoundationReader(currentStationURL, mediaFoundationReaderSettings);
                
                meter = new MeteringSampleProvider(mediaFoundationReader.ToSampleProvider(), 1024);
                if (GlobalRadio == this) {
                    meter.StreamVolume += (s,e) => streamSampleVolume = 0.5f*(e.MaxSampleValues[0] + e.MaxSampleValues[1]); //Console.WriteLine("{0} - {1}", e.MaxSampleValues[0],e.MaxSampleValues[1]);
                }
                
                centerSampleProvider = new VolumeSampleProvider(meter); //spatialize ? new VolumeSampleProvider(meter) : meter;
                // add audio effects?
                volumeSampleProvider = new VolumeSampleProvider(centerSampleProvider);

                if (spatialize) {
                    mediaFoundationReaderMono = new MediaFoundationReader(currentStationURL, mediaFoundationReaderSettings);

                    StereoToMonoSampleProvider stereoToMono = new StereoToMonoSampleProvider(mediaFoundationReaderMono.ToSampleProvider());
                    monoSampleProvider = new VolumeSampleProvider(stereoToMono); // panning volume
                    monoVolumeSampleProvider = new VolumeSampleProvider(monoSampleProvider); // radio volume
                    pannedMonoSampleProvider = new PanningSampleProvider(monoVolumeSampleProvider);
                    
                    radioVolume = 0f;
                    waveOutMono = new WaveOutEvent();
                    waveOutMono.Init(pannedMonoSampleProvider);
                }

                waveOut = new WaveOutEvent();
                waveOut.Init(volumeSampleProvider);
                
                waveOut.Play();
                if (waveOutMono != null) { waveOutMono.Play(); }
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

            if (GlobalRadio == this && !spatialize && !failedToLoad) {
                // metadata on separate connection, so let them run together
                _= TrackMetadata(); //StartCoroutine(metadataCoroutine);
            }
        }

        public void StopRadio() {
            //trackingMetadata = false;
            //if (metadataCoroutine != null) {  //    StopCoroutine(metadataCoroutine);  //}
            //    cts.Cancel();
            trackingMetadata = false;
            currentMetadata = "";
            currentSong = "Unknown Track";
            if (waveOut != null) { waveOut.Stop(); }
            if (waveOutMono != null) { waveOutMono.Stop(); }
        }

        public String GetStationTitle(int streamIndex = -999) {
            if (GlobalRadio != this || spatialize) { return "LocalRadio"; } // station titles are currently grabbed from settings, so throw a dummy for now if those don't match our streamURLs
            int station = streamIndex == -999 ? currentStation : streamIndex;
            
            if (station < 0 || station >= streamURLs.Count || station >= NetRadioSettings.streamTitles.Count) { return PluginName; }
            else { return NetRadioSettings.streamTitles[station]; }
        }

        public async Task GetMetadata(string url = "") {
            if (m_httpClient == null) { m_httpClient = CreateHTTPClient(); }
            if (url == "") { url = currentStationURL; }
            //cts = new CancellationTokenSource();
            string oldMetadata = currentMetadata; 
            currentMetadata = await GetMetaDataFromIceCastStream(url);
            if (currentMetadata != oldMetadata) { HandleMetadata(currentMetadata); } 
        }

        private async Task TrackMetadata() {
            if (m_httpClient == null) { m_httpClient = CreateHTTPClient(); }
            trackingMetadata = true;

            string oldMetadata = currentMetadata; 
            int oldStation = currentStation;
            oldMetadata = "";
            currentMetadata = "";
            
            while (trackingMetadata) {
                if (!string.IsNullOrWhiteSpace(currentStationURL)) { 
                    currentMetadata = await GetMetaDataFromIceCastStream(currentStationURL); 
                } 
                if (currentMetadata != oldMetadata) {
                    HandleMetadata(currentMetadata); 
                    oldMetadata = currentMetadata;
                }
                await Task.Yield(); //yield return null;
            }
            trackingMetadata = false; // cts.Cancel();
        }

        private void HandleMetadata(string originalMetadata) {
            string fallback = "Unknown Track";
            if (!string.IsNullOrWhiteSpace(originalMetadata)) {
                string[] splitMeta = originalMetadata.Split(new[]{ @"'" }, StringSplitOptions.RemoveEmptyEntries);
                string simpleMetadata = splitMeta.Length > 2 ? splitMeta[1] : fallback; //originalMetadata; 
                currentSong = simpleMetadata.Trim();
            } else { 
                currentSong = fallback; 
            }
            
            Log.LogInfo("Metadata updated for station " + GetStationTitle() + ": " + currentSong);
            NetRadioPlugin.UpdateCurrentSong();
        }

        private System.Net.Http.HttpClient CreateHTTPClient() { //private async Task<System.Net.Http.HttpClient> CreateHTTPClient() {
            var httpClient = new System.Net.Http.HttpClient();
            httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("C# App (BRC NetRadio)");
            httpClient.Timeout = TimeSpan.FromSeconds(10);
            return httpClient;
        }

        private async Task<string> GetMetaDataFromIceCastStream(string url) {
            //if (m_httpClient == null) { m_httpClient = CreateHTTPClient(); }
            if (url == null) { return null; }

            m_httpClient.DefaultRequestHeaders.Add("Icy-MetaData", "1");
            var response = await m_httpClient.GetAsync(url, System.Net.Http.HttpCompletionOption.ResponseHeadersRead);
            m_httpClient.DefaultRequestHeaders.Remove("Icy-MetaData");

            if (response.IsSuccessStatusCode) {
                IEnumerable<string> headerValues;
                if (response.Headers.TryGetValues("icy-metaint", out headerValues)) {
                    string metaIntString = headerValues.First();
                    if (!string.IsNullOrEmpty(metaIntString)) {
                        int metadataInterval = int.Parse(metaIntString);
                        byte[] buffer = new byte[metadataInterval];
                        using (var stream = await response.Content.ReadAsStreamAsync()) {
                            int numBytesRead = 0;
                            int numBytesToRead = metadataInterval;
                            while (numBytesToRead > 0) {
                                int n = stream.Read(buffer, numBytesRead, Mathf.Min(10, numBytesToRead));
                                numBytesRead += n;
                                numBytesToRead -= n;
                            }

                            int lengthOfMetaData = stream.ReadByte();
                            int metaBytesToRead = lengthOfMetaData * 16;
                            byte[] metadataBytes = new byte[metaBytesToRead];
                            var bytesRead = await stream.ReadAsync(metadataBytes, 0, metaBytesToRead);
                            string metaDataString = System.Text.Encoding.UTF8.GetString(metadataBytes);
                            return metaDataString; //return finalMeta;
                        }
                    }
                }
            }
            return null;
        }

        private void CalculateSpatial()
        {
            SetPan(GetSpatialPanning(Camera.main, this.gameObject));
            //Log.LogInfo(pan);

            // if this transform is closer to the radio, the radio gets louder
            Transform distanceListener = player.transform; // Camera.main.transform;
            float distanceBetweenVectors = Vector3.Distance(distanceListener.position, this.gameObject.transform.position);

            float volume = 1f;
            if (distanceBetweenVectors > minDistance) {
                float distanceAudio = distanceBetweenVectors - minDistance;
                //volume -= distanceAudio/maxDistance; // linear rolloff - try to implement logarithmic later
                volume -= (Mathf.Sqrt(maxDistance * distanceAudio))/maxDistance;
                volume = Mathf.Clamp01(volume);
                //volume = 1f/((maxDistance/10f)*distanceAudio);
            }
            
            radioVolume = volume*sfxVolume;
            AdjustMusicVolume(volume);
        }

        public void AdjustMusicVolume(float volume, float multiplier = 0.5f) { // make musicPlayer and GlobalRadio quieter while approaching spatialRadio 
            musicPlayer.audioSource.volume = 1 - (volume*multiplier);
            GlobalRadio.volume = 1 - (volume*multiplier);
        }

        public float GetSpatialPanning(Camera cam, GameObject audioEmitter)
        {
            if (cam == null || audioEmitter == null) { return pan; }

            Vector3 cameraForward = cam.transform.forward; //player.transform.position - cam.transform.position; // unreliable
            Vector3 directionToEmitter = audioEmitter.transform.position - cam.transform.position;

            // Project the vectors onto the camera's forward plane (ignore height or magnitude)
            directionToEmitter.y = 0f;
            cameraForward.y = 0f;
            cameraForward.Normalize();
            directionToEmitter.Normalize();

            // Rotate direction to audio emitter for dot product -> panning accuracy 
            directionToEmitter = Quaternion.AngleAxis(-90, Vector3.up) * directionToEmitter;
            // Calculate the dot product between the two normalized vectors
            float dotProduct = Vector3.Dot(cameraForward, directionToEmitter);
            dotProduct = Mathf.Clamp(dotProduct, -1f, 1f);

            // Map the dot product to a stereo panning value (-1 to 1)
            // Positive values pan to the right, negative values pan to the left
            float panning = dotProduct;
            return panning;
        }

        private void SetPan(float value) {
            pan = Mathf.Lerp(pan, value, 0.2f); // smoothing to account for sudden changes / occasional weird behavior
            if (this.pannedMonoSampleProvider != null && this.centerSampleProvider != null && this.monoSampleProvider != null) {
                this.pannedMonoSampleProvider.Pan = Mathf.Sign(pan); //= value; 
                if (this.centerSampleProvider is VolumeSampleProvider) { (this.centerSampleProvider as VolumeSampleProvider).Volume = 1f - Mathf.Abs(pan); }
                this.monoSampleProvider.Volume = Mathf.Abs(pan);
            }
        }

        // EXPERIMENTAL
        public static NetRadioManager CreateRadio(Transform parent, bool playNow = true, bool spatialize = true, List<string> clipURL = null) {
            GameObject radioHolder = new GameObject();
            NetRadioManager NetRadioManager = radioHolder.AddComponent<NetRadioManager>();
            radioHolder.transform.parent = parent;
            NetRadioManager.spatialize = spatialize; 
            NetRadioManager.gameObject.transform.position = parent.transform.position;
            if (clipURL != null) { NetRadioManager.streamURLs = clipURL; }
            if (playNow) { NetRadioManager.PlayRadioStation(0); }
            return NetRadioManager;
        }
    }
}