using System;
using System.Threading.Tasks;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using VsTestkit.Testing;
using static VsTestkit.Testing.Vs;
using Matcher = VintageSymphony.Engine.TrackRestrictionMatcher;

namespace VintageSymphony.Tests
{
    /// <summary>
    /// A season window can run across the turn of the year - the pack writes its winter
    /// tracks as 0.85 to 0.25 - and the restriction check used to be a plain between
    /// test, which no season passes when the minimum is above the maximum. Seven winter
    /// tracks in the default pack could never play because of it.
    /// </summary>
    public class SeasonWindows
    {
        [VsTest]
        public Task AWindowAcrossTheNewYearHoldsBothEnds()
        {
            // Winter, written the way the pack writes it.
            Assert.True(Matcher.IsSeasonInRange(0.95f, 0.85f, 0.25f), "December is in winter");
            Assert.True(Matcher.IsSeasonInRange(0.05f, 0.85f, 0.25f), "January is in winter");
            Assert.True(Matcher.IsSeasonInRange(0.85f, 0.85f, 0.25f), "the start is inclusive");
            Assert.True(Matcher.IsSeasonInRange(0.25f, 0.85f, 0.25f), "the end is inclusive");
            Assert.False(Matcher.IsSeasonInRange(0.5f, 0.85f, 0.25f), "July is not in winter");

            // An ordinary window is unchanged.
            Assert.True(Matcher.IsSeasonInRange(0.5f, 0.25f, 0.75f), "July is in summer");
            Assert.False(Matcher.IsSeasonInRange(0.05f, 0.25f, 0.75f), "January is not in summer");
            Assert.True(Matcher.IsSeasonInRange(0.3f, 0f, 1f), "the default window is the whole year");
            return Task.CompletedTask;
        }

        /// <summary>
        /// The same through the real calendar: a winter track and a summer track, and two
        /// dates half a year apart. Which date is winter depends on the hemisphere the test
        /// world puts the player in, so this asserts that the tracks follow the season the
        /// calendar reports rather than naming the date.
        /// </summary>
        [VsTest(TimeoutMs = 60000), RequiresClient]
        public async Task WinterTracksPlayInWinterAndSummerTracksInSummer()
        {
            await OnServer();
            var calendar = Sapi.World.Calendar;
            var wasHours = calendar.TotalHours;
            var hoursPerYear = (double)calendar.DaysPerYear * calendar.HoursPerDay;
            var yearStart = Math.Floor(wasHours / hoursPerYear) * hoursPerYear;
            await OnClient();

            var winter = Track(0.85f, 0.25f);
            // Between them the two windows cover the year, so whatever season a date lands
            // on, exactly one of them should take it.
            var summer = Track(0.25f, 0.85f);
            var matcher = new Matcher(Capi.World.Calendar);
            var props = new TrackedPlayerProperties { sunSlight = 15, DayLight = 1f };

            var winters = 0;
            try
            {
                foreach (var yearRel in new[] { 0.05, 0.55 })
                {
                    // Midday, so nothing about the hour is in question.
                    var target = yearStart + yearRel * hoursPerYear + 12;
                    await SetCalendarTo(target);
                    await Until(() => Math.Abs(Capi.World.Calendar.TotalHours - target) < 0.5, 200,
                        "the client has the new date");

                    var pos = Capi.World.Player.Entity.Pos.AsBlockPos;
                    var climate = Capi.World.BlockAccessor.GetClimateAt(pos);
                    var season = Capi.World.Calendar.GetSeasonRel(pos);
                    var isWinter = season >= 0.85f || season <= 0.25f;
                    if (isWinter) winters++;
                    Log($"year {yearRel:0.00}: season {season:0.000}, winter={isWinter}");

                    Assert.Equal(isWinter, matcher.IsWithinConfiguredRestrictions(winter, props, climate, pos),
                        $"the winter track at season {season:0.000}");
                    Assert.Equal(!isWinter, matcher.IsWithinConfiguredRestrictions(summer, props, climate, pos),
                        $"the rest-of-year track at season {season:0.000}");
                }

                // Half a year apart, one of the two dates is winter in either hemisphere -
                // and that is the date the wrap is on trial.
                Assert.Equal(1, winters, "dates that fell in winter");
            }
            finally
            {
                await SetCalendarTo(wasHours);
            }
        }

        static Engine.MusicTrack Track(float minSeason, float maxSeason) => new()
        {
            Location = new AssetLocation("vstestpack", "season"),
            MinSunlight = 0,
            MinSeason = minSeason,
            MaxSeason = maxSeason,
        };

        static async Task SetCalendarTo(double totalHours)
        {
            await OnServer();
            await World.SetCalendarTo(totalHours);
            await OnClient();
        }
    }
}
