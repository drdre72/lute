using Lute.Core.Spatial;

namespace Lute.Building
{
	/// <summary>
	/// Engine-boundary adapter: converts between Lute.Core spatial types
	/// (<see cref="System.Numerics.Vector3"/>, <see cref="Aabb3"/>) and
	/// S&Box engine types (<see cref="Vector3"/>, <see cref="BBox"/>).
	/// This is the only place in the codebase that should see both type
	/// systems at once.
	/// </summary>
	public static class SandboxSpatialConversions
	{
		public static System.Numerics.Vector3 ToCore( this Vector3 v ) =>
			new( v.x, v.y, v.z );

		public static Vector3 ToSandbox( this System.Numerics.Vector3 v ) =>
			new( v.X, v.Y, v.Z );

		public static Aabb3 ToCore( this BBox box ) =>
			new( box.Mins.ToCore(), box.Maxs.ToCore() );

		public static BBox ToSandbox( this Aabb3 box ) =>
			new( box.Min.ToSandbox(), box.Max.ToSandbox() );
	}
}
