using System;
using System.Collections.Generic;
using System.Linq;
using Sandbox;
using Lute.Crafting;
using Lute.Items;

namespace Lute.Building
{
	/// <summary>
	/// Workstation registry — discovers <see cref="CraftingBench"/>
	/// components in the scene and provides exclusive worker reservation
	/// with capability validation. This is the Gate 2e layer that sits
	/// over the existing crafting system.
	///
	/// Each bench type maps to a required <see cref="NpcCapability"/>:
	/// <list type="bullet">
	/// <item>BrickBench → Masonry</item>
	/// <item>Forge → Smithing</item>
	/// <item>Sawmill → OperateSawmill / Carpentry</item>
	/// <item>Smelter → Smithing</item>
	/// </list>
	///
	/// A crafter must:
	/// 1. Have the required capability (checked via ConstructionDirector.HasCapability)
	/// 2. Reserve the bench exclusively (only one worker at a time)
	/// 3. Provide inputs from their own LuteInventory
	/// 4. Receive outputs into their own LuteInventory
	/// 5. Release the bench when done
	/// </summary>
	public static class WorkstationRegistry
	{
		/// <summary>
		/// A workstation entry — a discovered CraftingBench with its
		/// reservation state.
		/// </summary>
		public class Workstation
		{
			public string Id { get; init; }
			public CraftingBench Bench { get; init; }
			public BenchType Type => Bench.Bench;
			public Vector3 Position => Bench.WorldPosition;
			public string ReservedBy { get; set; }
			public bool IsReserved => !string.IsNullOrEmpty( ReservedBy );
		}

		static readonly Dictionary<string, Workstation> _stations = new();

		/// <summary>
		/// Required capability for each bench type.
		/// </summary>
		static NpcCapability CapabilityForBench( BenchType type ) => type switch
		{
			BenchType.BrickBench => NpcCapability.Masonry,
			BenchType.Forge => NpcCapability.Smithing,
			BenchType.Sawmill => NpcCapability.OperateSawmill,
			BenchType.Smelter => NpcCapability.Smithing,
			_ => NpcCapability.GeneralConstruction,
		};

		/// <summary>
		/// Scan the active scene for all CraftingBench components and
		/// register them. Call once during world-gen or bootstrap.
		/// </summary>
		public static void DiscoverAll()
		{
			int count = 0;
			foreach ( var bench in Game.ActiveScene.GetAllComponents<CraftingBench>() )
			{
				var id = $"bench:{bench.GameObject.Name}:{bench.Id}";
				if ( _stations.ContainsKey( id ) )
					continue;
				_stations[id] = new Workstation
				{
					Id = id,
					Bench = bench,
				};
				count++;
			}
			Log.Info( $"Lute: WorkstationRegistry discovered {count} workstations ({_stations.Count} total)." );
		}

		/// <summary>
		/// Find the nearest unreserved workstation of the given type that
		/// the NPC has the capability to use.
		/// </summary>
		public static Workstation FindAvailable( BenchType type, string npcName, Vector3? nearPos = null )
		{
			var cap = CapabilityForBench( type );
			if ( !ConstructionDirector.HasCapability( npcName, cap ) )
				return null;

			var candidates = _stations.Values
				.Where( s => s.Type == type && !s.IsReserved );

			if ( nearPos.HasValue )
				candidates = candidates.OrderBy( s => Vector3.DistanceBetween( s.Position, nearPos.Value ) );

			return candidates.FirstOrDefault();
		}

		/// <summary>
		/// Reserve a workstation exclusively. Returns false if already
		/// reserved or the NPC lacks the required capability.
		/// </summary>
		public static (bool success, string detail) Reserve( string stationId, string npcName )
		{
			if ( !_stations.TryGetValue( stationId, out var ws ) )
				return ( false, $"workstation not found: {stationId}" );

			if ( ws.IsReserved && ws.ReservedBy != npcName )
				return ( false, $"workstation {stationId} reserved by {ws.ReservedBy}" );

			var cap = CapabilityForBench( ws.Type );
			if ( !ConstructionDirector.HasCapability( npcName, cap ) )
				return ( false, $"{npcName} lacks capability {cap} for {ws.Type}" );

			ws.ReservedBy = npcName;
			return ( true, $"reserved {stationId}" );
		}

		/// <summary>
		/// Release a workstation reservation.
		/// </summary>
		public static void Release( string stationId, string npcName )
		{
			if ( !_stations.TryGetValue( stationId, out var ws ) )
				return;
			if ( ws.ReservedBy == npcName )
				ws.ReservedBy = null;
		}

		/// <summary>
		/// Get a workstation by id.
		/// </summary>
		public static Workstation Get( string stationId ) =>
			_stations.TryGetValue( stationId, out var ws ) ? ws : null;

			/// <summary>
		/// All registered workstations with valid (non-destroyed) benches.
		/// Destroyed/invalid benches are pruned from the registry as a
		/// side effect so they don't accumulate over long play sessions.
		/// </summary>
		public static List<Workstation> All()
		{
			// Prune destroyed benches periodically. We can't remove during
			// foreach over Values, so collect dead keys first.
			List<string> dead = null;
			foreach ( var ws in _stations.Values )
			{
				if ( ws?.Bench == null || !ws.Bench.IsValid() )
				{
					dead ??= new List<string>();
					dead.Add( ws.Id );
				}
			}
			if ( dead != null )
			{
				foreach ( var id in dead )
					_stations.Remove( id );
				Log.Info( $"Lute: WorkstationRegistry pruned {dead.Count} destroyed workstation(s) ({_stations.Count} remaining)." );
			}
			return _stations.Values.ToList();
		}

		/// <summary>
		/// Clear all registrations and reservations (scene reset).
		/// </summary>
		public static void Reset()
		{
			_stations.Clear();
		}

		/// <summary>
		/// Diagnostics dump.
		/// </summary>
		[ConCmd( "workstations" )]
		public static void Dump()
		{
			Log.Info( $"Lute: WorkstationRegistry — {_stations.Count} stations" );
			foreach ( var ws in _stations.Values )
			{
				Log.Info( $"  {ws.Id} ({ws.Type}) @ {ws.Position} — {(ws.IsReserved ? $"reserved by {ws.ReservedBy}" : "available")}" );
			}
		}
	}
}
