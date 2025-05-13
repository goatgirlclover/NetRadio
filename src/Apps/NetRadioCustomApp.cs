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

namespace NetRadio.Apps
{
    // Allow us to save overlay so we can delete it later
    // useful for making headers with playlist/track names
    // code nearly entirely copy-pasted from CommonAPI github. thanks lazy duchess!
    public class NetRadioCustomApp : CustomApp {
        public UnityEngine.Transform overlayInstance = null; 

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

        public void CreateAndSaveTitleBar(string title, Sprite icon, float fontSize = 80f) {
            var sourceApp = MyPhone.GetAppInstance<AppGraffiti>();
            var overlay = sourceApp.transform.Find("Overlay");
            var newOverlay = GameObject.Instantiate(overlay);
            var icons = newOverlay.transform.Find("Icons");
            icons.Find("GraffitiIcon").GetComponent<Image>().sprite = icon;
            var header = icons.Find("HeaderLabel");
            Component.Destroy(header.GetComponent<TMProLocalizationAddOn>());
            var tmpro = header.GetComponent<TextMeshProUGUI>();
            tmpro.text = title;
            tmpro.fontSize = fontSize;
            tmpro.fontSizeMax = fontSize;
            //tmpro.fontSizeMin = fontSize;
            newOverlay.SetParent(transform, false);

            overlayInstance = newOverlay; 
        }

        public override void OnPressUp() { 
            int currentIndex = ScrollView.SelectedIndex;
            int nextIndex = currentIndex - 1;

            if (!(nextIndex < 0 || nextIndex >= ScrollView.Buttons.Count)) { 
                if (IsHeaderButton((SimplePhoneButton)ScrollView.Buttons[nextIndex])) {
                    SilentScrollUp();
                    return;
                }
            } else { return; }
            
            base.OnPressUp();
        } 

        public override void OnPressDown() { 
            int currentIndex = ScrollView.SelectedIndex;
            int nextIndex = currentIndex + 1;
            
            if (!(nextIndex < 0 || nextIndex >= ScrollView.Buttons.Count)) { 
                if (IsHeaderButton((SimplePhoneButton)ScrollView.Buttons[nextIndex])) {
                    SilentScrollDown();
                    return;
                }
            } else { return; }
            
            base.OnPressDown();
        } 

        public void SilentScrollUp() { SilentScroll(-1); }
        public void SilentScrollDown() { SilentScroll(1); }
        
        private void SilentScroll(int direction) {
            var newIndex = Mathf.Clamp(ScrollView.SelectedIndex + direction, 0, ScrollView.Buttons.Count - 1);
            if (ScrollView.Buttons.Count <= 0 || newIndex == ScrollView.SelectedIndex) { return; }
            var currentButton = ScrollView.Buttons[newIndex];
            var rect = currentButton.RectTransform();
            var buttonHeight = rect.anchoredPosition.y;
            var buttonHeightPlusSize = -buttonHeight + (currentButton.Height / 2f) + ScrollView.Separation - ScrollView.CurrentScroll;
            if (buttonHeightPlusSize < (currentButton.Height / 2f) + ScrollView.Separation) {
                ScrollView.CancelAnimation();
                ScrollView.CurrentScroll = -buttonHeight - (currentButton.Height / 2f) - ScrollView.Separation;
                ScrollView.StartScrollAnimation();
                PlaySelectSFX();
            }
        }
    }
}