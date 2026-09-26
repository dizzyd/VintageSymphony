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

    /// <summary>
    /// Write every track start to track-history.log in the mod's ModData folder, with
    /// how many tracks fit the moment and how many of those were off cooldown. For the
    /// player who hears the same few songs and wants to know whether it is the pool, the
    /// rules or the cooldown doing it. On by default: the file is capped, so it costs
    /// nothing to have, and the question is always asked about music that has already
    /// played.
    /// </summary>
    public bool LogTrackHistory = true;
}
