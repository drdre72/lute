using System;
using System.Collections.Generic;
using System.Linq;
using Sandbox;

namespace Lute.Building
{
	/// <summary>
	/// Result of a spatial query — what occupies a location and why.
	/// </summary>
	public sealed class SpatialQueryResult
	{
		public bool IsFree { get; init; }
		public StructuralPlacement Occupant { get; init; }
		public string Reason { get; init; }
		public float ClearanceMeters { get; init; }

		public static SpatialQueryResult Free( float clearance = float.MaxValue )
			=> new() { IsFree = true, ClearanceMeters = clearance };

		public static SpatialQueryResult Occupied( StructuralPlacement occupant, string reason = null )
			=> new() { IsFree = false, Occupant = occupant, Reason = reason ?? $"occupied by {occupant.SemanticType} [{occupant.EntityId}]" };
	}

	/// <summary>
	/// Authoritative spatial registry for all structural placements.
	///
	/// This is the query layer NPCs use to reason about the world:
	///   - Is this location free?
	///   - What is occupying it?
	///   - What clearance exists?
	///   - What cells would this structure reserve?
	///
	/// It stores StructuralPlacement objects (with full OBB) and supports
	/// both point and volume queries. It wraps but does not replace
	/// ReservationManager — task-tied AABB occupancy remains for the
	/// reservation/claim system, while SpatialRegistry provides the
	/// semantic, OBB-aware query layer.
	///
	/// All queries are deterministic and thread-safe (read-only after
	/// registration). Registration happens at placement time.
	/// </summary>
	public static class SpatialRegistry
	{
		// EntityId → StructuralPlacement
		static readonly Dictionary<string, StructuralPlacement> _placements = new();
		// Spatial hash for fast AABB queries (cell size = 1m = 39.37 units)
		const float CellSize = 39.37f;
		static readonly Dictionary<(int, int), List<string>> _grid = new();

		static readonly object _lock = new();

		/// <summary>
		/// Register a structural placement. Called at placement time
		/// (after SpawnBox creates the GameObject). The placement's OBB
		/// is the authoritative occupied volume.
		/// </summary>
		public static void Register( StructuralPlacement placement )
		{
			lock ( _lock )
			{
				_placements[placement.EntityId] = placement;
				IndexInGrid( placement );
			}
		}

		/// <summary>
		/// Remove a placement (e.g. on deconstruction).
		/// </summary>
		public static bool Unregister( string entityId )
		{
			lock ( _lock )
			{
				if ( !_placements.TryGetValue( entityId, out var placement ) )
					return false;
				_placements.Remove( entityId );
				RemoveFromGrid( placement );
				return true;
			}
		}

		/// <summary>
		/// Is the given world point free of any structural placement?
		/// </summary>
		public static SpatialQueryResult IsFree( Vector3 point )
		{
			lock ( _lock )
			{
				foreach ( var id in CandidatesNear( point ) )
				{
					if ( !_placements.TryGetValue( id, out var p ) ) continue;
					if ( PointInOBB( point, p ) )
						return SpatialQueryResult.Occupied( p );
				}
				return SpatialQueryResult.Free();
			}
		}

		/// <summary>
		/// Is the given volume free of any structural placement?
		/// Returns the first occupant if not free.
		/// </summary>
		public static SpatialQueryResult IsVolumeFree( BBox aabb, float yaw = 0f )
		{
			lock ( _lock )
			{
				foreach ( var id in CandidatesNear( aabb ) )
				{
					if ( !_placements.TryGetValue( id, out var p ) ) continue;
					if ( AABBsOverlap( aabb, p.OBB ) )
						return SpatialQueryResult.Occupied( p );
				}
				return SpatialQueryResult.Free();
			}
		}

		/// <summary>
		/// What occupies the given world point? Returns the placement
		/// or null if free.
		/// </summary>
		public static StructuralPlacement WhatOccupies( Vector3 point )
		{
			lock ( _lock )
			{
				foreach ( var id in CandidatesNear( point ) )
				{
					if ( !_placements.TryGetValue( id, out var p ) ) continue;
					if ( PointInOBB( point, p ) )
						return p;
				}
				return default;
			}
		}

		/// <summary>
		/// What is the clearance (distance to nearest placement) from
		/// the given point in the given direction? Returns float.MaxValue
		/// if nothing is nearby.
		/// </summary>
		public static float GetClearance( Vector3 point, Vector3 direction, float maxDistance = 100f * 39.37f )
		{
			direction = direction.Normal;
			lock ( _lock )
			{
				float nearest = maxDistance;
				foreach ( var p in _placements.Values )
				{
					var obb = p.OBB;
					if ( RayOBBDistance( point, direction, obb, out float dist ) && dist < nearest )
						nearest = dist;
				}
				return nearest / 39.37f; // return in meters
			}
		}

		/// <summary>
		/// Get all placements within a radius of the given point.
		/// </summary>
		public static List<StructuralPlacement> GetNearby( Vector3 point, float radiusUnits )
		{
			var result = new List<StructuralPlacement>();
			lock ( _lock )
			{
				foreach ( var id in CandidatesNear( point, radiusUnits ) )
				{
					if ( !_placements.TryGetValue( id, out var p ) ) continue;
					if ( Vector3.DistanceBetween( point, p.Position ) <= radiusUnits )
						result.Add( p );
				}
			}
			return result;
		}

		/// <summary>
		/// Get all placements of a specific semantic type.
		/// </summary>
		public static List<StructuralPlacement> GetByType( StructuralType type )
		{
			lock ( _lock )
			{
				return _placements.Values.Where( p => p.SemanticType == type ).ToList();
			}
		}

		/// <summary>
		/// Get all placements belonging to a parent assembly.
		/// </summary>
		public static List<StructuralPlacement> GetByAssembly( string parentAssembly )
		{
			lock ( _lock )
			{
				return _placements.Values.Where( p => p.ParentAssembly == parentAssembly ).ToList();
			}
		}

		/// <summary>
		/// Total number of registered placements.
		/// </summary>
		public static int Count
		{
			get
			{
				lock ( _lock ) return _placements.Count;
			}
		}

		/// <summary>
		/// Clear all placements (e.g. on scene reset).
		/// </summary>
		public static void Reset()
		{
			lock ( _lock )
			{
				_placements.Clear();
				_grid.Clear();
			}
		}

		/// <summary>
		/// Console command: dump spatial registry stats and nearby placements.
		/// Usage: spatial_query -16417,-16417,0
		/// </summary>
		[ConCmd( "spatial_query" )]
		public static void QueryCommand( string coords = "" )
		{
			Log.Info( $"Lute: SpatialRegistry — {Count} placements registered." );

			if ( string.IsNullOrEmpty( coords ) )
			{
				// Summary by type
				foreach ( StructuralType t in Enum.GetValues( typeof( StructuralType ) ) )
				{
					var list = GetByType( t );
					if ( list.Count > 0 )
						Log.Info( $"  {t}: {list.Count}" );
				}
				return;
			}

			// Parse "x,y,z" and query that point
			var parts = coords.Split( ',' );
			if ( parts.Length < 2 )
			{
				Log.Warning( "Lute: spatial_query — usage: spatial_query x,y,z" );
				return;
			}
			if ( !float.TryParse( parts[0], out float x ) || !float.TryParse( parts[1], out float y ) )
			{
				Log.Warning( "Lute: spatial_query — invalid coordinates" );
				return;
			}
			float z = parts.Length >= 3 && float.TryParse( parts[2], out float zz ) ? zz : 0f;
			var point = new Vector3( x, y, z );

			var occupant = WhatOccupies( point );
			if ( occupant.EntityId != null )
			{
				Log.Info( $"Lute: spatial_query {point} → OCCUPIED by {occupant}" );
			}
			else
			{
				Log.Info( $"Lute: spatial_query {point} → FREE" );
			}

			// Show nearby placements within 5m
			var nearby = GetNearby( point, 5f * 39.37f );
			Log.Info( $"  Nearby ({nearby.Count} within 5m):" );
			foreach ( var p in nearby.Take( 10 ) )
				Log.Info( $"    {p}" );
		}

		// ── Spatial hash ──

		static (int, int) CellKey( Vector3 pos )
		{
			return ( (int)MathF.Floor( pos.x / CellSize ), (int)MathF.Floor( pos.y / CellSize ) );
		}

		static void IndexInGrid( StructuralPlacement p )
		{
			var obb = p.OBB;
			var mins = obb.Mins;
			var maxs = obb.Maxs;
			for ( int x = (int)MathF.Floor( mins.x / CellSize ); x <= (int)MathF.Floor( maxs.x / CellSize ); x++ )
			for ( int y = (int)MathF.Floor( mins.y / CellSize ); y <= (int)MathF.Floor( maxs.y / CellSize ); y++ )
			{
				var key = (x, y);
				if ( !_grid.TryGetValue( key, out var list ) )
				{
					list = new List<string>();
					_grid[key] = list;
				}
				list.Add( p.EntityId );
			}
		}

		static void RemoveFromGrid( StructuralPlacement p )
		{
			var obb = p.OBB;
			var mins = obb.Mins;
			var maxs = obb.Maxs;
			for ( int x = (int)MathF.Floor( mins.x / CellSize ); x <= (int)MathF.Floor( maxs.x / CellSize ); x++ )
			for ( int y = (int)MathF.Floor( mins.y / CellSize ); y <= (int)MathF.Floor( maxs.y / CellSize ); y++ )
			{
				var key = (x, y);
				if ( _grid.TryGetValue( key, out var list ) )
					list.Remove( p.EntityId );
			}
		}

		static IEnumerable<string> CandidatesNear( Vector3 point )
		{
			var key = CellKey( point );
			// Check 3x3 neighborhood
			for ( int dx = -1; dx <= 1; dx++ )
			for ( int dy = -1; dy <= 1; dy++ )
			{
				var k = (key.Item1 + dx, key.Item2 + dy);
				if ( _grid.TryGetValue( k, out var list ) )
				{
					foreach ( var id in list )
						yield return id;
				}
			}
		}

		static IEnumerable<string> CandidatesNear( Vector3 point, float radius )
		{
			int cellRadius = (int)MathF.Ceiling( radius / CellSize ) + 1;
			var key = CellKey( point );
			for ( int dx = -cellRadius; dx <= cellRadius; dx++ )
			for ( int dy = -cellRadius; dy <= cellRadius; dy++ )
			{
				var k = (key.Item1 + dx, key.Item2 + dy);
				if ( _grid.TryGetValue( k, out var list ) )
				{
					foreach ( var id in list )
						yield return id;
				}
			}
		}

		static IEnumerable<string> CandidatesNear( BBox aabb )
		{
			int x0 = (int)MathF.Floor( aabb.Mins.x / CellSize );
			int x1 = (int)MathF.Ceiling( aabb.Maxs.x / CellSize );
			int y0 = (int)MathF.Floor( aabb.Mins.y / CellSize );
			int y1 = (int)MathF.Ceiling( aabb.Maxs.y / CellSize );
			for ( int x = x0; x <= x1; x++ )
			for ( int y = y0; y <= y1; y++ )
			{
				var k = (x, y);
				if ( _grid.TryGetValue( k, out var list ) )
				{
					foreach ( var id in list )
						yield return id;
				}
			}
		}

		// ── Geometry helpers ──

		static bool PointInOBB( Vector3 point, StructuralPlacement p )
		{
			var obb = p.OBB;
			return point.x >= obb.Mins.x && point.x <= obb.Maxs.x
				&& point.y >= obb.Mins.y && point.y <= obb.Maxs.y
				&& point.z >= obb.Mins.z && point.z <= obb.Maxs.z;
		}

		static bool AABBsOverlap( BBox a, BBox b )
		{
			return a.Mins.x <= b.Maxs.x && a.Maxs.x >= b.Mins.x
				&& a.Mins.y <= b.Maxs.y && a.Maxs.y >= b.Mins.y
				&& a.Mins.z <= b.Maxs.z && a.Maxs.z >= b.Mins.z;
		}

		static bool RayOBBDistance( Vector3 origin, Vector3 dir, BBox obb, out float distance )
		{
			distance = 0f;
			float tmin = float.MinValue;
			float tmax = float.MaxValue;

			for ( int i = 0; i < 3; i++ )
			{
				float o = i == 0 ? origin.x : i == 1 ? origin.y : origin.z;
				float d = i == 0 ? dir.x : i == 1 ? dir.y : dir.z;
				float mn = i == 0 ? obb.Mins.x : i == 1 ? obb.Mins.y : obb.Mins.z;
				float mx = i == 0 ? obb.Maxs.x : i == 1 ? obb.Maxs.y : obb.Maxs.z;

				if ( MathF.Abs( d ) < 0.0001f )
				{
					if ( o < mn || o > mx ) return false;
				}
				else
				{
					float t1 = (mn - o) / d;
					float t2 = (mx - o) / d;
					if ( t1 > t2 ) (t1, t2) = (t2, t1);
					if ( t1 > tmin ) tmin = t1;
					if ( t2 < tmax ) tmax = t2;
					if ( tmin > tmax ) return false;
					if ( tmax < 0 ) return false;
				}
			}

			distance = tmin >= 0 ? tmin : tmax;
			return distance >= 0;
		}
	}
}
