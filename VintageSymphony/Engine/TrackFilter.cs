using Vintagestory.API.Client;
using VintageSymphony.Config;

namespace VintageSymphony.Engine;

/// <summary>
/// Decides which of the game's tracks this engine will play. Music from a configured
/// source is matched by its asset domain rather than by the mod id: the domain belongs to
/// the music, and stays the same however this mod is named.
/// </summary>
public class TrackFilter
{
    private readonly Configuration configuration;
    private readonly IReadOnlyCollection<string> musicDomains;

    public TrackFilter(Configuration configuration, IReadOnlyCollection<string> musicDomains)
    {
        this.configuration = configuration;
        this.musicDomains = musicDomains;
    }

    public bool KeepTrack(IMusicTrack track)
    {
        if (track is CaveMusicTrack)
        {
            return configuration.LoadCaveTrack;
        }

        if (track is not SurfaceMusicTrack surfaceTrack)
        {
            return false;
        }

        // The game's own music is a source too - its domain is "game" - so one rule covers
        // everything: is this track's source switched on?
        return musicDomains.Contains(surfaceTrack.Location.Domain);
    }
}
