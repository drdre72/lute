using System;
using System.Collections.Generic;
using System.Linq;
using Sandbox;

namespace Lute.Building
{
	/// <summary>
	/// Raw and processed material types in the Lute economy. Raw
	/// materials come from <see cref="ResourceSource"/>s; processed
	/// materials are produced at workstations. Both can be stored in
	/// <see cref="Stockpile"/>s and hauled by NPCs.
	/// </summary>
	public enum ResourceType
	{
		Unknown,
		// Raw
		Wood,    // from trees
		Stone,   // from quarry
		Ore,     // from mine
		// Processed
		Brick,   // from brick bench (stone -> brick)
		Plank,   // from sawmill (wood -> plank)
		Timber,  // from sawmill (wood -> timber)
		Ingot,   // from forge (ore -> ingot)
		Tool,    // from smithy (ingot -> tool)
	}

	/// <summary>
	/// Whether a resource type is raw (from a source) or processed
	/// (from a workstation).
	/// </summary>
	public static class ResourceClassification
	{
		static readonly HashSet<ResourceType> _raw = new()
		{ ResourceType.Wood, ResourceType.Stone, ResourceType.Ore };

		static readonly HashSet<ResourceType> _processed = new()
		{ ResourceType.Brick, ResourceType.Plank, ResourceType.Timber, ResourceType.Ingot, ResourceType.Tool };

		public static bool IsRaw( ResourceType t ) => _raw.Contains( t );
		public static bool IsProcessed( ResourceType t ) => _processed.Contains( t );
		public static bool IsValid( ResourceType t ) => t != ResourceType.Unknown;
	}

	/// <summary>
	/// A physically located source of a raw resource (tree cluster,
	/// quarry face, ore vein). Has a finite yield that depletes as NPCs
	/// gather from it. The source itself is not reservable — multiple
	/// NPCs can gather simultaneously — but the yield is tracked so
	/// depletion is deterministic.
	///
	/// Per PR #6 §5: "Resource source interface" — sources are
	/// world-side semantic contracts, not NPC-side logic.
	/// </summary>
	public sealed class ResourceSource
	{
		/// <summary> Unique source id (e.g. "tree_cluster_0"). </summary>
		public string Id { get; init; }

		/// <summary> What this source produces. </summary>
		public ResourceType Type { get; init; }

		/// <summary> World position of the source. </summary>
		public Vector3 Position { get; init; }

		/// <summary> Radius of the gather area (inches). </summary>
		public float Radius { get; init; } = 200f;

		/// <summary> Total yield available before depletion. </summary>
		public int TotalYield { get; init; }

		/// <summary> Yield remaining (decrements as gathered). </summary>
		public int RemainingYield { get; set; }

		/// <summary> Yield per gather action. </summary>
		public int YieldPerGather { get; init; } = 10;

		/// <summary> True when RemainingYield reaches 0. </summary>
		public bool IsDepleted => RemainingYield <= 0;

		/// <summary> Optional: required capability to gather from this source. </summary>
		public NpcCapability RequiredCapability { get; init; } = NpcCapability.None;

		/// <summary> Optional: required tool (future, not yet enforced). </summary>
		public string RequiredTool { get; init; } = "";

		/// <summary> Visual placeholder GameObject (SpawnBox). </summary>
		public GameObject VisualGo { get; set; }

		public ResourceSource( string id, ResourceType type, Vector3 pos,
			int totalYield, int yieldPerGather = 10, float radius = 200f,
			NpcCapability requiredCapability = NpcCapability.None,
			string requiredTool = "" )
		{
			Id = id;
			Type = type;
			Position = pos;
			TotalYield = totalYield;
			RemainingYield = totalYield;
			YieldPerGather = yieldPerGather;
			Radius = radius;
			RequiredCapability = requiredCapability;
			RequiredTool = requiredTool;
		}

		/// <summary>
		/// Attempt to gather from this source. Returns the amount
		/// actually gathered (may be less than YieldPerGather if nearly
		/// depleted). Returns 0 if depleted.
		/// </summary>
		public int Gather()
		{
			if ( IsDepleted ) return 0;
			int amount = Math.Min( YieldPerGather, RemainingYield );
			RemainingYield -= amount;
			return amount;
		}

		public string Summary =>
			$"{Id} ({Type}) @ {Position} yield={RemainingYield}/{TotalYield}" +
			( IsDepleted ? " DEPLETED" : "" );
	}

	/// <summary>
	/// A reservable physical stockpile where materials are stored.
	/// Stockpiles have a capacity and track their current contents by
	/// resource type. NPCs reserve stockpile slots before depositing or
	/// withdrawing materials.
	///
	/// Per PR #6 §5: "Reservable physical stockpiles" — stockpiles are
	/// world-side semantic contracts connected to the construction
	/// pipeline, not NPC-side inventory.
	/// </summary>
	public sealed class Stockpile
	{
		/// <summary> Unique stockpile id (e.g. "stockyard_0"). </summary>
		public string Id { get; init; }

		/// <summary> World position. </summary>
		public Vector3 Position { get; init; }

		/// <summary> OBB half-extents for spatial queries. </summary>
		public Vector3 HalfExtents { get; init; }

		/// <summary> Maximum units this stockpile can hold. </summary>
		public int Capacity { get; init; }

		/// <summary> Current contents by resource type. </summary>
		public Dictionary<ResourceType, int> Contents { get; } = new();

		/// <summary> Total units currently stored. </summary>
		public int Used => Contents.Values.Sum();

		/// <summary> Available space. </summary>
		public int Available => Capacity - Used;

		/// <summary> Who has reserved this stockpile (NPC name -> reserved units). </summary>
		public Dictionary<string, int> Reservations { get; } = new();

		/// <summary> Total units reserved. </summary>
		public int ReservedTotal => Reservations.Values.Sum();

		/// <summary> Visual placeholder GameObject. </summary>
		public GameObject VisualGo { get; set; }

		public Stockpile( string id, Vector3 pos, Vector3 halfExtents, int capacity )
		{
			Id = id;
			Position = pos;
			HalfExtents = halfExtents;
			Capacity = capacity;
		}

		/// <summary>
		/// Reserve space on this stockpile. Returns true if the
		/// reservation was created, false if insufficient available space.
		/// </summary>
		public bool Reserve( string npcName, int amount )
		{
			if ( amount <= 0 ) return false;
			int availableForReserve = Available - ( ReservedTotal - ( Reservations.GetValueOrDefault( npcName ) ) );
			if ( amount > availableForReserve ) return false;
			Reservations[npcName] = Reservations.GetValueOrDefault( npcName ) + amount;
			return true;
		}

		/// <summary>
		/// Release a reservation (full or partial).
		/// </summary>
		public void ReleaseReservation( string npcName, int amount )
		{
			if ( !Reservations.ContainsKey( npcName ) ) return;
			Reservations[npcName] -= amount;
			if ( Reservations[npcName] <= 0 )
				Reservations.Remove( npcName );
		}

		/// <summary>
		/// Deposit materials into this stockpile. Returns the amount
		/// actually deposited (may be less if near capacity).
		/// </summary>
		public int Deposit( ResourceType type, int amount )
		{
			if ( amount <= 0 ) return 0;
			int canDeposit = Math.Min( amount, Available );
			Contents[type] = Contents.GetValueOrDefault( type ) + canDeposit;
			return canDeposit;
		}

		/// <summary>
		/// Withdraw materials from this stockpile. Returns the amount
		/// actually withdrawn (may be less if insufficient stock).
		/// </summary>
		public int Withdraw( ResourceType type, int amount )
		{
			if ( amount <= 0 ) return 0;
			int have = Contents.GetValueOrDefault( type );
			int canWithdraw = Math.Min( amount, have );
			Contents[type] = have - canWithdraw;
			if ( Contents[type] <= 0 )
				Contents.Remove( type );
			return canWithdraw;
		}

		/// <summary> How much of a type is stored here. </summary>
		public int Count( ResourceType type ) => Contents.GetValueOrDefault( type );

		public string Summary =>
			$"{Id} @ {Position} used={Used}/{Capacity} reserved={ReservedTotal}" +
			( Contents.Count > 0 ? " [" + string.Join( ", ", Contents.Select( kv => $"{kv.Key}={kv.Value}" ) ) + "]" : "" );
	}

	/// <summary>
	/// Authoritative registry of all resource sources and stockpiles in
	/// the world. NPCs query this to find where to gather, where to
	/// deposit, and where to withdraw materials. The
	/// <see cref="NpcActionDispatcher"/> routes ClaimResource/
	/// ReleaseResource intents here.
	///
	/// This is the single resource authority — there is no second
	/// resource tracking system (per PR #6: "reuse existing authorities,
	/// do not create a second scheduler or spatial authority").
	/// </summary>
	public static class ResourceRegistry
	{
		static readonly Dictionary<string, ResourceSource> _sources = new();
		static readonly Dictionary<string, Stockpile> _stockpiles = new();
		static bool _initialized;

		/// <summary> Register a resource source. </summary>
		public static void RegisterSource( ResourceSource source )
		{
			if ( source == null ) return;
			_sources[source.Id] = source;
		}

		/// <summary> Register a stockpile. </summary>
		public static void RegisterStockpile( Stockpile stockpile )
		{
			if ( stockpile == null ) return;
			_stockpiles[stockpile.Id] = stockpile;
		}

		/// <summary> Get a source by id. </summary>
		public static ResourceSource GetSource( string id ) =>
			!string.IsNullOrEmpty( id ) && _sources.TryGetValue( id, out var s ) ? s : null;

		/// <summary> Get a stockpile by id. </summary>
		public static Stockpile GetStockpile( string id ) =>
			!string.IsNullOrEmpty( id ) && _stockpiles.TryGetValue( id, out var s ) ? s : null;

		/// <summary> All registered sources. </summary>
		public static List<ResourceSource> AllSources() => _sources.Values.ToList();

		/// <summary> All registered stockpiles. </summary>
		public static List<Stockpile> AllStockpiles() => _stockpiles.Values.ToList();

		/// <summary>
		/// Find the nearest non-depleted source of a given type.
		/// </summary>
		public static ResourceSource NearestSource( ResourceType type, Vector3 from )
		{
			ResourceSource best = null;
			float bestDist = float.MaxValue;
			foreach ( var s in _sources.Values )
			{
				if ( s.Type != type || s.IsDepleted ) continue;
				float d = ( s.Position - from ).LengthSquared;
				if ( d < bestDist ) { bestDist = d; best = s; }
			}
			return best;
		}

		/// <summary>
		/// Find the nearest stockpile that has space available.
		/// </summary>
		public static Stockpile NearestStockpileWithSpace( Vector3 from, int needed = 1 )
		{
			Stockpile best = null;
			float bestDist = float.MaxValue;
			foreach ( var s in _stockpiles.Values )
			{
				if ( s.Available < needed ) continue;
				float d = ( s.Position - from ).LengthSquared;
				if ( d < bestDist ) { bestDist = d; best = s; }
			}
			return best;
		}

		/// <summary>
		/// Find the nearest stockpile that has at least the requested
		/// amount of a specific resource type.
		/// </summary>
		public static Stockpile NearestStockpileWithResource( ResourceType type, Vector3 from, int needed = 1 )
		{
			Stockpile best = null;
			float bestDist = float.MaxValue;
			foreach ( var s in _stockpiles.Values )
			{
				if ( s.Count( type ) < needed ) continue;
				float d = ( s.Position - from ).LengthSquared;
				if ( d < bestDist ) { bestDist = d; best = s; }
			}
			return best;
		}

		/// <summary>
		/// Claim a resource: reserve space on a stockpile for a deposit,
		/// or reserve a withdrawal quantity. Returns a typed result.
		/// </summary>
		public static (bool success, string detail) ClaimResource(
			string npcName, ResourceType type, int amount, Vector3? nearPos = null )
		{
			if ( amount <= 0 )
				return ( false, "amount must be positive" );

			// Claim for deposit: find a stockpile with space
			var pile = nearPos.HasValue
				? NearestStockpileWithSpace( nearPos.Value, amount )
				: _stockpiles.Values.FirstOrDefault( s => s.Available >= amount );
			if ( pile == null )
				return ( false, "no stockpile with sufficient space" );

			if ( !pile.Reserve( npcName, amount ) )
				return ( false, $"stockpile {pile.Id} reservation failed" );

			return ( true, $"reserved {amount} {type} on {pile.Id}" );
		}

		/// <summary>
		/// Release a previously claimed resource reservation.
		/// </summary>
		public static (bool success, string detail) ReleaseResource(
			string npcName, int amount )
		{
			bool any = false;
			foreach ( var pile in _stockpiles.Values )
			{
				if ( pile.Reservations.ContainsKey( npcName ) )
				{
					pile.ReleaseReservation( npcName, amount );
					any = true;
				}
			}
			return any
				? ( true, $"released {amount} reservation" )
				: ( false, "no matching reservation" );
		}

		/// <summary> Clear all sources and stockpiles (for reset). </summary>
		public static void Clear()
		{
			_sources.Clear();
			_stockpiles.Clear();
			_initialized = false;
		}

		/// <summary> Diagnostic summary. </summary>
		public static string Summary()
		{
			var sb = new System.Text.StringBuilder();
			sb.AppendLine( $"Lute: ResourceRegistry — {_sources.Count} sources, {_stockpiles.Count} stockpiles" );
			foreach ( var s in _sources.Values )
				sb.AppendLine( $"  Source: {s.Summary}" );
			foreach ( var s in _stockpiles.Values )
				sb.AppendLine( $"  Stockpile: {s.Summary}" );
			return sb.ToString();
		}

		[ConCmd( "resources" )]
		static void ResourcesCmd()
		{
			Log.Info( Summary() );
		}
	}
}
