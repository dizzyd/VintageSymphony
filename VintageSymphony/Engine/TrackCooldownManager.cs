namespace VintageSymphony.Engine;

public class TrackCooldownManager
{
	private class TrackCooldown
	{
		public readonly long CooldownUntil;
		public readonly MusicTrack Track;

		public TrackCooldown(long cooldownUntil, MusicTrack track)
		{
			CooldownUntil = cooldownUntil;
			Track = track;
		}
	}

	private long trackCooldownMs = 30L * 60L * 1000L;
	private long trackCooldownVarianceMs = 4L * 60L * 1000L;
	private readonly List<TrackCooldown> tracksOnCooldown = new();
	private readonly Func<long> currentTimeMs;

	public TrackCooldownManager(Func<long> currentTimeMs)
	{
		this.currentTimeMs = currentTimeMs;
	}

	public void SetCooldownDuration(long cooldownMs, long cooldownVarianceMs = 0)
	{
		trackCooldownMs = cooldownMs;
		trackCooldownVarianceMs = cooldownVarianceMs;
	}

	public void PutOnCooldown(MusicTrack musicTrack)
	{
		tracksOnCooldown.Add(new TrackCooldown(GetCooldownEndTime(musicTrack), musicTrack));
	}

	public bool IsOnCooldown(MusicTrack musicTrack)
	{
		var now = currentTimeMs();
		return tracksOnCooldown.Exists(t => t.Track == musicTrack && now < t.CooldownUntil);
	}

	public void CleanupRoutine()
	{
		var now = currentTimeMs();
		tracksOnCooldown.RemoveAll(t => now > t.CooldownUntil);
	}

	public void Remove(MusicTrack musicTrack)
	{
		tracksOnCooldown.RemoveAll(t => t.Track == musicTrack);
	}

	private long GetCooldownEndTime(MusicTrack musicTrack)
	{
		const double priorityTrackMultiplicator = 1.25;
		double multiplicator = musicTrack.Priority > 1 ? priorityTrackMultiplicator : 1;
		var cooldownDuration =
			(long)((trackCooldownMs + trackCooldownVarianceMs * Random.Shared.NextSingle()) * multiplicator);
		var cooldownUntil = currentTimeMs() + cooldownDuration;
		return cooldownUntil;
	}
}