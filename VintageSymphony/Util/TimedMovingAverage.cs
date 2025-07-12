namespace VintageSymphony.Util;

public class TimedMovingAverage<T> where T : struct
{
	private readonly TimeSpan _windowDuration;
	private readonly Queue<(DateTime timestamp, T value)> _samples = new();
	private dynamic _sum = default(T);

	public TimedMovingAverage(TimeSpan windowDuration)
	{
		_windowDuration = windowDuration;
	}

	public void Add(T val, DateTime now)
	{
		_samples.Enqueue((now, val));
		_sum += val;
		TrimOld(now);
	}

	public T GetAverage()
	{
		if (_samples.Count == 0) return default;
		return (T)(_sum / _samples.Count);
	}

	public T GetAverageOverTimespan(TimeSpan timespan)
	{
		if (_samples.Count == 0) return default;
		
		// Get the most recent sample time as reference point
		DateTime latestTime = _samples.Count > 0 ? _samples.Last().timestamp : DateTime.MinValue;
		
		// If timespan is larger than all available samples, use all samples
		DateTime cutoffTime = latestTime - timespan;
		
		// Count only samples within the specified timespan
		dynamic samplesSum = default(T);
		int samplesCount = 0;
		
		foreach (var sample in _samples)
		{
			if (sample.timestamp >= cutoffTime)
			{
				samplesSum += sample.value;
				samplesCount++;
			}
		}
		
		// If no samples within the timespan, return default
		if (samplesCount == 0) return default;
		return (T)(samplesSum / samplesCount);
	}
	
	private void TrimOld(DateTime now)
	{
		while (_samples.Count > 0 && now - _samples.Peek().timestamp > _windowDuration)
		{
			_sum -= _samples.Dequeue().value;
		}
	}
}