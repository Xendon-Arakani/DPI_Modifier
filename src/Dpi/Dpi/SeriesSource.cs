using System;

namespace Dpi;

internal sealed class SeriesSource
{
	public const int EntryCount = 9;

	public const int MaxBaseDpi = 9999;

	public const int MaxSeconds = 86400;

	private readonly Random random = new Random();

	public int[] Create(int baseDpi, int lowerRangeDpi, int upperRangeDpi)
	{
		if (!IsConfigurationValid(baseDpi, lowerRangeDpi, upperRangeDpi))
		{
			throw new ArgumentOutOfRangeException("baseDpi");
		}
		int[] array = new int[9];
		array[0] = (array[1] = (array[2] = baseDpi));
		for (int i = 0; i < 6; i++)
		{
			array[i + 3] = PickRandomDpi(baseDpi, lowerRangeDpi, upperRangeDpi);
		}
		Shuffle(array);
		return array;
	}

	private int PickRandomDpi(int baseDpi, int lowerRangeDpi, int upperRangeDpi)
	{
		int num = random.Next(lowerRangeDpi + upperRangeDpi);
		if (num >= lowerRangeDpi)
		{
			return baseDpi + 1 + num - lowerRangeDpi;
		}
		return baseDpi - lowerRangeDpi + num;
	}

	public static bool IsTimingConfigurationValid(int durationSeconds, int countdownSeconds)
	{
		if (durationSeconds >= 1 && durationSeconds <= 86400 && countdownSeconds >= 0)
		{
			return countdownSeconds <= durationSeconds;
		}
		return false;
	}

	private void Shuffle(int[] values)
	{
		for (int num = values.Length - 1; num > 0; num--)
		{
			int num2 = random.Next(num + 1);
			int num3 = values[num];
			values[num] = values[num2];
			values[num2] = num3;
		}
	}

	public static bool IsConfigurationValid(int baseDpi, int lowerRangeDpi, int upperRangeDpi)
	{
		if (baseDpi >= 1 && baseDpi <= 9999 && lowerRangeDpi >= 0 && lowerRangeDpi < baseDpi && upperRangeDpi >= 0 && upperRangeDpi <= baseDpi)
		{
			if (lowerRangeDpi == 0)
			{
				return upperRangeDpi != 0;
			}
			return true;
		}
		return false;
	}

	public static bool IsValid(int[] values, int baseDpi, int lowerRangeDpi, int upperRangeDpi)
	{
		if (!IsConfigurationValid(baseDpi, lowerRangeDpi, upperRangeDpi) || values == null || values.Length != 9)
		{
			return false;
		}
		int num = 0;
		foreach (int num2 in values)
		{
			if (num2 == baseDpi)
			{
				num++;
			}
			if (num2 < baseDpi - lowerRangeDpi || num2 > baseDpi + upperRangeDpi)
			{
				return false;
			}
		}
		return num == 3;
	}
}
