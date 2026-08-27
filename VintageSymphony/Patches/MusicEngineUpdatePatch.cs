using HarmonyLib;
using Vintagestory.API.Client;
using Vintagestory.Client.NoObf;

// ReSharper disable UnusedType.Global
// ReSharper disable UnusedMember.Local
// ReSharper disable InconsistentNaming

namespace VintageSymphony.Patches;

[HarmonyPatch(typeof(SystemMusicEngine), "OnEverySecond")]
public class MusicEngineUpdatePatch
{
    static bool Prefix(float dt,
        SystemMusicEngine __instance,
        IMusicTrack[] ___shuffledTracks)
    {
        // __instance is the game's own IMusicEngine, which is what actually loads a sound
        // for a track - tracks we build ourselves need it too.
        VintageSymphony.MusicEngine?.LoadTracks(___shuffledTracks, __instance);
        return false;
    }
}
