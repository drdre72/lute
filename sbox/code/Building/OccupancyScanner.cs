using System;
using System.Collections.Generic;
using System.Linq;

namespace Lute.Building
{
	/// <summary>
	/// Scans the scene for static world geometry (colliders) and registers
	/// their bounds in the <see cref="ReservationManager"/> occupancy ledger.
	/// This prevents construction tasks from overlapping pre-existing
	/// structures like the monument, temple, walls, acropolis, etc.
	///
	/// Call <see cref="ScanScene"/> after the world is built and before
	/// village construction begins.
	/// </summary>
	public static class OccupancyScanner
	{
		/// <summary>
		/// Scan all static colliders in the scene and register their world
		/// bounds as occupied regions. Skips colliders that are part of
		/// the village construction system (identified by name prefix or
		/// parent hierarchy).
		/// </summary>
		public static int ScanScene( Sandbox.Scene scene )
		{
			if ( scene == null )
			{
				Log.Warning( "Lute: OccupancyScanner - scene is null." );
				return 0;
			}

			int count = 0;
			var colliders = scene.GetAllComponents<Sandbox.Collider>()
				.Where( c => c.IsValid() && c.GameObject.IsValid() )
				.ToList();

			Log.Info( $"Lute: OccupancyScanner found {colliders.Count} colliders in scene." );

			foreach ( var collider in colliders )
			{
				var go = collider.GameObject;

				// Skip village construction objects — they are managed by
				// the ReservationManager through task reservations.
				var name = go.Name ?? "";
				if ( name.StartsWith( "Village_" ) || name.StartsWith( "MedievalVillage" ) ||
					name.StartsWith( "VillageRoot_" ) || name.StartsWith( "VillageBuilder" ) )
					continue;

				// Skip objects that are children of the village root.
				if ( IsChildOf( go, "MedievalVillage" ) || IsChildOf( go, "VillageRoot_" ) )
					continue;

				// Skip NavMesh agents and player controllers.
				if ( go.Components.Get<Sandbox.NavMeshAgent>() != null )
					continue;
				if ( go.Components.Get<Sandbox.PlayerController>() != null )
					continue;

				BBox bounds;
				try
				{
					bounds = collider.GetWorldBounds();
				}
				catch
				{
					continue;
				}

				// Skip zero-volume bounds (e.g. trigger zones with no size).
				var size = bounds.Maxs - bounds.Mins;
				if ( size.x < 1f || size.y < 1f || size.z < 1f )
					continue;

				// Skip very large bounds (terrain) — terrain covers the
				// entire world and would block everything. We only care
				// about discrete structures.
				if ( size.x > 50000f || size.y > 50000f )
					continue;

				var source = $"scan:{go.Name ?? go.Id.ToString()}";
				ReservationManager.RegisterOccupiedRegion( source, bounds );
				count++;
			}

			Log.Info( $"Lute: OccupancyScanner registered {count} static geometry regions." );
			return count;
		}

		/// <summary>
		/// Check if a GameObject is a descendant of a parent with the
		/// given name prefix.
		/// </summary>
		static bool IsChildOf( Sandbox.GameObject go, string parentNamePrefix )
		{
			var parent = go.Parent;
			while ( parent.IsValid() )
			{
				var name = parent.Name ?? "";
				if ( name.StartsWith( parentNamePrefix ) )
					return true;
				parent = parent.Parent;
			}
			return false;
		}
	}
}
