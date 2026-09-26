namespace VintageSymphony.Engine;

public class TrackCooldownManager
{
	/// <summary>
	/// Keyed by piece, as the listening history is: a whole track and its stems are one
	/// piece of music, and one of them cooling must cool the other.
	/// </summary>
	private class TrackCooldown
	{
		public readonly long CooldownUntil;
		public readonly string Piece;

		public TrackCooldown(long cooldownUntil, MusicTrack track)
		{
			CooldownUntil = cooldownUntil;
			Piece = TrackSelector.PieceOf(track);
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

	/// <summary>How long a track that just played stays out of the running.</summary>
	public long CooldownDuration => trackCooldownMs;

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
		var piece = TrackSelector.PieceOf(musicTrack);
		return tracksOnCooldown.Exists(t => t.Piece == piece && now < t.CooldownUntil);
	}

	public void CleanupRoutine()
	{
		var now = currentTimeMs();
		tracksOnCooldown.RemoveAll(t => now > t.CooldownUntil);
	}

	public void Remove(MusicTrack musicTrack)
	{
		var piece = TrackSelector.PieceOf(musicTrack);
		tracksOnCooldown.RemoveAll(t => t.Piece == piece);
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