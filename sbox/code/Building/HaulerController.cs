using System;
using System.Linq;
using Sandbox;
using Lute.Items;

namespace Lute.Building
{
	/// <summary>
	/// Physical hauler NPC controller. Implements the Gate 2d hauler
	/// work loop as a finite-state machine:
	///
	/// <code>
	/// Idle
	///   ↓ claim haul job (LogisticsBoard.ClaimNextJob — gated by Haul capability)
	/// WalkingToSource
	///   ↓ arrive at source stockpile/resource source
	/// Pickup
	///   ↓ reserve outgoing + withdraw into NPC LuteInventory
	/// WalkingToDestination
	///   ↓ arrive at destination stockpile/build site
	/// Drop
	///   ↓ deposit from NPC LuteInventory + commit job (atomic transfer)
	///   ↓ release reservations
	/// Idle (loop)
	/// </code>
	///
	/// No material teleports. The hauler physically carries material in
	/// its own <see cref="LuteInventory"/> from source to destination.
	/// If interrupted (job fails, source depleted, destination full),
	/// the hauler rolls back any picked-up material and returns to idle.
	///
	/// Movement uses the same PlayerController + NavMeshAgent + WishVelocity
	/// pattern as <see cref="NPCBuilderController"/> and
	/// <see cref="VillageBuilderController"/>.
	/// </summary>
	public sealed class HaulerController : Component
	{
		[RequireComponent] public PlayerController Controller { get; set; }
		[RequireComponent] public NavMeshAgent NavAgent { get; set; }

		/// <summary> The hauler's inventory for carrying material. </summary>
		[Property] public LuteInventory Inventory { get; set; }

		/// <summary> Move speed in units/sec (~5 m/s). </summary>
		[Property] public float WalkSpeed { get; set; } = 5f * 39.37f;

		/// <summary> Stop distance from a walk target (units). </summary>
		[Property] public float StopRadius { get; set; } = 80f;

		/// <summary> NPC name for director/logistics registration. </summary>
		[Property] public string NpcName { get; set; } = "Hauler";

		/// <summary> Use NavMesh for pathfinding when available. </summary>
		[Property] public bool UseNavMesh { get; set; } = true;

		/// <summary> Hauler state machine. </summary>
		public enum HaulerState
		{
			Idle,
			WalkingToSource,
			Pickup,
			WalkingToDestination,
			Drop,
			Done,
		}

		[Property] public HaulerState State { get; set; } = HaulerState.Idle;

		/// <summary> Currently claimed haul job (null when idle). </summary>
		LogisticsJob _job;

		/// <summary> Builder ID assigned by the director for this hauler. </summary>
		int _builderId = -1;

		float _stateTimer;
		float _logTimer;
		bool _usingNavMesh;
		bool _navMeshDiagLogged;

		protected override void OnStart()
		{
			Log.Info( $"Lute: HaulerController '{NpcName}' started at {WorldPosition}." );

			Inventory ??= Components.Get<LuteInventory>();
			if ( Inventory is null )
			{
				Inventory = Components.Create<LuteInventory>();
			}

			// Register with the director as a hauler so
			// LogisticsBoard.ClaimNextJob can verify NpcCapability.Haul.
			// We use a negative builder ID range for haulers to avoid
			// colliding with VillageBuilder IDs (which start at 0).
			_builderId = -1 - Math.Abs( GameObject.Id.GetHashCode() );
			ConstructionDirector.RegisterBuilder( _builderId, NpcName, professionId: "hauler" );
			Log.Info( $"Lute: HaulerController '{NpcName}' registered as hauler (builderId={_builderId})." );
		}

		protected override void OnUpdate()
		{
			_logTimer += Time.Delta;

			switch ( State )
			{
				case HaulerState.Idle:
				{
					_stateTimer += Time.Delta;
					if ( _stateTimer > 0.5f )
					{
						_stateTimer = 0f;
						TryClaimJob();
					}
					break;
				}

				case HaulerState.WalkingToSource:
				{
					if ( _job == null ) { ResetToIdle(); break; }
					SteerToward( _job.FromPosition );
					if ( WithinStopRadius( _job.FromPosition ) )
					{
						Stop();
						State = HaulerState.Pickup;
						_stateTimer = 0f;
						Log.Info( $"Lute: Hauler '{NpcName}' reached source {_job.FromId} → Pickup." );
					}
					break;
				}

				case HaulerState.Pickup:
				{
					if ( _job == null ) { ResetToIdle(); break; }
					DoPickup();
					break;
				}

				case HaulerState.WalkingToDestination:
				{
					if ( _job == null ) { ResetToIdle(); break; }
					SteerToward( _job.ToPosition );
					if ( WithinStopRadius( _job.ToPosition ) )
					{
						Stop();
						State = HaulerState.Drop;
						_stateTimer = 0f;
						Log.Info( $"Lute: Hauler '{NpcName}' reached destination {_job.ToId} → Drop." );
					}
					break;
				}

				case HaulerState.Drop:
				{
					if ( _job == null ) { ResetToIdle(); break; }
					DoDrop();
					break;
				}

				case HaulerState.Done:
				{
					_job = null;
					State = HaulerState.Idle;
					_stateTimer = 0f;
					break;
				}
			}
		}

		// ── State transitions ──

		void TryClaimJob()
		{
			var job = LogisticsBoard.ClaimNextJob( NpcName );
			if ( job == null )
				return;

			_job = job;
			Log.Info( $"Lute: Hauler '{NpcName}' claimed job {job.Id}: {job.ItemType}x{job.Amount} {job.FromId} -> {job.ToId}." );

			// Start the job (verifies assigned hauler).
			var (ok, detail) = LogisticsBoard.StartJob( job.Id, NpcName );
			if ( !ok )
			{
				Log.Warning( $"Lute: Hauler '{NpcName}' StartJob failed: {detail}" );
				_job = null;
				return;
			}

			State = HaulerState.WalkingToSource;
			_stateTimer = 0f;
		}

		void DoPickup()
		{
			// Reserve outgoing + withdraw from the source into the NPC inventory.
			var job = _job;

			if ( job.FromId.StartsWith( "stockpile:" ) )
			{
				var pile = ResourceRegistry.GetStockpile( job.FromId.Substring( 10 ) );
				if ( pile == null )
				{
					Log.Warning( $"Lute: Hauler '{NpcName}' source stockpile not found: {job.FromId}" );
					FailJob();
					return;
				}

				// Reserve outgoing so no other hauler grabs the same stock.
				var (resOk, resDetail) = ResourceRegistry.ReserveOutgoing(
					NpcName, job.ItemType, job.Amount, pile.Position );
				if ( !resOk )
				{
					Log.Warning( $"Lute: Hauler '{NpcName}' reserve outgoing failed: {resDetail}" );
					FailJob();
					return;
				}

				// Withdraw into NPC inventory.
				int withdrawn = pile.Withdraw( job.ItemType, job.Amount );
				if ( withdrawn <= 0 )
				{
					Log.Warning( $"Lute: Hauler '{NpcName}' nothing to withdraw from {pile.Id}" );
					ResourceRegistry.ReleaseOutgoing( NpcName, job.ItemType, job.Amount );
					FailJob();
					return;
				}

				int leftover = Inventory.AddItem( job.ItemType, withdrawn );
				if ( leftover > 0 )
				{
					// NPC inventory full — put back what didn't fit.
					pile.Deposit( job.ItemType, leftover );
					Log.Warning( $"Lute: Hauler '{NpcName}' inventory full, returned {leftover} {job.ItemType} to {pile.Id}" );
				}

				Log.Info( $"Lute: Hauler '{NpcName}' picked up {withdrawn - leftover} {job.ItemType} from {pile.Id}." );
			}
			else if ( job.FromId.StartsWith( "source:" ) )
			{
				var src = ResourceRegistry.GetSource( job.FromId.Substring( 7 ) );
				if ( src == null )
				{
					Log.Warning( $"Lute: Hauler '{NpcName}' source not found: {job.FromId}" );
					FailJob();
					return;
				}

				// Gather from the resource source into NPC inventory.
				int gathered = src.Gather( Inventory );
				if ( gathered <= 0 )
				{
					Log.Warning( $"Lute: Hauler '{NpcName}' nothing to gather from {src.Id}" );
					FailJob();
					return;
				}

				Log.Info( $"Lute: Hauler '{NpcName}' gathered {gathered} {job.ItemType} from {src.Id}." );
			}
			else
			{
				Log.Warning( $"Lute: Hauler '{NpcName}' unknown source kind: {job.FromId}" );
				FailJob();
				return;
			}

			State = HaulerState.WalkingToDestination;
			_stateTimer = 0f;
		}

		void DoDrop()
		{
			var job = _job;
			int carrying = Inventory.CountItem( job.ItemType );

			if ( carrying <= 0 )
			{
				Log.Warning( $"Lute: Hauler '{NpcName}' arrived at destination with no {job.ItemType} — failing job." );
				FailJob();
				return;
			}

			if ( job.ToId.StartsWith( "stockpile:" ) )
			{
				var pile = ResourceRegistry.GetStockpile( job.ToId.Substring( 10 ) );
				if ( pile == null )
				{
					Log.Warning( $"Lute: Hauler '{NpcName}' destination stockpile not found: {job.ToId}" );
					FailJob();
					return;
				}

				// Remove from NPC inventory, deposit into stockpile.
				Inventory.RemoveItem( job.ItemType, carrying );
				int deposited = pile.Deposit( job.ItemType, carrying );
				if ( deposited < carrying )
				{
					// Stockpile full — put the remainder back into NPC inventory.
					int remainder = carrying - deposited;
					Inventory.AddItem( job.ItemType, remainder );
					Log.Warning( $"Lute: Hauler '{NpcName}' destination {pile.Id} full, kept {remainder} {job.ItemType}." );
				}

				Log.Info( $"Lute: Hauler '{NpcName}' dropped {deposited} {job.ItemType} at {pile.Id}." );
			}
			else if ( job.ToId.StartsWith( "task:" ) )
			{
				// Deliver to build site: remove from NPC inventory, credit
				// the task's material requirement via CompleteJob.
				Inventory.RemoveItem( job.ItemType, carrying );

				// CompleteJob handles the task credit + rollback if the
				// task has no matching requirement (material goes back
				// to the source). Since we already removed from NPC
				// inventory, we need to temporarily re-add it so
				// CompleteJob's rollback path can deposit it back.
				// Instead, we credit the task directly here and let
				// CompleteJob just mark completion.
				var task = ConstructionDirector.GetTask( job.ToId.Substring( 5 ) );
				if ( task?.MaterialRequirements != null )
				{
					bool credited = false;
					foreach ( var req in task.MaterialRequirements )
					{
						if ( req.Type == job.ItemType && !req.Satisfied )
						{
							int need = req.Amount - req.Delivered;
							int credit = Math.Min( carrying, need );
							int surplus = carrying - credit;
							req.Delivered += credit;
							credited = true;
							if ( surplus > 0 )
							{
								// Put surplus back into NPC inventory —
								// it will be returned to a stockpile on
								// the next idle cycle (or just dropped).
								Inventory.AddItem( job.ItemType, surplus );
								Log.Info( $"Lute: Hauler '{NpcName}' delivered {credit} {job.ItemType} to task {task.Id}, kept {surplus} surplus." );
							}
							else
							{
								Log.Info( $"Lute: Hauler '{NpcName}' delivered {credit} {job.ItemType} to task {task.Id}." );
							}
							break;
						}
					}
					if ( !credited )
					{
						// Task has no matching requirement — put material back.
						Inventory.AddItem( job.ItemType, carrying );
						Log.Warning( $"Lute: Hauler '{NpcName}' task {task.Id} has no unsatisfied {job.ItemType} requirement — keeping material." );
					}
				}
				else
				{
					Inventory.AddItem( job.ItemType, carrying );
					Log.Warning( $"Lute: Hauler '{NpcName}' task not found or no requirements — keeping material." );
				}
			}
			else
			{
				Log.Warning( $"Lute: Hauler '{NpcName}' unknown destination kind: {job.ToId}" );
				FailJob();
				return;
			}

			// Release any outgoing reservation held on the source.
			ResourceRegistry.ReleaseOutgoing( NpcName, job.ItemType, job.Amount );

			// Mark job complete.
			job.Status = LogisticsJobStatus.Completed;
			job.CompletedAt = SpatialBlackboard.CurrentTime;

			Log.Info( $"Lute: Hauler '{NpcName}' completed job {job.Id}." );
			State = HaulerState.Done;
			_stateTimer = 0f;
		}

		void FailJob()
		{
			if ( _job == null ) { ResetToIdle(); return; }

			// Roll back: return any carried material to the source.
			int carrying = Inventory.CountItem( _job.ItemType );
			if ( carrying > 0 )
			{
				Inventory.RemoveItem( _job.ItemType, carrying );
				if ( _job.FromId.StartsWith( "stockpile:" ) )
				{
					var pile = ResourceRegistry.GetStockpile( _job.FromId.Substring( 10 ) );
					pile?.Deposit( _job.ItemType, carrying );
				}
				else if ( _job.FromId.StartsWith( "source:" ) )
				{
					var src = ResourceRegistry.GetSource( _job.FromId.Substring( 7 ) );
					if ( src != null ) src.RemainingYield += carrying;
				}
			}

			// Release reservations.
			ResourceRegistry.ReleaseOutgoing( NpcName, _job.ItemType, _job.Amount );

			_job.Status = LogisticsJobStatus.Failed;
			Log.Warning( $"Lute: Hauler '{NpcName}' failed job {_job.Id}." );
			_job = null;
			ResetToIdle();
		}

		void ResetToIdle()
		{
			_job = null;
			State = HaulerState.Idle;
			_stateTimer = 0f;
		}

		// ── Movement (same pattern as NPCBuilderController) ──

		bool WithinStopRadius( Vector3 target )
		{
			var dist = Vector3.DistanceBetween(
				WorldPosition.WithZ( 0 ), target.WithZ( 0 ) );
			return dist < StopRadius;
		}

		void SteerToward( Vector3 target )
		{
			var toTarget = (target - WorldPosition).WithZ( 0 );
			var dist = toTarget.Length;
			if ( dist < 1f ) return;

			Controller.EyeAngles = new Angles( 0, toTarget.EulerAngles.yaw, 0 );

			if ( UseNavMesh && TrySteerWithNavMesh( target ) )
			{
				// NavMesh drove WishVelocity this tick.
			}
			else
			{
				Controller.WishVelocity = toTarget.Normal * WalkSpeed;
			}

			if ( _logTimer > 2f )
			{
				_logTimer = 0f;
				Log.Info( $"Lute: Hauler '{NpcName}' walking — pos={WorldPosition}, dist={dist:F1}, navmesh={_usingNavMesh}." );
			}
		}

		bool TrySteerWithNavMesh( Vector3 target )
		{
			_usingNavMesh = false;
			var nav = NavAgent;
			if ( nav is null || !nav.Enabled ) return false;

			var sceneNav = Scene?.NavMesh;
			if ( sceneNav is null || !sceneNav.IsEnabled ) return false;

			if ( !_navMeshDiagLogged )
			{
				_navMeshDiagLogged = true;
				var agentHit = sceneNav.GetClosestPoint( WorldPosition, 1000f );
				var targetHit = sceneNav.GetClosestPoint( target, 1000f );
				Log.Info( $"Lute: Hauler '{NpcName}' navmesh diag — agent={(agentHit.HasValue ? "on-mesh" : "off-mesh")}, target={(targetHit.HasValue ? "on-mesh" : "off-mesh")}." );
			}

			nav.MoveTo( target );
			var wish = nav.WishVelocity;
			// If NavMesh can't produce a useful velocity (agent off-mesh
			// or no path), fall back to direct steering.
			if ( wish.LengthSquared < 1f )
				return false;

			Controller.WishVelocity = wish;
			_usingNavMesh = true;
			return true;
		}

		void Stop()
		{
			Controller.WishVelocity = Vector3.Zero;
			NavAgent?.MoveTo( WorldPosition );
		}
	}
}
