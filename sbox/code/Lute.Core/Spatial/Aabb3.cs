using System.Numerics;

namespace Lute.Core.Spatial;

/// <summary>
/// Axis-aligned bounding box in Lute world units (S&amp;Box-compatible:
/// X/Y horizontal, Z up, 39.37 units per meter). Immutable.
/// </summary>
public readonly record struct Aabb3( Vector3 Min, Vector3 Max )
{
	/// <summary> Size of the box along each axis (Max - Min). </summary>
	public Vector3 Size => Max - Min;

	/// <summary> Geometric center of the box. </summary>
	public Vector3 Center => (Min + Max) * 0.5f;

	/// <summary> Empty box at the origin. </summary>
	public static Aabb3 Empty => new( Vector3.Zero, Vector3.Zero );

	/// <summary>
	/// True when the point lies inside or on the boundary of the box.
	/// </summary>
	public bool Contains( Vector3 point ) =>
		point.X >= Min.X && point.X <= Max.X &&
		point.Y >= Min.Y && point.Y <= Max.Y &&
		point.Z >= Min.Z && point.Z <= Max.Z;

	/// <summary>
	/// True when the two boxes share any interior volume. A positive
	/// <paramref name="tolerance"/> shrinks the test box on every axis,
	/// so faces that merely touch (intersection width == tolerance) do
	/// NOT count as a volume intersection. Use this for "do two
	/// structures actually penetrate each other".
	/// </summary>
	public bool IntersectsVolume( Aabb3 other, float tolerance = 0f ) =>
		Min.X < other.MaxX - tolerance &&
		Max.X > other.MinX + tolerance &&
		Min.Y < other.MaxY - tolerance &&
		Max.Y > other.MinY + tolerance &&
		Min.Z < other.MaxZ - tolerance &&
		Max.Z > other.MinZ + tolerance;

	/// <summary>
	/// True when the two boxes touch or overlap (inclusive on every
	/// axis). Use this for "are these two pieces adjacent / joined".
	/// </summary>
	public bool TouchesOrIntersects( Aabb3 other ) =>
		Min.X <= other.MaxX && Max.X >= other.MinX &&
		Min.Y <= other.MaxY && Max.Y >= other.MinY &&
		Min.Z <= other.MaxZ && Max.Z >= other.MinZ;

	/// <summary> Box expanded uniformly on every side by <paramref name="amount"/>. </summary>
	public Aabb3 ExpandedBy( float amount ) =>
		new( Min - new Vector3( amount ), Max + new Vector3( amount ) );

	/// <summary> Box translated by <paramref name="delta"/>. </summary>
	public Aabb3 TranslatedBy( Vector3 delta ) =>
		new( Min + delta, Max + delta );

	// Expose Max/Min components directly for the intersection math
	// above without forcing callers to deconstruct the record.
	public float MinX => Min.X;
	public float MaxX => Max.X;
	public float MinY => Min.Y;
	public float MaxY => Max.Y;
	public float MinZ => Min.Z;
	public float MaxZ => Max.Z;
}
