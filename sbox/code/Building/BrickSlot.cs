using System;

namespace Lute.Building
{
	/// <summary>
	/// Brick form: the physical shape of a placed brick.
	/// </summary>
	public enum BrickForm
	{
		/// <summary> Full-length brick (24cm body in a 25cm module). </summary>
		Full,
		/// <summary> Half-length brick (12cm body in a 12.5cm module). Used at running-bond edges. </summary>
		Half,
		/// <summary> Quarter brick — reserved for future corner/repair work. </summary>
		Quarter,
	}

	/// <summary>
	/// Brick orientation: how the brick's long axis faces relative to the wall.
	/// A stretcher shows its long face; a header shows its short end.
	/// </summary>
	public enum BrickOrientation
	{
		/// <summary> Long axis parallel to wall length (the default running-bond face). </summary>
		Stretcher,
		/// <summary> Long axis perpendicular to wall length (ties wythes together). </summary>
		Header,
		/// <summary> Brick stood on end (decorative course). Reserved for future use. </summary>
		Soldier,
		/// <summary> Brick laid on its long edge. Reserved for future use. </summary>
		Rowlock,
	}

	/// <summary>
	/// A single brick slot in a masonry assembly. This is the authoritative
	/// topology primitive for Lute construction — a wall, floor, pillar, or
	/// arch is a deterministic set of BrickSlots, not a pile of GameObjects.
	///
	/// The BrickSlot is truth. The GameObject is a temporary visual
	/// representation that can be collapsed into a shared mesh (finalization)
	/// or expanded back out (deconstruction) without changing the topology.
	///
	/// Grid coordinates are in module units (not world units):
	///   GridX = horizontal position along the wall length
	///   GridY = depth position (wythe index, 0 = front face)
	///   GridZ = vertical position (course index, 0 = foundation)
	///
	/// The slot's world position is derived deterministically from the
	/// assembly origin + grid coordinates * module size. The slot does
	/// NOT store a world position — that's computed at placement time.
	/// </summary>
	public readonly struct BrickSlot : IEquatable<BrickSlot>
	{
		/// <summary> Horizontal grid index along the wall length (0-based). </summary>
		public readonly int GridX;

		/// <summary> Depth grid index (wythe). 0 = front face, increasing toward back. </summary>
		public readonly int GridY;

		/// <summary> Vertical grid index (course). 0 = foundation/bottom. </summary>
		public readonly int GridZ;

		/// <summary> Physical form of the brick occupying this slot. </summary>
		public readonly BrickForm Form;

		/// <summary> Orientation of the brick in this slot. </summary>
		public readonly BrickOrientation Orientation;

		public BrickSlot( int gridX, int gridY, int gridZ, BrickForm form, BrickOrientation orientation )
		{
			GridX = gridX;
			GridY = gridY;
			GridZ = gridZ;
			Form = form;
			Orientation = orientation;
		}

		/// <summary> Convenience: front-face stretcher slot (most common). </summary>
		public static BrickSlot Stretcher( int gridX, int gridY, int gridZ )
			=> new( gridX, gridY, gridZ, BrickForm.Full, BrickOrientation.Stretcher );

		/// <summary> Convenience: half-brick stretcher slot (running-bond edge). </summary>
		public static BrickSlot HalfStretcher( int gridX, int gridY, int gridZ )
			=> new( gridX, gridY, gridZ, BrickForm.Half, BrickOrientation.Stretcher );

		public bool Equals( BrickSlot other )
			=> GridX == other.GridX && GridY == other.GridY && GridZ == other.GridZ
				&& Form == other.Form && Orientation == other.Orientation;

		public override bool Equals( object obj ) => obj is BrickSlot other && Equals( other );

		public override int GetHashCode()
		{
			unchecked
			{
				int hash = 17;
				hash = hash * 31 + GridX;
				hash = hash * 31 + GridY;
				hash = hash * 31 + GridZ;
				hash = hash * 31 + (int)Form;
				hash = hash * 31 + (int)Orientation;
				return hash;
			}
		}

		public override string ToString()
			=> $"BrickSlot({GridX},{GridY},{GridZ},{Form},{Orientation})";

		public static bool operator ==( BrickSlot a, BrickSlot b ) => a.Equals( b );
		public static bool operator !=( BrickSlot a, BrickSlot b ) => !a.Equals( b );
	}
}
