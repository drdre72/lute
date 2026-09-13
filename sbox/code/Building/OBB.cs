using System;
using System.Collections.Generic;
using System.Linq;

namespace Lute.Building
{
	/// <summary>
	/// Oriented bounding box for accurate footprint of rotated structures.
	/// </summary>
	public struct OBB
	{
		public Vector3 Center;
		public Vector3 HalfExtents;
		public float Rotation; // Y-axis rotation in degrees

		public OBB( Vector3 center, Vector3 halfExtents, float rotation = 0f )
		{
			Center = center;
			HalfExtents = halfExtents;
			Rotation = rotation;
		}

		/// <summary>Convert to an axis-aligned bounding box that contains this OBB.</summary>
		public BBox ToAABB()
		{
			float angle = Rotation * (float)Math.PI / 180f;
			float c = Math.Abs( (float)Math.Cos( angle ) );
			float s = Math.Abs( (float)Math.Sin( angle ) );
			float aabbX = HalfExtents.x * c + HalfExtents.y * s;
			float aabbY = HalfExtents.x * s + HalfExtents.y * c;
			var half = new Vector3( aabbX, aabbY, HalfExtents.z );
			return new BBox( Center - half, Center + half );
		}

		/// <summary>
		/// Test if this OBB overlaps another OBB using the Separating Axis Theorem.
		/// Returns the overlap volume if they intersect, 0 otherwise.
		/// </summary>
		public float OverlapVolume( in OBB other )
		{
			// For same rotation (common case for wall-wall), use AABB overlap
			if ( Math.Abs( Rotation - other.Rotation ) < 1f )
			{
				var a = ToAABB();
				var b = other.ToAABB();
				float ixX = Math.Min( a.Maxs.x, b.Maxs.x ) - Math.Max( a.Mins.x, b.Mins.x );
				float ixY = Math.Min( a.Maxs.y, b.Maxs.y ) - Math.Max( a.Mins.y, b.Mins.y );
				float ixZ = Math.Min( a.Maxs.z, b.Maxs.z ) - Math.Max( a.Mins.z, b.Mins.z );
				if ( ixX <= 0 || ixY <= 0 || ixZ <= 0 ) return 0;
				return ixX * ixY * ixZ;
			}

			// For different rotations, use SAT on the XY plane (walls are vertical)
			// Get the 4 corners of each OBB in XY plane
			var cornersA = GetCornersXY();
			var cornersB = other.GetCornersXY();

			// Axes to test: 2 from each OBB
			float angleA = Rotation * (float)Math.PI / 180f;
			float angleB = other.Rotation * (float)Math.PI / 180f;
			var axes = new[]
			{
				new Vector2( (float)Math.Cos( angleA ), (float)Math.Sin( angleA ) ),
				new Vector2( -(float)Math.Sin( angleA ), (float)Math.Cos( angleA ) ),
				new Vector2( (float)Math.Cos( angleB ), (float)Math.Sin( angleB ) ),
				new Vector2( -(float)Math.Sin( angleB ), (float)Math.Cos( angleB ) ),
			};

			float minOverlap = float.MaxValue;

			foreach ( var axis in axes )
			{
				ProjectOntoAxis( cornersA, axis, out float minA, out float maxA );
				ProjectOntoAxis( cornersB, axis, out float minB, out float maxB );

				float overlap = Math.Min( maxA, maxB ) - Math.Max( minA, minB );
				if ( overlap <= 0 ) return 0; // Separating axis found — no overlap
				minOverlap = Math.Min( minOverlap, overlap );
			}

			// Check Z overlap
			float zA = Math.Min( Center.z + HalfExtents.z, other.Center.z + other.HalfExtents.z )
				- Math.Max( Center.z - HalfExtents.z, other.Center.z - other.HalfExtents.z );
			if ( zA <= 0 ) return 0;

			// Approximate overlap volume (min penetration * Z overlap * average width)
			return minOverlap * zA * minOverlap;
		}

		Vector2[] GetCornersXY()
		{
			float angle = Rotation * (float)Math.PI / 180f;
			float c = (float)Math.Cos( angle );
			float s = (float)Math.Sin( angle );
			var h = new Vector2( HalfExtents.x, HalfExtents.y );
			var center2 = new Vector2( Center.x, Center.y );

			var localCorners = new[]
			{
				new Vector2( -h.x, -h.y ),
				new Vector2( h.x, -h.y ),
				new Vector2( h.x, h.y ),
				new Vector2( -h.x, h.y ),
			};

			var result = new Vector2[4];
			for ( int i = 0; i < 4; i++ )
			{
				result[i] = new Vector2(
					center2.x + localCorners[i].x * c - localCorners[i].y * s,
					center2.y + localCorners[i].x * s + localCorners[i].y * c );
			}
			return result;
		}

		static void ProjectOntoAxis( Vector2[] corners, Vector2 axis, out float min, out float max )
		{
			min = float.MaxValue;
			max = float.MinValue;
			foreach ( var c in corners )
			{
				float proj = c.x * axis.x + c.y * axis.y;
				min = Math.Min( min, proj );
				max = Math.Max( max, proj );
			}
		}
	}

	/// <summary>
	/// Structured result for a placement validation check.
	/// </summary>
	public sealed class PlacementValidation
	{
		public bool IsValid { get; init; }
		public string Reason { get; init; }
		public string BlockingEntity { get; init; }
		public float IntersectionVolume { get; init; }
		public Vector3? NearestValidPosition { get; init; }

		public static PlacementValidation Valid() => new() { IsValid = true };
		public static PlacementValidation Invalid( string reason, string blockingEntity = null,
			float intersection = 0f, Vector3? nearestValid = null ) => new()
		{
			IsValid = false,
			Reason = reason,
			BlockingEntity = blockingEntity,
			IntersectionVolume = intersection,
			NearestValidPosition = nearestValid,
		};
	}
}
