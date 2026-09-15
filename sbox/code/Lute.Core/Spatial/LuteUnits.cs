namespace Lute.Core.Spatial;

/// <summary>
/// Lute world-unit conversions. All Lute.Core spatial coordinates are
/// S&amp;Box-compatible world units represented as floats (X/Y horizontal,
/// Z up). Conversion to meters is deliberate, never implicit.
/// </summary>
public static class LuteUnits
{
	/// <summary> S&amp;Box world units per meter (39.37 inches/meter). </summary>
	public const float WorldUnitsPerMeter = 39.37f;

	/// <summary> Convert a measurement in meters to world units. </summary>
	public static float Meters( float meters ) => meters * WorldUnitsPerMeter;

	/// <summary> Convert a measurement in world units to meters. </summary>
	public static float ToMeters( float worldUnits ) => worldUnits / WorldUnitsPerMeter;
}
