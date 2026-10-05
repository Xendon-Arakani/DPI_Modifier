using System;

namespace Dpi;

internal sealed class DeadlineClock
{
	private long deadline;

	private int remainder;

	public bool IsSet { get; private set; }

	public bool IsRunning { get; private set; }

	public void Start(int milliseconds, long now, bool running)
	{
		if (milliseconds < 0)
		{
			throw new ArgumentOutOfRangeException("milliseconds");
		}
		remainder = milliseconds;
		deadline = now + milliseconds;
		IsSet = true;
		IsRunning = running;
	}

	public int Remaining(long now)
	{
		if (IsSet)
		{
			if (IsRunning)
			{
				return (int)Math.Max(0L, deadline - now);
			}
			return remainder;
		}
		return 0;
	}

	public void Pause(long now)
	{
		if (IsRunning)
		{
			remainder = Remaining(now);
			IsRunning = false;
		}
	}

	public void Resume(long now)
	{
		if (IsSet && !IsRunning)
		{
			deadline = now + remainder;
			IsRunning = true;
		}
	}

	public void Clear()
	{
		IsSet = false;
		IsRunning = false;
		remainder = 0;
	}

	public int NextDelay(int countdownMilliseconds, long now)
	{
		int num = Remaining(now);
		if (num > countdownMilliseconds)
		{
			return num - countdownMilliseconds;
		}
		if (num == 0)
		{
			return 1;
		}
		int num2 = 999 - (num - 1) % 1000;
		int num3 = num2 * 3 / 1000;
		return ((num3 + 1) * 1000 + 2) / 3 - num2;
	}
}
