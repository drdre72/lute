using System;
using System.Linq;
using Sandbox;
using Lute.Items;

namespace Lute.Building
{
	/// <summary>
	/// Physical gatherer NPC controller (Gate 3.3). Implements the
	/// source-gathering work loop as a finite-state machine:
	///
	/// <code>
	/// Idle
	///   ↓ find nearest non-depleted source matching this NPC's capability
	/// WalkingToSource
	///   ↓ arrive at resource source
	/// Gathering
	///   ↓ source.Gather( inventory ) repeatedly until inventory full or source depleted
	/// WalkingToStockpile
	///   ↓ arrive at nearest stockpile with space
	/// Depositing
	///   ↓ deposit gathered material from NPC LuteInventory into stockpile
	///   ↓ Idle (loop)
	/// </code>
	///
	/// No material teleports. The gatherer physically carries gathered
	/// material in its own <see cref="LuteInventory"/> from the source to
	/// the stockpile. This is the front end of the Gate 3.3 full loop:
	///
	/// <code>
	/// sources → gather → haul to stockpile → crafter processes → haul to site → build
	/// </code>
	///
	/// The gatherer only handles the first link (source → stockpile).
	/// Haulers, crafters, and builders handle the rest via the existing
	/// Gate 3.2 machinery.
	///
	/// Movement uses the same PlayerController + NavMeshAgent + WishVelocity
	/// pattern as <see cref="HaulerController"/> and
	/// <see cref="CrafterController"/>, with a direct-steering fallback when
	/// NavMesh velocity is zero (matching the crafter warp fix).
	/// </summary>
	public sealed class GathererController : Component
	{
		[RequireComponent] public PlayerController Controller { get; set; }
		[RequireComponent] public NavMeshAgent NavAgent { get; set; }

		/// <summary> The gatherer's inventory for carrying gathered material. </summary>
		[Property] public LuteInventory Inventory { get; set; }

		/// <summary> Move speed in units/sec (~5 m/s). </summary>
		[Property] public float WalkSpeed { get; set; } = 5f * 39.37f;

		/// <summary> Stop distance from a walk target (units). </summary>
		[Property] public float StopRadius { get; set; } = 80f;

		/// <summary> NPC name for logging. </summary>
		[Property] public string NpcName { get; set; } = "Gatherer";

		/// <summary>
		/// Profession id (e.g. "lumberjack", "quarryman"). Determines which
		/// resource types this gatherer will seek. The profession's
		/// capability map is read from <see cref="CapabilityRegistry"/>.
		/// </summary>
		[Property] public string ProfessionId { get; set; } = "lumberjack";

		/// <summary> Use NavMesh for pathfinding when available. </summary>
		[Property] public bool UseNavMesh { get; set; } = true;

		/// <summary> Max items to carry before returning to a stockpile. </summary>
		[Property] public int CarryCapacity { get; set; } = 30;

		/// <summary> Seconds spent gathering per gather action (dev speed). </summary>
		[Property] public float GatherInterval { get; set; } = 0.5f;

		/// <summary> Gatherer state machine. </summary>
		public enum GathererState
		{
			Idle,
			WalkingToSource,
			Gathering,
			WalkingToStockpile,
			Depositing,
		}

		[Property] public GathererState State { get; set; } = GathererState.Idle;

		/// <summary> The resource types this gatherer can harvest, derived
		/// from the profession's capabilities. Maps NpcCapability.GatherX
		/// to the ItemType that capability produces. </summary>
		static readonly System.Collections.Generic.Dictionary<NpcCapability, ItemType> CapabilityToItemType = new()
		{
			{ NpcCapability.GatherWood,   ItemType.Wood  },
			{ NpcCapability.GatherStone,  ItemType.Stone },
			{ NpcCapability.GatherOre,    ItemType.Ore   },
			{ NpcCapability.GatherClay,    ItemType.Clay  },
			{ NpcCapability.GatherStraw,  ItemType.Straw },
		};

		ResourceSource _targetSource;
		Stockpile _targetStockpile;
		ItemType _gatherType;
		float _stateTimer;
		float _logTimer;
		float _stuckTimer;
		bool _usingNavMesh;
		int _builderId = -1;

		protected override void OnStart()
		{
			Log.Info( $"Lute: GathererController '{NpcName}' started at {WorldPosition} (profession={ProfessionId})." );

			Inventory ??= Components.Get<LuteInventory>();
			if ( Inventory is null )
				Inventory = Components.Create<LuteInventory>();

			// Register with the director so it shows up in the workforce
			// log (gatherers don't claim construction tasks, but the
			// director tracks all registered workers).
			_builderId = -1 - Math.Abs( GameObject.Id.GetHashCode() );
			ConstructionDirector.RegisterBuilder( _builderId, NpcName, professionId: ProfessionId );
			Log.Info( $"Lute: GathererController '{NpcName}' registered as {ProfessionId} (builderId={_builderId})." );
		}

		protected override void OnUpdate()
		{
			_logTimer += Time.Delta;

			switch ( State )
			{
				case GathererState.Idle:
				{
					_stateTimer += Time.Delta;
					if ( _stateTimer > 0.5f )
					{
						_stateTimer = 0f;
						PickNextSource();
					}
					break;
				}

				case GathererState.WalkingToSource:
				{
					if ( _targetSource == null || _targetSource.IsDepleted )
					{
						ResetToIdle();
						break;
					}
					SteerToward( _targetSource.Position );
					if ( WithinStopRadius( _targetSource.Position ) )
					{
						Stop();
						State = GathererState.Gathering;
						_stateTimer = 0f;
						_stuckTimer = 0f;
						Log.Info( $"Lute: Gatherer '{NpcName}' arrived at source '{_targetSource.Id}' ({_gatherType}) → gathering." );
					}
					break;
				}

				case GathererState.Gathering:
				{
					if ( _targetSource == null || _targetSource.IsDepleted )
					{
						Log.Info( $"Lute: Gatherer '{NpcName}' source '{_targetSource?.Id}' depleted → heading to stockpile." );
						State = GathererState.WalkingToStockpile;
						_targetStockpile = null;
						_stateTimer = 0f;
						break;
					}
					_stateTimer += Time.Delta;
					if ( _stateTimer >= GatherInterval )
					{
						_stateTimer = 0f;
						int before = Inventory.CountItem( _gatherType );
						int gathered = _targetSource.Gather( Inventory );
						int actual = Inventory.CountItem( _gatherType ) - before;

						if ( actual > 0 )
						{
							Log.Info( $"Lute: Gatherer '{NpcName}' gathered {actual} {_gatherType} from '{_targetSource.Id}' (yield {_targetSource.RemainingYield}/{_targetSource.TotalYield})." );
						}

						int carrying = Inventory.CountItem( _gatherType );
						if ( carrying >= CarryCapacity || _targetSource.IsDepleted )
						{
							Log.Info( $"Lute: Gatherer '{NpcName}' carrying {carrying} {_gatherType} → heading to stockpile." );
							State = GathererState.WalkingToStockpile;
							_targetStockpile = null;
							_stateTimer = 0f;
							_stuckTimer = 0f;
						}
					}
					break;
				}

				case GathererState.WalkingToStockpile:
				{
					if ( _targetStockpile == null )
					{
						_targetStockpile = ResourceRegistry.NearestStockpileWithSpace(
							WorldPosition, Inventory.CountItem( _gatherType ) );
						if ( _targetStockpile == null )
						{
							Log.Warning( $"Lute: Gatherer '{NpcName}' no stockpile with space for {_gatherType} → waiting." );
							_stateTimer += Time.Delta;
							if ( _stateTimer > 3f )
							{
								_stateTimer = 0f;
								// Retry: drop at any stockpile.
								var any = ResourceRegistry.AllStockpiles().FirstOrDefault();
								if ( any != null ) _targetStockpile = any;
							}
							break;
						}
					}
					SteerToward( _targetStockpile.Position );
					if ( WithinStopRadius( _targetStockpile.Position ) )
					{
						Stop();
						State = GathererState.Depositing;
						_stateTimer = 0f;
						_stuckTimer = 0f;
					}
					break;
				}

				case GathererState.Depositing:
				{
					if ( _targetStockpile == null )
					{
						ResetToIdle();
						break;
					}
					int carrying = Inventory.CountItem( _gatherType );
					if ( carrying <= 0 )
					{
						ResetToIdle();
						break;
					}
					Inventory.RemoveItem( _gatherType, carrying );
					int deposited = _targetStockpile.Deposit( _gatherType, carrying );
					if ( deposited > 0 )
					{
						Log.Info( $"Lute: Gatherer '{NpcName}' deposited {deposited} {_gatherType} at '{_targetStockpile.Id}'." );
					}
					if ( deposited < carrying )
					{
						int remainder = carrying - deposited;
						Inventory.AddItem( _gatherType, remainder );
						Log.Warning( $"Lute: Gatherer '{NpcName}' stockpile '{_targetStockpile.Id}' full; {remainder} {_gatherType} kept." );
					}
					ResetToIdle();
					break;
				}
			}

			// Periodic movement diagnostic (same pattern as hauler/crafter).
			if ( _logTimer >= 10f && ( State == GathererState.WalkingToSource || State == GathererState.WalkingToStockpile ) )
			{
				_logTimer = 0f;
				Vector3 tgt = State == GathererState.WalkingToSource
					? _targetSource.Position
					: ( _targetStockpile?.Position ?? Vector3.Zero );
				float dist = WorldPosition.Distance( tgt );
				Log.Info( $"Lute: Gatherer '{NpcName}' walking — pos={WorldPosition}, dist={dist:F1}, navmesh={_usingNavMesh}." );
			}
		}

		// ── State transitions ──

		void PickNextSource()
		{
			// Determine which item types this profession can gather.
			var prof = CapabilityRegistry.Get( ProfessionId );
			if ( prof == null )
			{
				Log.Warning( $"Lute: Gatherer '{NpcName}' unknown profession '{ProfessionId}'." );
				return;
			}

			var gatherTypes = prof.Skills.Keys
				.Where( CapabilityToItemType.ContainsKey )
				.Select( c => CapabilityToItemType[c] )
				.ToList();

			if ( gatherTypes.Count == 0 )
			{
				Log.Warning( $"Lute: Gatherer '{NpcName}' profession '{ProfessionId}' has no gather capabilities." );
				return;
			}

			// Find the nearest non-depleted source of any gatherable type.
			ResourceSource best = null;
			float bestDist = float.MaxValue;
			foreach ( var type in gatherTypes )
			{
				var src = ResourceRegistry.NearestSource( type, WorldPosition );
				if ( src != null && !src.IsDepleted )
				{
					float d = WorldPosition.Distance( src.Position );
					if ( d < bestDist )
					{
						best = src;
						bestDist = d;
						_gatherType = type;
					}
				}
			}

			if ( best == null )
			{
				Log.Warning( $"Lute: Gatherer '{NpcName}' no non-depleted source for {string.Join( ", ", gatherTypes )}." );
				return;
			}

			_targetSource = best;
			State = GathererState.WalkingToSource;
			_stateTimer = 0f;
			_stuckTimer = 0f;
			Log.Info( $"Lute: Gatherer '{NpcName}' heading to source '{best.Id}' ({_gatherType}) at {best.Position}." );
		}

		void ResetToIdle()
		{
			State = GathererState.Idle;
			_stateTimer = 0f;
			_targetSource = null;
			_targetStockpile = null;
		}

		// ── Movement (same pattern as HaulerController/CrafterController) ──

		void SteerToward( Vector3 target )
		{
			// Warp to target if stuck for too long (matches crafter warp fix).
			_stuckTimer += Time.Delta;
			float warpThreshold = 15f;
			if ( _stuckTimer > warpThreshold )
			{
				Log.Info( $"Lute: Gatherer '{NpcName}' stuck for {_stuckTimer:F1}s → warping to {target}." );
				WorldPosition = target;
				_stuckTimer = 0f;
				return;
			}

			var toTarget = target - WorldPosition;
			toTarget.z = 0f;
			float dist = toTarget.Length;

			if ( UseNavMesh && NavAgent != null )
			{
				NavAgent.MoveTo( target );
				_usingNavMesh = NavAgent.Velocity.LengthSquared > 1f;

				// If NavMesh isn't producing useful velocity, fall back to
				// direct steering (matches HaulerController/CrafterController fix).
				if ( NavAgent.WishVelocity.LengthSquared < 1f )
				{
					_usingNavMesh = false;
					DirectSteer( toTarget, dist );
				}
				else if ( dist < StopRadius )
				{
					// NavMesh is moving us — let it finish the approach.
				}
				else
				{
					// Keep moving via NavMesh; reset stuck timer on progress.
					_stuckTimer = 0f;
				}
			}
			else
			{
				_usingNavMesh = false;
				DirectSteer( toTarget, dist );
			}
		}

		void DirectSteer( Vector3 toTarget, float dist )
		{
			if ( dist > 1f )
			{
				var dir = toTarget.Normal;
				WorldPosition += dir * WalkSpeed * Time.Delta;
				_stuckTimer = 0f;
			}
		}

		bool WithinStopRadius( Vector3 target )
		{
			var p = WorldPosition;
			var t = target;
			p.z = 0f; t.z = 0f;
			return p.Distance( t ) <= StopRadius;
		}

		void Stop()
		{
			if ( Controller != null )
				Controller.WishVelocity = Vector3.Zero;
		}
	}
}
