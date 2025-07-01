namespace NetRadio;
public class MusicCuratorHelper {
    public static void SetupNetRadioIntegration() {
        NetRadioPlugin.Instance.NewTrack += (a, b) => MusicCurator.MusicCuratorPlugin.SetTrackPopupText(b.SongName);
    }
}