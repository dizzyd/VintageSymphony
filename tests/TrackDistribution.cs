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

        /// <summary>The curator lends Calm to the peaceful playlists, and to nothing else.</summary>
        [VsTest, RequiresClient]
        public async Task CalmIsLentToThePeacefulPlaylistsOnly()
        {
            await OnClient();

            var curator = new Engine.MusicCurator(Capi, () => new List<Situations.Scoring.SituationAssessment>(),
                new Engine.Playback(Capi.Logger, new Engine.TrackCooldownManager(() => 0L),
                    () => new TrackedPlayerProperties(), () => 0L), () => 0L);
            curator.Tracks = new List<Engine.MusicTrack> { Track("x") };

            var playlists = (Dictionary<Situations.Situation, Engine.Playlist>)typeof(Engine.MusicCurator)
                .GetField("playlists", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                .GetValue(curator)!;

            foreach (var (situation, playlist) in playlists)
            {
                var lends = situation is Idle or Adventure or Keep;
                Assert.Equal(lends ? Calm : (Situations.Situation?)null, playlist.Borrows?.Situation,
                    situation + " borrows from");
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
