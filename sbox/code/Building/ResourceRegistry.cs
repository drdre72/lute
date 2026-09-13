using System;
using System.Collections.Generic;
using System.Linq;
using Sandbox;
using Lute.Items;
using Lute.Farming;

namespace Lute.Building
{
	// ── Note on the unified economy ──
	//
	// Gate 2c collapsed the parallel "ResourceType" vocabulary into the
	// existing physical ItemType / LuteInventory / ResourceNode / CraftingBench
	// economy. There is now ONE type system (ItemType), ONE inventory
	// abstraction (LuteInventory), ONE source abstraction (ResourceNode,
	// adapted by ResourceSource), and ONE crafting system (CraftingBench,
	// wrapped later by WorkstationRegistry).
	//
	// Stockpile is now backed by a real LuteInventory, so material lives in
	// a physical 30-slot/500-stack component with tool durability, stacking,
	// and serialization — not a data-class dictionary that can teleport or
	// double-promise stock. Outgoing reservations (reserve specific
	// ItemType × amount for a future withdrawal) are tracked separately from
	// incoming capacity reservations (reserve room for a future deposit), so
	// two haulers cannot both be promised the same 20 bricks.

	/// <summary>
	/// A reservable physical stockpile backed by a real
	/// <see cref="LuteInventory"/>. Materials deposited here live in actual
	/// inventory slots with stack limits and tool durability — no
	/// teleporting, no double-promising.
	///
	/// Two kinds of reservations are tracked, per PR #6 §5 and the professor's
	/// Gate 2c hardening notes:
	/// <list type="bullet">
	/// <item><b>Incoming</b> (deposit room): NPC → amount. Reserves free
	/// capacity for a future deposit. Prevents over-accepting deliveries.</item>
	/// <item><b>Outgoing</b> (withdrawal hold): NPC → (ItemType, amount).
	/// Reserves specific stock for a future withdrawal. Prevents two haulers
	/// being promised the same 20 bricks.</item>
	/// </list>
	/// </summary>
	public sealed class Stockpile
	{
		/// <summary> Unique stockpile id (e.g. "stockyard_0"). </summary>
		public string Id { get; init; }

		/// <summary> World position. </summary>
		public Vector3 Position { get; init; }

		/// <summary> OBB half-extents for spatial queries. </summary>
		public Vector3 HalfExtents { get; init; }

		/// <summary> The real inventory backing this stockpile. </summary>
		public LuteInventory Inventory { get; init; }

		/// <summary> Maximum units this stockpile can hold (capacity in item units). </summary>
		public int Capacity { get; init; }

		/// <summary> Total units currently stored across all slots. </summary>
		public int Used => Inventory == null ? 0 : CountAll();

		/// <summary> Available space in units. </summary>
		public int Available => Capacity - Used;

		/// <summary>
		/// Incoming reservations: NPC name -> reserved units of deposit room.
		/// </summary>
		public Dictionary<string, int> IncomingReservations { get; } = new();

		/// <summary> Total incoming reserved units. </summary>
		public int IncomingReservedTotal => IncomingReservations.Values.Sum();

		/// <summary>
		/// Outgoing reservations: NPC name -> (ItemType, amount) list.
		/// Stored as a flat list so multiple resource types can be held for
		/// one NPC (e.g. a hauler reserving 20 brick + 10 plank).
		/// </summary>
		public Dictionary<string, List<(ItemType Type, int Amount)>> OutgoingReservations { get; } = new();

		/// <summary> Visual placeholder GameObject. </summary>
		public GameObject VisualGo { get; set; }

		public Stockpile( string id, Vector3 pos, Vector3 halfExtents, int capacity, LuteInventory inventory )
		{
			Id = id;
			Position = pos;
			HalfExtents = halfExtents;
			Capacity = capacity;
			Inventory = inventory;
		}

		int CountAll()
		{
			int total = 0;
			for ( int i = 0; i < Inventory.SlotCount; i++ )
				if ( Inventory.Slots[i].Count > 0 )
					total += Inventory.Slots[i].Count;
			return total;
		}

		/// <summary> How much of a type is stored here (physical stock). </summary>
		public int Count( ItemType type ) => Inventory?.CountItem( type ) ?? 0;

		/// <summary>
		/// How much of a type is physically present AND not reserved outgoing
		/// by someone else. This is what a new hauler can actually promise to
		/// withdraw.
		/// </summary>
		public int AvailableOutgoing( ItemType type, string forNpc )
		{
			int physical = Count( type );
			int reservedByOthers = 0;
			foreach ( var kv in OutgoingReservations )
			{
				if ( kv.Key == forNpc ) continue;
				foreach ( var r in kv.Value )
					if ( r.Type == type ) reservedByOthers += r.Amount;
			}
			return Math.Max( 0, physical - reservedByOthers );
		}

		// ── Incoming (deposit room) reservations ──

		/// <summary>
		/// Reserve deposit room. Returns true if the reservation was
		/// created, false if insufficient available space.
		/// </summary>
		public bool ReserveIncoming( string npcName, int amount )
		{
			if ( amount <= 0 ) return false;
			int already = IncomingReservations.GetValueOrDefault( npcName );
			int availableForReserve = Available - ( IncomingReservedTotal - already );
			if ( amount > availableForReserve ) return false;
			IncomingReservations[npcName] = already + amount;
			return true;
		}

		/// <summary> Release an incoming reservation (full or partial). </summary>
		public void ReleaseIncoming( string npcName, int amount )
		{
			if ( !IncomingReservations.ContainsKey( npcName ) ) return;
			IncomingReservations[npcName] -= amount;
			if ( IncomingReservations[npcName] <= 0 )
				IncomingReservations.Remove( npcName );
		}

		// ── Outgoing (withdrawal hold) reservations ──

		/// <summary>
		/// Reserve specific stock for a future withdrawal by a hauler.
		/// Returns true if the stock is physically present and not already
		/// reserved outgoing by another NPC.
		/// </summary>
		public bool ReserveOutgoing( string npcName, ItemType type, int amount )
		{
			if ( amount <= 0 ) return false;
			if ( AvailableOutgoing( type, npcName ) < amount ) return false;
			var list = OutgoingReservations.GetValueOrDefault( npcName ) ?? new List<(ItemType, int)>();
			list.Add( (type, amount) );
			OutgoingReservations[npcName] = list;
			return true;
		}

		/// <summary> Release an outgoing reservation (full or partial). </summary>
		public void ReleaseOutgoing( string npcName, ItemType type, int amount )
		{
			if ( !OutgoingReservations.TryGetValue( npcName, out var list ) ) return;
			int remaining = amount;
			for ( int i = list.Count - 1; i >= 0 && remaining > 0; i-- )
			{
				if ( list[i].Type != type ) continue;
				int take = Math.Min( list[i].Amount, remaining );
				var entry = list[i];
				entry.Amount -= take;
				list[i] = entry;
				remaining -= take;
				if ( list[i].Amount <= 0 ) list.RemoveAt( i );
			}
			if ( list.Count == 0 ) OutgoingReservations.Remove( npcName );
		}

		// ── Deposit / Withdraw (physical, via LuteInventory) ──

		/// <summary>
		/// Deposit materials into the physical inventory. Returns the
		/// amount actually deposited (may be less than requested if near
		/// capacity or slot limits). Does NOT touch reservations — the
		/// caller is responsible for releasing the matching incoming
		/// reservation after a successful deposit.
		/// </summary>
		public int Deposit( ItemType type, int amount )
		{
			if ( amount <= 0 || Inventory == null ) return 0;
			int canFit = Math.Min( amount, Available );
			if ( canFit <= 0 ) return 0;
			int leftover = Inventory.AddItem( type, canFit );
			return canFit - leftover;
		}

		/// <summary>
		/// Withdraw materials from the physical inventory. Returns the
		/// amount actually withdrawn (may be less if insufficient stock).
		/// Does NOT touch reservations — the caller must hold an outgoing
		/// reservation for this material and release it after withdrawing.
		/// </summary>
		public int Withdraw( ItemType type, int amount )
		{
			if ( amount <= 0 || Inventory == null ) return 0;
			int have = Count( type );
			int canWithdraw = Math.Min( amount, have );
			if ( canWithdraw <= 0 ) return 0;
			return Inventory.RemoveItem( type, canWithdraw ) ? canWithdraw : 0;
		}

		public string Summary =>
			$"{Id} @ {Position} used={Used}/{Capacity} inResv={IncomingReservedTotal}" +
			( OutgoingReservations.Count > 0
				? " outResv=[" + string.Join( ", ",
					OutgoingReservations.Select( kv => $"{kv.Key}:{string.Join( "+", kv.Value.Select( r => $"{r.Type}x{r.Amount}" ) )}" ) ) + "]"
				: "" );
	}

	/// <summary>
	/// A physically located source of a raw resource. Adapts an existing
	/// <see cref="ResourceNode"/> component (the physical gather point with
	/// tool checks, yield, and respawn) into the registry's spatial query
	/// surface. The source itself is not reservable — multiple NPCs can
	/// gather simultaneously — but the yield is tracked so depletion is
	/// deterministic.
	///
	/// Per PR #6 §5: "Resource source interface" — sources are world-side
	/// semantic contracts, not NPC-side logic.
	/// </summary>
	public sealed class ResourceSource
	{
		/// <summary> Unique source id (e.g. "tree_cluster_0"). </summary>
		public string Id { get; init; }

		/// <summary> The physical node this source adapts. May be null for
		/// purely synthetic sources (e.g. a cluster registered by position). </summary>
		public ResourceNode Node { get; init; }

		/// <summary> What this source produces. </summary>
		public ItemType Type { get; init; }

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

		/// <summary> Optional: required tool (ItemType, or Clay for "none"). </summary>
		public ItemType RequiredTool { get; init; } = ItemType.Clay;

		/// <summary> Visual placeholder GameObject. </summary>
		public GameObject VisualGo { get; set; }

		public ResourceSource( string id, ItemType type, Vector3 pos,
			int totalYield, int yieldPerGather = 10, float radius = 200f,
			NpcCapability requiredCapability = NpcCapability.None,
			ItemType requiredTool = ItemType.Clay,
			ResourceNode node = null )
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
			Node = node;
		}

		/// <summary>
		/// Attempt to gather from this source into an inventory. Returns
		/// the amount actually gathered (may be less than YieldPerGather if
		/// nearly depleted). Returns 0 if depleted. If a physical
		/// <see cref="Node"/> is attached, delegates to it (which performs
		/// the real tool check and writes into the inventory); otherwise
		/// performs a synthetic gather that still respects depletion.
		/// </summary>
		public int Gather( LuteInventory inventory )
		{
			if ( IsDepleted ) return 0;
			if ( Node != null && inventory != null )
			{
				// Delegate to the physical node — it does tool checks,
				// durability, and writes into the inventory directly.
				int before = inventory.CountItem( Type );
				int gathered = Node.Gather( inventory );
				// Keep our depletion counter roughly in sync with the node.
				int actual = inventory.CountItem( Type ) - before;
				RemainingYield = Math.Max( 0, RemainingYield - actual );
				return actual > 0 ? actual : gathered;
			}
			// Synthetic gather (no physical node) — still deterministic.
			int amount = Math.Min( YieldPerGather, RemainingYield );
			RemainingYield -= amount;
			if ( inventory != null )
			{
				int leftover = inventory.AddItem( Type, amount );
				amount -= leftover;
			}
			return amount;
		}

		public string Summary =>
			$"{Id} ({Type}) @ {Position} yield={RemainingYield}/{TotalYield}" +
			( IsDepleted ? " DEPLETED" : "" ) +
			( Node != null ? " [node]" : "" );
	}

	/// <summary>
	/// Authoritative registry of all resource sources and stockpiles in
	/// the world. NPCs query this to find where to gather, where to
	/// deposit, and where to withdraw materials. The
	/// NpcActionDispatcher routes ClaimResource/ReleaseResource intents
	/// here.
	///
	/// This is the single resource authority — there is no second
	/// resource tracking system (per PR #6: "reuse existing authorities,
	/// do not create a second scheduler or spatial authority").
	/// </summary>
	public static class ResourceRegistry
	{
		static readonly Dictionary<string, ResourceSource> _sources = new();
		static readonly Dictionary<string, Stockpile> _stockpiles = new();

		/// <summary> Register a resource source. </summary>
		public static void RegisterSource( ResourceSource source )
		{
			if ( source != null ) _sources[source.Id] = source;
		}

		/// <summary> Register a stockpile. </summary>
		public static void RegisterStockpile( Stockpile stockpile )
		{
			if ( stockpile != null ) _stockpiles[stockpile.Id] = stockpile;
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
		public static ResourceSource NearestSource( ItemType type, Vector3 from )
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
		/// Find the nearest stockpile that has free capacity (after
		/// accounting for incoming reservations).
		/// </summary>
		public static Stockpile NearestStockpileWithSpace( Vector3 from, int needed = 1 )
		{
			Stockpile best = null;
			float bestDist = float.MaxValue;
			foreach ( var s in _stockpiles.Values )
			{
				int availableForNew = s.Available - s.IncomingReservedTotal;
				if ( availableForNew < needed ) continue;
				float d = ( s.Position - from ).LengthSquared;
				if ( d < bestDist ) { bestDist = d; best = s; }
			}
			return best;
		}

		/// <summary>
		/// Find the nearest stockpile that has at least the requested
		/// amount of a specific item type available for outgoing
		/// (physical stock minus reservations held by other NPCs).
		/// </summary>
		public static Stockpile NearestStockpileWithResource( ItemType type, Vector3 from,
			int needed = 1, string forNpc = null )
		{
			Stockpile best = null;
			float bestDist = float.MaxValue;
			foreach ( var s in _stockpiles.Values )
			{
				if ( s.AvailableOutgoing( type, forNpc ?? "" ) < needed ) continue;
				float d = ( s.Position - from ).LengthSquared;
				if ( d < bestDist ) { bestDist = d; best = s; }
			}
			return best;
		}

		// ── Reservation APIs ──

		/// <summary>
		/// Claim deposit room on a stockpile (incoming reservation).
		/// Used by a hauler about to deliver material.
		/// </summary>
		public static (bool success, string detail) ClaimResource(
			string npcName, ItemType type, int amount, Vector3? nearPos = null )
		{
			if ( amount <= 0 )
				return ( false, "amount must be positive" );

			var pile = nearPos.HasValue
				? NearestStockpileWithSpace( nearPos.Value, amount )
				: _stockpiles.Values.FirstOrDefault( s => s.Available - s.IncomingReservedTotal >= amount );
			if ( pile == null )
				return ( false, "no stockpile with sufficient space" );

			if ( !pile.ReserveIncoming( npcName, amount ) )
				return ( false, $"stockpile {pile.Id} reservation failed" );

			return ( true, $"reserved {amount} {type} on {pile.Id}" );
		}

		/// <summary>
		/// Release a previously claimed incoming reservation.
		/// </summary>
		public static (bool success, string detail) ReleaseResource(
			string npcName, int amount )
		{
			bool any = false;
			foreach ( var pile in _stockpiles.Values )
			{
				if ( pile.IncomingReservations.ContainsKey( npcName ) )
				{
					pile.ReleaseIncoming( npcName, amount );
					any = true;
				}
			}
			return any
				? ( true, $"released {amount} reservation" )
				: ( false, "no matching reservation" );
		}

		/// <summary>
		/// Reserve specific outgoing stock for a hauler about to withdraw.
		/// This is the controlled-failure guard: two haulers cannot both be
		/// promised the same 20 bricks.
		/// </summary>
		public static (bool success, string detail) ReserveOutgoing(
			string npcName, ItemType type, int amount, Vector3? nearPos = null )
		{
			if ( amount <= 0 )
				return ( false, "amount must be positive" );

			var pile = nearPos.HasValue
				? NearestStockpileWithResource( type, nearPos.Value, amount, npcName )
				: _stockpiles.Values.FirstOrDefault( s => s.AvailableOutgoing( type, npcName ) >= amount );
			if ( pile == null )
				return ( false, $"no stockpile has {amount} {type} available" );

			if ( !pile.ReserveOutgoing( npcName, type, amount ) )
				return ( false, $"stockpile {pile.Id} outgoing reservation failed" );

			return ( true, $"reserved {amount} {type} outgoing on {pile.Id}" );
		}

		/// <summary>
		/// Release an outgoing reservation.
		/// </summary>
		public static (bool success, string detail) ReleaseOutgoing(
			string npcName, ItemType type, int amount )
		{
			bool any = false;
			foreach ( var pile in _stockpiles.Values )
			{
				if ( pile.OutgoingReservations.ContainsKey( npcName ) )
				{
					pile.ReleaseOutgoing( npcName, type, amount );
					any = true;
				}
			}
			return any
				? ( true, $"released {amount} {type} outgoing" )
				: ( false, "no matching outgoing reservation" );
		}

		// ── Atomic transfer ──

		/// <summary>
		/// Atomically transfer material from one stockpile to another.
		/// Withdraws from <paramref name="from"/>, deposits into
		/// <paramref name="to"/>. If the destination accepts less than was
		/// withdrawn, the difference is rolled back into the source.
		/// Returns the amount actually transferred. Never creates or
		/// destroys material — conservation is invariant.
		/// </summary>
		public static (int transferred, string detail) Transfer(
			Stockpile from, Stockpile to, ItemType type, int amount )
		{
			if ( amount <= 0 || from == null || to == null )
				return ( 0, "invalid transfer parameters" );
			if ( from == to )
				return ( 0, "source and destination are the same stockpile" );

			int withdrawn = from.Withdraw( type, amount );
			if ( withdrawn <= 0 )
				return ( 0, $"nothing to withdraw from {from.Id}" );

			int deposited = to.Deposit( type, withdrawn );
			int lost = withdrawn - deposited;

			// Roll back any undeposited material into the source so we
			// never create or destroy stock.
			if ( lost > 0 )
			{
				int rolled = from.Deposit( type, lost );
				// If even the rollback fails (source somehow full), we have
				// a real conservation problem — log it loudly.
				if ( rolled < lost )
					Log.Warning( $"Lute: ResourceRegistry conservation loss — {lost - rolled} {type} could not be rolled back to {from.Id}" );
			}

			return ( deposited, deposited == withdrawn
				? $"transferred {deposited} {type} {from.Id} -> {to.Id}"
				: $"transferred {deposited}/{withdrawn} {type} {from.Id} -> {to.Id} (rolled back {lost})" );
		}

		/// <summary> Clear all sources and stockpiles (for reset). </summary>
		public static void Clear()
		{
			_sources.Clear();
			_stockpiles.Clear();
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
