using Newtonsoft.Json;

namespace VintageSymphony.Config;

[JsonObject(MemberSerialization.Fields)]
public class Configuration
{
    public bool InitialConfigurationShown = false;
    public float GlobalVolume = 1f;
    public bool LoadCaveTrack = true;

    /// <summary>
    /// The game marks each of its own tracks for survival, creative or both, and plays a
    /// track only in the mode it was marked for. Off, which is the default, lets the
    /// mod's engine draw from all of them in either mode - the way it did before the
    /// game's other rules for its own music (villages, hours, temporal stability) were
    /// honoured. On, the split is kept.
    /// </summary>
    public bool HonourGamePlaylists = false;

    /// <summary>
    /// Say in chat what has started playing, with the artist when the pack names one.
    /// Once per track per game session, so it credits rather than nags.
    /// </summary>
    public bool AnnounceTracks = true;
}
