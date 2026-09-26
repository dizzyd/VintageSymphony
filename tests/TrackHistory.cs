using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.Client.NoObf;
using VsTestkit.Testing;
using static VsTestkit.Testing.Vs;
using VS = VintageSymphony.VintageSymphony;
using static VintageSymphony.Situations.Situation;

namespace VintageSymphony.Tests
{
    /// <summary>
    /// The history log is what a player sends with "it keeps playing the same songs",
    /// so what it says has to be enough to answer them: what played, how often, how many
    /// tracks fit the moment and how many of those were off cooldown. The lines are
    /// plain logic on a temp file; that a real track start reaches the real file, under
    /// the real switch, is the part only the game can prove.
    /// </summary>
    public class TrackHistory
    {
        [VsTest]
        public async Task ALineSaysWhatPlayedAndWhyItCouldRepeat()
        {
            await Ticks(1);
            Engine.TrackHistoryLog.Forget();

            var path = Path.Combine(Path.GetTempPath(), "vs-track-history-" + Guid.NewGuid() + ".log");
            var wanted = false;
            var at = new DateTime(2026, 9, 6, 14, 3, 11);
            var log = new Engine.TrackHistoryLog(path, () => wanted, null, () => at);

            var hearth = new Engine.MusicTrack { Location = new AssetLocation("vstestpack", "hearth") };
            hearth.Title = "By the Hearth";

            try
            {
                log.Record(new Engine.TrackStart(hearth, Calm, 12, 3, 2, Engine.TrackStart.Tier.Fresh));
                Assert.False(File.Exists(path), "a file written with the log switched off");

                wanted = true;
                log.Note("world joined; 12 tracks in the pool");
                log.Record(new Engine.TrackStart(hearth, Calm, 12, 3, 2, Engine.TrackStart.Tier.Fresh));
                log.Record(new Engine.TrackStart(hearth, Idle, 12, 1, 0, Engine.TrackStart.Tier.OnlyFit));

                var lines = File.ReadAllLines(path);
                Assert.Equal(3, lines.Length, "lines written");
                Assert.Equal("2026-09-06 14:03:11  -- world joined; 12 tracks in the pool", lines[0], "the note");
                Assert.Equal(
                    "2026-09-06 14:03:11  Calm          By the Hearth  [vstestpack:hearth]  play #1 this sitting  fit 3 of 12, 2 off cooldown",
                    lines[1], "a fresh draw");
                Assert.Equal(
                    "2026-09-06 14:03:11  Idle          By the Hearth  [vstestpack:hearth]  play #2 this sitting  fit 1 of 12, 0 off cooldown; the only track that fit",
                    lines[2], "the same track again, with the reason");

                // The count is the sitting's, not this log's: a rejoined world carries on.
                new Engine.TrackHistoryLog(path, () => true, null, () => at)
                    .Record(new Engine.TrackStart(hearth, Calm, 12, 3, 0, Engine.TrackStart.Tier.Recycled));
                var last = File.ReadAllLines(path).Last();
                Assert.True(last.Contains("play #3 this sitting"), "the third play counted across logs: " + last);
                Assert.True(last.EndsWith("; everything that fit was on cooldown"), "the recycled reason: " + last);
            }
            finally
            {
                Engine.TrackHistoryLog.Forget();
                if (File.Exists(path)) File.Delete(path);
            }
        }

        /// <summary>
        /// A log nobody trims is a disk that fills. Past the limit the oldest lines go,
        /// whole lines only, and enough of them that the next trim is a long way off.
        /// </summary>
        [VsTest]
        public async Task TheFileIsTrimmedToItsNewestHalfPastTheLimit()
        {
            await Ticks(1);
            Engine.TrackHistoryLog.Forget();

            var path = Path.Combine(Path.GetTempPath(), "vs-track-history-" + Guid.NewGuid() + ".log");
            const long limit = 4000;
            var log = new Engine.TrackHistoryLog(path, () => true, null, () => new DateTime(2026, 9, 6), limit);
            var hearth = new Engine.MusicTrack { Location = new AssetLocation("vstestpack", "hearth") };
            hearth.Title = "By the Hearth";

            try
            {
                long largest = 0;
                for (var i = 0; i < 200; i++)
                {
                    log.Record(new Engine.TrackStart(hearth, Calm, 12, 3, 2, Engine.TrackStart.Tier.Fresh));
                    largest = Math.Max(largest, new FileInfo(path).Length);
                }

                var lines = File.ReadAllLines(path);
                var lineBytes = File.ReadAllBytes(path).Length / (double)lines.Length;
                Log("lines kept: " + lines.Length + ", largest size seen: " + largest + ", final: " + new FileInfo(path).Length);

                // Never far past the limit: one line's worth, at most.
                Assert.True(largest <= limit + lineBytes + 1, "the file grew to " + largest + " against a limit of " + limit);
                // Cut back to about half, not to nothing and not by a line at a time.
                Assert.True(new FileInfo(path).Length <= limit, "still over the limit after trimming");
                Assert.Greater(lines.Length, 10, "lines kept after a trim");
                Assert.True(lines.All(l => l.StartsWith("2026-09-06")), "every line that remains is whole");
                // Newest survive, oldest go.
                Assert.True(lines.Last().Contains("play #200 this sitting"), "the newest line is the last one: " + lines.Last());
                Assert.False(lines.Any(l => l.Contains("play #1 this sitting")), "the oldest line was dropped");
            }
            finally
            {
                Engine.TrackHistoryLog.Forget();
                if (File.Exists(path)) File.Delete(path);
            }
        }

        /// <summary>
        /// The wiring: with the switch on, a track the engine starts lands in the file the
        /// engine says it writes to, with the playlist it was drawn from.
        /// </summary>
        [VsTest(TimeoutMs = 120000), RequiresClient]
        public async Task ATrackStartReachesTheFileWhenAskedFor()
        {
            await OnClient();

            var engine = VS.MusicEngine;
            var curator = Curator(engine);
            var wasMusicLevel = ClientSettings.MusicLevel;
            var wasLogging = VS.Configuration.LogTrackHistory;
            var path = engine.TrackHistoryPath;
            var linesBefore = File.Exists(path) ? File.ReadAllLines(path).Length : 0;

            try
            {
                VS.Configuration.LogTrackHistory = true;
                curator.Tracks = VanillaTracks().OfType<SurfaceMusicTrack>().Select(OpenedUp).ToList();
                ClientSettings.MusicLevel = 20;

                await Until(() => engine.Playback.CurrentPlaylist != null
                                  && engine.Playback.CurrentPlaylist.Situation != Silence, 1500,
                    "the curator picks a playlist with music");
                engine.NextTrack();
                await Until(() => engine.CurrentMusicTrack?.IsPlaying == true, 900, "a track is playing");
                var track = engine.CurrentMusicTrack;
                var playlist = engine.Playback.CurrentPlaylist.Situation;

                Assert.True(File.Exists(path), "the history file exists at " + path);
                var fresh = File.ReadAllLines(path).Skip(linesBefore).ToList();
                Log(string.Join("\n", fresh));
                var line = fresh.LastOrDefault(l => l.Contains("[" + track.Location + "]"));
                Assert.NotNull(line, "a line for " + track.Location);
                Assert.True(line.Contains(" " + playlist + " "), "the line names the playlist " + playlist + ": " + line);
                Assert.True(line.Contains(" of " + curator.Tracks.Count + ","), "the line counts the playlist: " + line);
            }
            finally
            {
                engine.Playback.StopTrack(0f);
                ClientSettings.MusicLevel = wasMusicLevel;
                VS.Configuration.LogTrackHistory = wasLogging;
                curator.Tracks = new List<Engine.MusicTrack>();
            }
        }

        static Engine.MusicTrack OpenedUp(SurfaceMusicTrack vanilla)
        {
            var path = vanilla.Location.Path;
            path = path.Substring("music/".Length, path.Length - "music/".Length - ".ogg".Length);

            var track = new Engine.MusicTrack
            {
                Location = new AssetLocation(vanilla.Location.Domain, path),
                Situation = string.Join("|", Enum.GetValues<Situations.Situation>()
                    .Where(s => s != Silence).Select(s => s.ToString())),
                MinSunlight = 0
            };

            track.Initialize(Capi.Assets, Capi, Vanilla());
            return track;
        }

        static Engine.MusicCurator Curator(Engine.MusicEngine engine)
        {
            var curator = typeof(Engine.MusicEngine)
                .GetField("musicCurator", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.GetValue(engine) as Engine.MusicCurator;
            Assert.NotNull(curator, "music curator");
            return curator;
        }

        static SystemMusicEngine Vanilla() =>
            VS.ClientMain.clientSystems.OfType<SystemMusicEngine>().First();

        static IMusicTrack[] VanillaTracks() =>
            typeof(SystemMusicEngine)
                .GetField("shuffledTracks", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.GetValue(Vanilla()) as IMusicTrack[];
    }
}
