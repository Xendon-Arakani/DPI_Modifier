using System;

namespace Dpi;

internal sealed class ManualRandomSource
{
	internal const int RecommendedBaseDpi = 1600;
	internal const int RangeDpi = 800;
	private readonly Random random = new Random();

	internal int Next(int current, int baseDpi)
	{
		if (!IsValid(current, baseDpi)) throw new ArgumentOutOfRangeException(nameof(current));
		int value = random.Next(Minimum(baseDpi), Maximum(baseDpi));
		return value >= current ? value + 1 : value;
	}

	internal static int Minimum(int baseDpi) => Math.Max(1, baseDpi - RangeDpi);
	internal static int Maximum(int baseDpi) => baseDpi + RangeDpi;
	internal static bool IsValid(int value, int baseDpi) => baseDpi >= 1 && baseDpi <= SeriesSource.MaxBaseDpi && value >= Minimum(baseDpi) && value <= Maximum(baseDpi);
}
