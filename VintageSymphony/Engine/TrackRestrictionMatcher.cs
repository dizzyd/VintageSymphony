using System.Runtime.CompilerServices;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;

namespace VintageSymphony.Engine;

public class TrackRestrictionMatcher
{
    private readonly IClientGameCalendar calendar;

    public TrackRestrictionMatcher(IClientGameCalendar calendar)
    {
        this.calendar = calendar;
    }

    public bool IsWithinConfiguredRestrictions(MusicTrack musicTrack, TrackedPlayerProperties props, ClimateCondition conds, BlockPos pos)
    {
        return IsHourInRange(calendar.HourOfDay, musicTrack.MinHour, musicTrack.MaxHour)
               && IsBetween((float)Math.Abs(calendar.OnGetLatitude(pos.Z)), musicTrack.MinLatitude,
                   musicTrack.MaxLatitude)
               && IsSeasonInRange(calendar.GetSeasonRel(pos), musicTrack.MinSeason, musicTrack.MaxSeason)
               && IsBetween(conds.Temperature, musicTrack.MinTemperature, musicTrack.MaxTemperature)
               && IsBetween(conds.WorldGenTemperature, musicTrack.MinWorldGenTemperature, musicTrack.MaxWorldGenTemperature)
               && conds.Rainfall >= musicTrack.MinRainFall
               && IsBetween(conds.WorldgenRainfall, musicTrack.MinWorldGenRainfall, musicTrack.MaxWorldGenRainfall)
               && IsBetween(props.sunSlight, musicTrack.MinSunlight, musicTrack.MaxSunlight)
               && IsBetween(props.DayLight, musicTrack.MinDaylight, musicTrack.MaxDaylight)
               && props.DistanceToSpawnPoint >= musicTrack.DistanceToSpawnPoint;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool IsBetween(float value, float min, float max)
    {
        return value >= min && value <= max;
    }
	
    /// <summary>
    /// Seasons wrap at the turn of the year the way hours wrap at midnight, so a winter
    /// track is written 0.85 to 0.25. A plain between test can never pass for that - no
    /// season is both above 0.85 and below 0.25 - and the pack's seven winter tracks
    /// never played at all. The game's own check has the same flaw; this is ours.
    /// </summary>
    public static bool IsSeasonInRange(float season, float minSeason, float maxSeason)
    {
        if (minSeason <= maxSeason)
        {
            return season >= minSeason && season <= maxSeason;
        }

        return season >= minSeason || season <= maxSeason;
    }

    private bool IsHourInRange(float hourOfDay, float minHour, float maxHour)
    {
        if (minHour <= maxHour)
        {
            // Simple case: range does not cross midnight
            return hourOfDay >= minHour && hourOfDay < maxHour;
        }

        // Range crosses midnight
        return hourOfDay >= minHour || hourOfDay < maxHour;
		
    }
}