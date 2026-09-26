using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using VsTestkit.Testing;
using static VsTestkit.Testing.Vs;
using static VintageSymphony.Situations.Situation;
using Selector = VintageSymphony.Engine.TrackSelector;

namespace VintageSymphony.Tests
{
    /// <summary>
    /// "The same few songs over and over" with the default pack. A thirty-minute cooldown
    /// against the twenty or so tracks that fit at any moment brought every track back
    /// as soon as its time was up, and Idle - two tracks on a winter afternoon - simply
    /// alternated them. These hold the shape of the replacement: a shuffle bag, Calm lent
    /// to thin peaceful playlists, a piece and its stems treated as one, an artist damped
    /// after their own track, and the default pack's missing situations filled in.
    /// </summary>
    public class TrackDistribution
    {
        // ---- the bag ----------------------------------------------------------

        /// <summary>
        /// Every draw is from the longest-unheard half of what fits, bar the track just
        /// played. With ten that fit, a track is in the bag again only once four of the
        /// other nine have played since - so no track comes back inside five draws.
        /// </summary>
        [VsTest]
        public Task NoTrackReturnsBeforeHalfTheOthersHavePlayed()
        {
            var tracks = Enumerable.Range(0, 10).Select(i => Track("t" + i)).ToList();
            var order = DrawMany(tracks, 300);

            var closest = ClosestReturn(order);
            Log("closest return over 300 draws of ten: " + closest + " draws");
            Assert.GreaterOrEqual(closest, 5, "draws before a track came back");
            Assert.Equal(10, order.Distinct().Count(), "every track played");
            return Task.CompletedTask;
        }

        /// <summary>
        /// Oldest is not the same as off cooldown: a favoured track cools a quarter longer.
        /// A (1.05) played at minute 0 cools until 37.5; B played at 5 cools until 35; C
        /// played at 35. At 36, A is the longest unheard and still cooling, B is free -
        /// and the bag used to take its oldest half first, which was A alone.
        /// </summary>
        [VsTest, RequiresClient]
        public async Task ATrackOffCooldownBeatsAnOlderOneStillCooling()
        {
            await OnClient();

            for (var round = 0; round < 20; round++)
            {
                var a = Track("a", 1.05f);
                var b = Track("b");
                var c = Track("c");

                long now = 0;
                var cooldowns = new Engine.TrackCooldownManager(() => now);
                var starts = new List<Engine.TrackStart>();
                var playback = new Engine.Playback(Capi.Logger, cooldowns,
                    () => new TrackedPlayerProperties { sunSlight = 15 }, () => now, starts.Add);
                cooldowns.SetCooldownDuration(30 * 60_000L);

                void PlayAlone(Engine.MusicTrack track, long minute)
                {
                    now = minute * 60_000L;
                    playback.Play(new Engine.Playlist(Calm, new[] { track }));
                    playback.NextTrack();
                }

                PlayAlone(a, 0);
                PlayAlone(b, 5);
                PlayAlone(c, 35);

                now = 36 * 60_000L;
                playback.Play(new Engine.Playlist(Calm, new[] { a, b, c }));
                Assert.True(cooldowns.IsOnCooldown(a), "A is still cooling at minute 36");
                Assert.False(cooldowns.IsOnCooldown(b), "B is not");
                playback.NextTrack();
                playback.StopTrack(0f);

                var start = starts.Last();
                Assert.True(start.Track == b, "round " + round + ": the draw took " + start.Track.Name);
                Assert.Equal(Engine.TrackStart.Tier.Fresh, start.How, "and the log calls it fresh");
            }
        }

        /// <summary>
        /// The whole piece at minute 4 after another song at 0, both still cooling at 8. The
        /// stems version is the same piece: it must neither follow the whole straight away
        /// nor count as rested because a different track object cooled. The other song goes
        /// round again instead.
        /// </summary>
        [VsTest, RequiresClient]
        public async Task StemsDoNotFollowTheirWholePieceEvenWhenTheRestIsCooling()
        {
            await OnClient();

            for (var round = 0; round < 20; round++)
            {
                var whole = Track("jon_algar_-_campfire_legends");
                var stems = Track("jon_algar_-_campfire_legends_stems_melody");
                var other = Track("different_song");
                var rig = new Rig();

                rig.PlayAlone(other, 0);
                rig.PlayAlone(whole, 4);
                var stemsCooling = rig.Cooldowns.IsOnCooldown(stems);

                var start = rig.DrawFrom(8, whole, stems, other);
                Assert.True(start.Track == other, "round " + round + ": after the whole piece came " + start.Track.Name);
                Assert.Equal(Engine.TrackStart.Tier.Recycled, start.How, "a pick still on cooldown");
                Assert.True(stemsCooling, "the stems cooled with their whole piece");
            }
        }

        /// <summary>When one piece is all that fits, its other version still plays rather than nothing.</summary>
        [VsTest, RequiresClient]
        public async Task ThePiecesOtherVersionPlaysWhenItIsAllThatFits()
        {
            await OnClient();

            var whole = Track("jon_algar_-_campfire_legends");
            var stems = Track("jon_algar_-_campfire_legends_stems_melody");
            var rig = new Rig();

            rig.PlayAlone(whole, 0);
            var start = rig.DrawFrom(4, whole, stems);
            Assert.True(start.Track == stems, "the other version, not the track just played: " + start.Track.Name);
        }

        /// <summary>
        /// A recycled pick can sit beside a track off cooldown: the one just played, when it
        /// opts out of cooldowns. The start must say that the pick was cooling without
        /// claiming everything that fit was.
        /// </summary>
        [VsTest, RequiresClient]
        public async Task ARecycledPickBesideARestedPreviousTrackIsExplained()
        {
            await OnClient();

            var free = Track("free");
            free.DisableCooldown = true;
            var cooling = Track("cooling");
            var rig = new Rig();

            rig.PlayAlone(cooling, 0);
            rig.PlayAlone(free, 1);
            var start = rig.DrawFrom(2, free, cooling);

            Assert.True(start.Track == cooling, "the other track, not the one just played");
            Assert.Equal(Engine.TrackStart.Tier.Recycled, start.How, "the pick was still cooling");
            Assert.Equal(2, start.Fit, "tracks that fit");
            Assert.Equal(1, start.OffCooldown, "one of them off cooldown - the one just played");

            var line = Engine.TrackHistoryLog.Describe(start, 1, DateTime.Now);
            Log(line);
            Assert.True(line.Contains("fit 2 of 2, 1 off cooldown; "), "the counts, then a reason: " + line);
        }

        /// <summary>
        /// When everything but the track just played is cooling, the draw goes round again
        /// rather than falling silent, and the log says the pick was recycled.
        /// </summary>
        [VsTest, RequiresClient]
        public async Task AllCoolingIsRecycledAndSaysSo()
        {
            await OnClient();

            var a = Track("a");
            var b = Track("b");
            long now = 0;
            var cooldowns = new Engine.TrackCooldownManager(() => now);
            cooldowns.SetCooldownDuration(30 * 60_000L);
            var starts = new List<Engine.TrackStart>();
            var playback = new Engine.Playback(Capi.Logger, cooldowns,
                () => new TrackedPlayerProperties { sunSlight = 15 }, () => now, starts.Add);
            playback.Play(new Engine.Playlist(Calm, new[] { a, b }));

            playback.NextTrack();
            now += 3 * 60_000L;
            playback.NextTrack();
            now += 3 * 60_000L;
            playback.NextTrack();
            playback.StopTrack(0f);

            Assert.Equal(Engine.TrackStart.Tier.Fresh, starts[1].How, "the second track had not played");
            Assert.Equal(Engine.TrackStart.Tier.Recycled, starts[2].How, "the third start came round again");
            Assert.True(starts[2].Track == starts[0].Track, "to the first track, not the one just played");
        }

        /// <summary>
        /// A track at priority 3 wins every draw it is in, as it always has - but once
        /// played it is at the back of the bag, and has to wait its turn like the rest.
        /// Before, it won every draw outright and played back to back with itself bar one.
        /// </summary>
        [VsTest]
        public Task AFavouredTrackWinsItsTurnsButStillWaitsForThem()
        {
            var tracks = Enumerable.Range(0, 9).Select(i => Track("t" + i)).ToList();
            var favoured = Track("favoured", 3f);
            tracks.Add(favoured);

            var order = DrawMany(tracks, 300);
            var share = order.Count(n => n == favoured.Name) / 300.0;
            Log("share taken by the priority-3 track: " + share.ToString("P1"));

            Assert.Greater(share, 0.15, "it still wins whenever it is in the bag");
            Assert.LessOrEqual(share, 0.2, "and is in the bag at most one draw in five");
            return Task.CompletedTask;
        }

        /// <summary>
        /// Campfire Legends ships whole and as stems. Hearing one after the other is
        /// hearing the same piece twice, so they share one listening history.
        /// </summary>
        [VsTest]
        public Task APieceAndItsStemsAreHeardAsOne()
        {
            Assert.Equal(Selector.PieceOf(Track("jon_algar_-_campfire_legends")),
                Selector.PieceOf(Track("jon_algar_-_campfire_legends_stems_melody")),
                "the stems are the same piece");
            Assert.NotEqual(Selector.PieceOf(Track("jon_algar_-_campfire_legends")),
                Selector.PieceOf(Track("jon_algar_-_desert_wind")),
                "another piece by the same composer is not");

            var tracks = Enumerable.Range(0, 8).Select(i => Track("t" + i)).ToList();
            tracks.Add(Track("jon_algar_-_campfire_legends"));
            tracks.Add(Track("jon_algar_-_campfire_legends_stems_melody"));

            // Either version of the piece counts as the piece playing.
            var order = DrawMany(tracks, 300)
                .Select(n => n.Contains("campfire") ? "campfire" : n)
                .ToList();
            var closest = ClosestReturn(order, only: "campfire");
            Log("closest the piece came round again: " + closest + " draws");
            Assert.GreaterOrEqual(closest, 4, "draws between one version of the piece and the other");
            return Task.CompletedTask;
        }

        /// <summary>
        /// Twenty-three of the default pack's peaceful tracks are one composer's. After one
        /// of them, another of theirs is left a third of its weight - when there is anyone
        /// else to play, and not otherwise.
        /// </summary>
        [VsTest]
        public Task TheSameArtistIsDampedAfterThemselves()
        {
            var previous = Track("a0", artist: "Jon Algar");
            var theirs = Track("a1", artist: "Jon Algar");
            var someoneElse = Track("b1", artist: "Christophe Gorman");

            var theirShare = Enumerable.Range(0, 4000)
                .Count(_ => Selector.Select(new[] { theirs, someoneElse }, previous) == theirs) / 4000.0;
            Log("share to the same artist again, one track each: " + theirShare.ToString("P1"));
            Assert.Greater(theirShare, 0.2, "damped, not barred");
            Assert.Less(theirShare, 0.32, "to about 0.35 / 1.35");

            Assert.True(Selector.Select(new[] { theirs }, previous) == theirs,
                "with nobody else to play, their track plays");
            return Task.CompletedTask;
        }

        /// <summary>
        /// Below 1 the weight is plain, so a pack's 0.5 means half as often - not never,
        /// which is what the steep weight above 1 would make it.
        /// </summary>
        [VsTest]
        public Task APriorityBelowOneIsHalfAsLikelyNotNever()
        {
            var half = Track("half", 0.5f);
            var normal = Track("normal");
            var share = Enumerable.Range(0, 4000)
                .Count(_ => Selector.Select(new[] { half, normal }) == half) / 4000.0;
            Log("share to the 0.5 track against one at 1: " + share.ToString("P1"));
            Assert.Greater(share, 0.28, "about a third");
            Assert.Less(share, 0.39, "about a third");
            return Task.CompletedTask;
        }

        // ---- borrowing --------------------------------------------------------

        /// <summary>
        /// Idle on a winter afternoon: two tracks. The playlist takes Calm's that fit too,
        /// and says so in the history line.
        /// </summary>
        [VsTest, RequiresClient]
        public async Task AThinIdlePlaylistBorrowsFromCalm()
        {
            await OnClient();

            var idle = new[] { Track("idle-a"), Track("idle-b") };
            var calmTracks = Enumerable.Range(0, 10).Select(i => Track("calm" + i)).ToList();
            var calm = new Engine.Playlist(Calm, calmTracks);
            var idlePlaylist = new Engine.Playlist(Idle, idle) { Borrows = calm };

            var starts = new List<Engine.TrackStart>();
            long now = 1_000_000L;
            var playback = new Engine.Playback(Capi.Logger, new Engine.TrackCooldownManager(() => now),
                () => new TrackedPlayerProperties { sunSlight = 15 }, () => now, starts.Add);
            playback.Play(idlePlaylist);

            for (var i = 0; i < 40; i++)
            {
                now += 5 * 60_000L;
                playback.NextTrack();
            }

            playback.StopTrack(0f);

            Assert.Equal(40, starts.Count, "tracks started");
            var distinct = starts.Select(s => s.Track.Name).Distinct().Count();
            var fromIdle = starts.Count(s => idle.Contains(s.Track));
            Log($"distinct {distinct} of 12, {fromIdle} from Idle's own two; first line: "
                + Engine.TrackHistoryLog.Describe(starts[0], 1, DateTime.Now));

            Assert.Equal(12, starts[0].Fit, "Idle's two and Calm's ten fit");
            Assert.Equal(10, starts[0].Borrowed, "ten of them borrowed");
            Assert.Greater(distinct, 8, "the draw spread over the borrowed tracks");
            Assert.Greater(fromIdle, 0, "Idle's own tracks still play");
        }

        /// <summary>A playlist with enough of its own does not borrow.</summary>
        [VsTest, RequiresClient]
        public async Task APlaylistWithEnoughOfItsOwnDoesNotBorrow()
        {
            await OnClient();

            var own = Enumerable.Range(0, 8).Select(i => Track("own" + i)).ToList();
            var calm = new Engine.Playlist(Calm, new[] { Track("calm-only") });
            var playlist = new Engine.Playlist(Idle, own) { Borrows = calm };

            var starts = new List<Engine.TrackStart>();
            long now = 1_000_000L;
            var playback = new Engine.Playback(Capi.Logger, new Engine.TrackCooldownManager(() => now),
                () => new TrackedPlayerProperties { sunSlight = 15 }, () => now, starts.Add);
            playback.Play(playlist);
            for (var i = 0; i < 20; i++)
            {
                now += 5 * 60_000L;
                playback.NextTrack();
            }

            playback.StopTrack(0f);

            Assert.True(starts.All(s => s.Borrowed == 0), "nothing borrowed");
            Assert.False(starts.Any(s => s.Track.Name == "calm-only"), "the lender's track never played");
        }

        /// <summary>
        /// Through the curator, as the engine runs it: standing still with two Idle tracks
        /// and ten Calm ones, the Calm ones play too. In a fight with two fight tracks,
        /// they do not - a thin fight playlist is not to be padded with peaceful music.
        /// </summary>
        [VsTest, RequiresClient]
        public async Task OnlyThePeacefulSituationsReachForCalm()
        {
            await OnClient();

            var idleTracks = new[] { Track("idle-a", situation: "idle"), Track("idle-b", situation: "idle") };
            var fightTracks = new[] { Track("fight-a", situation: "fight"), Track("fight-b", situation: "fight") };
            var calmTracks = Enumerable.Range(0, 10).Select(i => Track("calm" + i)).ToList();

            foreach (var (leader, own) in new[] { (Idle, idleTracks), (Fight, fightTracks) })
            {
                long now = 1_000_000L;
                var starts = new List<Engine.TrackStart>();
                var playback = new Engine.Playback(Capi.Logger, new Engine.TrackCooldownManager(() => now),
                    () => new TrackedPlayerProperties { sunSlight = 15 }, () => now, starts.Add);
                var ranked = Enum.GetValues<Situations.Situation>()
                    .Select(s => new Situations.Scoring.SituationAssessment(s, s == leader ? 1f : 0f))
                    .OrderByDescending(a => a.WeightedScore).ToList();
                var curator = new Engine.MusicCurator(Capi, () => ranked, playback, () => now);
                curator.Tracks = idleTracks.Concat(fightTracks).Concat(calmTracks).ToList();

                curator.Update(1f);
                Assert.Equal(leader, playback.CurrentPlaylist?.Situation, "the playlist the curator chose");
                for (var i = 0; i < 30; i++)
                {
                    now += 5 * 60_000L;
                    playback.NextTrack();
                }

                playback.StopTrack(0f);

                var fromCalm = starts.Count(s => calmTracks.Contains(s.Track));
                Log($"{leader}: {starts.Count} starts, {fromCalm} of them Calm's");
                if (leader == Idle)
                {
                    Assert.Greater(fromCalm, 10, "Calm's tracks played while idle");
                    Assert.Greater(starts.Count(s => own.Contains(s.Track)), 0, "and Idle's own still did");
                }
                else
                {
                    Assert.Equal(0, fromCalm, "Calm's tracks played during the fight");
                }
            }
        }

        // ---- the default pack's data ------------------------------------------

        [VsTest]
        public Task TheDefaultPacksUntaggedTracksGetSituations()
        {
            Assert.Equal("calm|idle", DefaultPackCorrections.Correct("deskant_-_crying_winds", new Situations.Situation[0]),
                "Crying Winds");
            Assert.Equal("calm|idle|adventure", DefaultPackCorrections.Correct("jon_algar_-_natures_way", new Situations.Situation[0]),
                "Nature's Way");
            Assert.Null(DefaultPackCorrections.Correct("someone_-_else", new Situations.Situation[0]),
                "a track the table does not know stays as it is");
            return Task.CompletedTask;
        }

        [VsTest]
        public Task CalmTracksAlsoPlayWhenIdle()
        {
            Assert.Equal("calm|idle", DefaultPackCorrections.Correct("x", new[] { Calm }), "calm alone");
            Assert.Equal("calm|adventure|idle", DefaultPackCorrections.Correct("x", new[] { Calm, Adventure }),
                "calm and adventure");
            Assert.Null(DefaultPackCorrections.Correct("x", new[] { Calm, Idle }), "already both");
            Assert.Null(DefaultPackCorrections.Correct("x", new[] { Fight }), "fight music is left alone");
            Assert.Null(DefaultPackCorrections.Correct("x", new[] { Adventure }), "so is adventure alone");
            return Task.CompletedTask;
        }

        /// <summary>Only the default pack's tracks are touched; anyone else's say what they mean.</summary>
        [VsTest]
        public Task OnlyTheDefaultPackIsCorrected()
        {
            var ours = Track("deskant_-_crying_winds", domain: "vintagesymphony", situation: "");
            var theirs = Track("deskant_-_crying_winds", domain: "somepack", situation: "");
            var calmOurs = Track("silent_river", domain: "vintagesymphony", situation: "calm");
            var calmTheirs = Track("silent_river", domain: "somepack", situation: "calm");

            Assert.Equal(2, DefaultPackCorrections.Apply(new[] { ours, theirs, calmOurs, calmTheirs }), "tracks changed");
            Assert.Equal("calm|idle", ours.Situation, "the default pack's untagged track");
            Assert.Equal(2, ours.TrackSituations.Length, "and its playlists follow");
            Assert.Equal("", theirs.Situation, "another pack's untagged track");
            Assert.Equal("calm", calmTheirs.Situation, "another pack's calm track");
            return Task.CompletedTask;
        }

        // ---- helpers ----------------------------------------------------------

        static List<string> DrawMany(List<Engine.MusicTrack> tracks, int draws)
        {
            var history = new Engine.ListeningHistory();
            var order = new List<string>();
            Engine.MusicTrack previous = null;
            for (var i = 0; i < draws; i++)
            {
                var pick = Selector.Draw(tracks, history.LastHeard, _ => false, previous);
                Assert.NotNull(pick, "a track was drawn");
                history.Heard(pick, i);
                order.Add(pick.Name);
                previous = pick;
            }

            return order;
        }

        static int ClosestReturn(List<string> order, string only = null)
        {
            var closest = int.MaxValue;
            var seen = new Dictionary<string, int>();
            for (var i = 0; i < order.Count; i++)
            {
                if (seen.TryGetValue(order[i], out var at) && (only == null || order[i] == only))
                {
                    closest = Math.Min(closest, i - at);
                }

                seen[order[i]] = i;
            }

            return closest;
        }

        /// <summary>A Playback on a fake clock, with a thirty-minute cooldown and no variance.</summary>
        class Rig
        {
            long now;
            public readonly Engine.TrackCooldownManager Cooldowns;
            readonly Engine.Playback playback;
            readonly List<Engine.TrackStart> starts = new();

            public Rig()
            {
                Cooldowns = new Engine.TrackCooldownManager(() => now);
                Cooldowns.SetCooldownDuration(30 * 60_000L);
                playback = new Engine.Playback(Capi.Logger, Cooldowns,
                    () => new TrackedPlayerProperties { sunSlight = 15 }, () => now, starts.Add);
            }

            public void PlayAlone(Engine.MusicTrack track, long minute) => DrawFrom(minute, track);

            public Engine.TrackStart DrawFrom(long minute, params Engine.MusicTrack[] tracks)
            {
                now = minute * 60_000L;
                var before = starts.Count;
                playback.Play(new Engine.Playlist(Calm, tracks));
                playback.NextTrack();
                playback.StopTrack(0f);
                Assert.Equal(before + 1, starts.Count, "a track started at minute " + minute);
                return starts[^1];
            }
        }

        static Engine.MusicTrack Track(string name, float priority = 1f, string artist = null,
            string domain = "vstestpack", string situation = "calm")
        {
            var track = new SilentTrack
            {
                Location = new AssetLocation(domain, "music/" + name + ".ogg"),
                Priority = priority,
                MinSunlight = 0,
                Artist = artist,
            };
            track.SetSituation(situation);
            return track;
        }

        /// <summary>Eligible everywhere, and makes no sound: the draw is what is on trial.</summary>
        class SilentTrack : Engine.MusicTrack
        {
            public override void BeginPlay(TrackedPlayerProperties props) { }
        }
    }
}
