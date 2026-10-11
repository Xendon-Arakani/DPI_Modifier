namespace Dpi;

internal static class GameDpiSettings
{
	internal const int MaximumMouseDpi = 65535;
	internal const int MaximumOutputDpi = MaximumMouseDpi * 2;
	internal static bool IsMouseDpiValid(int value) => value >= 1 && value <= MaximumMouseDpi;
	internal static bool IsOutputDpiValid(int value) => value >= 1 && value <= MaximumOutputDpi;
	internal static bool IsFactorValid(int factor) => factor == 1 || factor == 2 || factor == 4;
	internal static int ActualDpi(int baseDpi, int factor) => checked(baseDpi * factor);
}
