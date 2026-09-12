using System.Linq;
using Sandbox;

namespace Lute.Building
{
	using Lute.NLP;
	/// <summary>
	/// Body + AI controller for a <see cref="VillageBuilder"/>. A
	/// traditional finite-state machine that dresses a citizen body as
	/// a village builder, walks to the current build site, stands by
	/// while <see cref="VillageBuilder"/> places pieces, then walks to
	/// the next site when the current task completes. Loops until all
	/// village tasks are done.
	///
	/// Movement uses the proven <see cref="PlayerController"/> + Rigidbody
	/// velocity pattern. When a sibling <see cref="NavMeshAgent"/> is
	/// present and the scene's NavMesh is enabled/loaded, the agent
	/// pathfinds to the target. Otherwise it falls back to direct
	/// steering toward the world target.
	///
	/// When <see cref="UseBlackboard"/> is true, this NPC registers its
	/// position on the shared <see cref="SpatialBlackboard"/> every tick,
	/// claims a radius around the current build site before construction,
	/// and releases the claim when the task completes or the NPC is
	/// destroyed. Other NPCs check the blackboard before building so
	/// multiple builders don't stack on the same site.
	/// </summary>
	public sealed class VillageBuilderController : Component
	{
		[RequireComponent] public PlayerController Controller { get; set; }
		[RequireComponent] public NavMeshAgent NavAgent { get; set; }

		/// <summary> The village builder we work for. </summary>
		[Property] public VillageBuilder Builder { get; set; }

		/// <summary> Move speed in units/sec (~5 m/s). </summary>
		[Property] public float WalkSpeed { get; set; } = 5f * 39.37f;

		/// <summary> Stop distance from a build site (units). </summary>
		[Property] public float StopRadius { get; set; } = 120f;

		/// <summary> Use NavMesh for pathfinding when available. </summary>
		[Property] public bool UseNavMesh { get; set; } = true;

		/// <summary>
		/// Identity of this NPC on the <see cref="SpatialBlackboard"/>. If
		/// empty, the GameObject's name is used. Must be unique among builder
		/// NPCs so positions and claims don't collide.
		/// </summary>
		[Property] public string NpcName { get; set; } = "";

		/// <summary>
		/// Radius (inches) claimed around a build site while constructing.
		/// Other NPCs avoid building inside this radius. ~150in is about
		/// 3.8m, enough to cover a single wall segment plus standing room.
		/// </summary>
		[Property] public float ClaimRadius { get; set; } = 150f;

		/// <summary>
		/// If true, this NPC participates in the shared spatial blackboard:
		/// publishing its position every tick, claiming build sites, and
		/// releasing them on completion. Disable for purely decorative NPCs.
		/// </summary>
		[Property] public bool UseBlackboard { get; set; } = true;

		/// <summary>
		/// If true, this NPC registers with the ConversationManager and
		/// processes incoming NLP messages from other NPCs. Enables
		/// cooperative task negotiation and help requests.
		/// </summary>
		[Property] public bool UseCommunication { get; set; } = true;

		/// <summary>
		/// If true, this NPC registers with the ConstructionDirector and
		/// claims tasks through the authoritative transaction system
		/// (BlackboardRequest → BlackboardResult) instead of directly
		/// manipulating the SpatialBlackboard.
		/// </summary>
		[Property] public bool UseDirector { get; set; } = true;

		/// <summary> Builder ID for ConstructionDirector registration. </summary>
		[Property] public int BuilderId { get; set; } = -1;

		private BeliefModel _beliefs;
		private bool _registeredWithDirector;
		private bool _registeredWithConversation;
		private float _helpRequestCooldown;

		public enum NpcState
		{
			Idle,
			WalkingToSite,
			Building,
			WalkingToNextSite,
			VillageComplete
		}

		[Property] public NpcState State { get; set; } = NpcState.Idle;

		private float _stateTimer;
		private float _logTimer;
		private Vector3 _currentTarget;
		private int _buildingTaskIndex = -1; // tracks which task index we're standing at
		private string _activeDirectedTaskId; // exact DirectedTask.Id the NPC is currently building (director mode)
		private string _npcId;       // resolved blackboard identity
		private string _activeClaimId; // non-null while we hold a build-site claim
		private bool _npcIdResolved;
		private static int _fallbackIdCounter;

		protected override void OnStart()
		{
			Log.Info( $"Lute: VillageBuilderController '{GameObject.Name}' started." );

			if ( NavAgent is not null )
			{
				NavAgent.UpdatePosition = false;
				NavAgent.UpdateRotation = false;
			}

			ResolveNpcId();

			// Register with the ConversationManager for NLP communication
			if ( UseCommunication )
			{
				_beliefs = new BeliefModel( _npcId ) { Role = "builder", CurrentGoal = "build" };
				ConversationManager.Register( _npcId, _beliefs, "builder" );
				_registeredWithConversation = true;
				Log.Info( $"Lute: VillageBuilderController '{_npcId}' registered with ConversationManager." );
			}

			// Register with the ConstructionDirector for authoritative task scheduling.
			// Use the VillageBuilder's BuilderId (0,1,2,...) - not a hash - so the
			// controller and the builder share the same director identity.
			if ( UseDirector )
			{
				BuilderId = Builder?.BuilderId ?? 0;
				ConstructionDirector.RegisterBuilder( BuilderId, _npcId );
				_registeredWithDirector = true;
				Log.Info( $"Lute: VillageBuilderController '{_npcId}' registered with ConstructionDirector as builder {BuilderId}." );
			}

			// Subscribe to construction events for cooperative behavior
			if ( UseCommunication )
			{
				ConstructionEventBus.Subscribe( OnConstructionEvent );
			}
		}

		void ResolveNpcId()
		{
			_npcId = string.IsNullOrWhiteSpace( NpcName ) ? GameObject?.Name : NpcName;
			if ( string.IsNullOrWhiteSpace( _npcId ) )
			{
				// Name not available yet (e.g. OnStart ran before the GameObject
				// was fully initialized) — assign a stable fallback so the
				// blackboard never receives a null key.
				_fallbackIdCounter++;
				_npcId = $"villager_{_fallbackIdCounter}";
			}
			_npcIdResolved = true;
		}

		protected override void OnFixedUpdate()
		{
			if ( Builder is null )
				return;

			_stateTimer += Time.Delta;
			_logTimer += Time.Delta;

			// Keep the shared blackboard clock ticking and publish our position
			// every tick so other NPCs know where we are.
			if ( UseBlackboard )
			{
				if ( !_npcIdResolved )
					ResolveNpcId();

				SpatialBlackboard.Update( Time.Delta );
				SpatialBlackboard.UpdatePosition( _npcId, WorldPosition );
			}

			// Process incoming NLP messages from other NPCs
			if ( UseCommunication && _registeredWithConversation )
			{
				ConversationManager.ProcessIncoming( _npcId );
				_helpRequestCooldown -= Time.Delta;
			}

			// Update construction event bus clock
			ConstructionEventBus.Update( Time.Delta );

			switch ( State )
			{
				case NpcState.Idle:
					HandleIdle();
					break;
				case NpcState.WalkingToSite:
				case NpcState.WalkingToNextSite:
					HandleWalking();
					break;
				case NpcState.Building:
					HandleBuilding();
					break;
				case NpcState.VillageComplete:
					// Stand idle — village is done.
					Controller.WishVelocity = Vector3.Zero;
					ReleaseActiveClaim();
					break;
			}

			// Periodic status log (every 60s)
			if ( _logTimer >= 60f )
			{
				_logTimer = 0;
				LogVillageStatus();
			}
		}

		protected override void OnDestroy()
		{
			// Unsubscribe from construction events
			ConstructionEventBus.Unsubscribe( OnConstructionEvent );

			// Unregister from conversation system
			if ( _registeredWithConversation && !string.IsNullOrEmpty( _npcId ) )
				ConversationManager.Unregister( _npcId );

			// Deactivate from director
			if ( _registeredWithDirector )
				ConstructionDirector.DeactivateBuilder( BuilderId );

			// Always release our claim and announce departure so other NPCs
			// don't think the site is still reserved.
			ReleaseActiveClaim();
			if ( UseBlackboard && !string.IsNullOrEmpty( _npcId ) )
				SpatialBlackboard.PostMessage( _npcId, "", "done", "npc destroyed", WorldPosition );
		}
		/// <summary>
		/// Handle a construction event from the ConstructionEventBus.
		/// This is how conversation becomes a consequence of simulation:
		/// when a task is blocked or a reservation conflicts, the NPC
		/// can request help or negotiate with other NPCs.
		/// </summary>
		void OnConstructionEvent( ConstructionEvent evt )
		{
			if ( evt == null )
				return;

			switch ( evt.Type )
			{
				case ConstructionEventType.ReservationConflict:
					// Our reservation was blocked by another NPC — request help
					if ( evt.Actor == _npcId && _helpRequestCooldown <= 0 )
					{
						var blockedBy = evt.Parameters.TryGetValue( "blocked_by", out var b ) ? b : "unknown";
						ConversationManager.Send( _npcId, blockedBy,
							$"I'm blocked at {evt.TaskId}. Can you help or move?" );
						_helpRequestCooldown = 10f; // don't spam
					}
					break;

				case ConstructionEventType.TaskBlocked:
					// A task failed and is retrying — offer help if we're idle
					if ( State == NpcState.Idle || State == NpcState.VillageComplete )
					{
						ConversationManager.Send( _npcId, "",
							$"Task {evt.TaskId} is blocked. I can help." );
					}
					break;

				case ConstructionEventType.TaskCompleted:
					// Director mode: this is the authoritative completion signal.
					// The executor (VillageBuilder) completed the exact task we
					// were building. Transition to Idle so HandleIdle can pick up
					// the next claimed task.
					if ( UseDirector && evt.TaskId == _activeDirectedTaskId )
					{
						_activeDirectedTaskId = null;
						_buildingTaskIndex = -1;
						ReleaseActiveClaim();
						State = Builder.IsComplete
							? NpcState.VillageComplete
							: NpcState.Idle;
						_stateTimer = 0;
						Controller.WishVelocity = Vector3.Zero;
						Log.Info( $"Lute: VillageBuilderController '{_npcId}' task {evt.TaskId} completed (event-driven). Transitioning to {(Builder.IsComplete ? "VillageComplete" : "Idle")}." );
					}

					// Update task history and reputation for other builders
					if ( _beliefs != null && evt.Actor != null && evt.Actor != _npcId )
					{
						_beliefs.AdjustReputation( evt.Actor, +5 );
						var rep = _beliefs.GetOrCreateReputation( evt.Actor );
						rep.TasksCompletedObserved++;
					}
					break;

				case ConstructionEventType.TaskFailed:
					// Update task history and reputation
					if ( _beliefs != null && evt.Actor != null && evt.Actor != _npcId )
					{
						_beliefs.AdjustReputation( evt.Actor, -3 );
						var rep = _beliefs.GetOrCreateReputation( evt.Actor );
						rep.TasksFailedObserved++;
					}
					break;

				case ConstructionEventType.NpcRequestedHelp:
					// Another NPC requested help — offer if we're available
					if ( evt.Actor != _npcId && State != NpcState.Building )
					{
						ConversationManager.Send( _npcId, evt.Actor,
							"I can help with that. What do you need?" );
						if ( _beliefs != null )
						{
							var rep = _beliefs.GetOrCreateReputation( evt.Actor );
							rep.HelpRequestedObserved++;
						}
					}
					break;

				case ConstructionEventType.NpcOfferedHelp:
					// Someone offered help — accept if we're blocked
					if ( evt.Target == _npcId && State == NpcState.Building )
					{
						ConversationManager.Send( _npcId, evt.Actor,
							"Thank you. I'm blocked here." );
						if ( _beliefs != null )
						{
							_beliefs.AdjustReputation( evt.Actor, +2 );
							var rep = _beliefs.GetOrCreateReputation( evt.Actor );
							rep.HelpOfferedObserved++;
						}
					}
					break;
			}
		}

		void HandleIdle()
		{
			// Wait a moment, then start walking to the first/current build site
			if ( _stateTimer > 2f )
			{
				if ( Builder.IsComplete )
				{
					State = NpcState.VillageComplete;
					Log.Info( "Lute: VillageBuilderController — village already complete on start." );
					return;
				}

				if ( Builder.CurrentTask is not null )
				{
					_currentTarget = Builder.CurrentTask.Position;
					State = NpcState.WalkingToSite;
					_stateTimer = 0;
					Log.Info( $"Lute: VillageBuilderController walking to first site '{Builder.CurrentTask.Name}' at {_currentTarget}." );
				}
			}
		}

		void HandleWalking()
		{
			if ( Builder.CurrentTask is null || Builder.CurrentTask.Status == 2 )
			{
				// No current task (or current task is complete) — check if
				// village is complete, otherwise wait for the builder to
				// assign the next task.
				if ( Builder.IsComplete )
				{
					State = NpcState.VillageComplete;
					Log.Info( "Lute: VillageBuilderController — village complete!" );
					return;
				}
				Controller.WishVelocity = Vector3.Zero;
				return;
			}

			// Update target if the task changed
			if ( Builder.CurrentTask.Position != _currentTarget )
			{
				_currentTarget = Builder.CurrentTask.Position;
				Log.Info( $"Lute: VillageBuilderController walking to '{Builder.CurrentTask.Name}' at {_currentTarget}." );
			}

			float dist = Vector3.DistanceBetween( WorldPosition, _currentTarget );

			if ( dist <= StopRadius )
			{
				// Arrived at site. If using the director and the builder already
				// holds a directed task reservation, skip the site claim - the
				// reservation is already held by the builder through the
				// ConstructionDirector. Just authorize execution.
				if ( UseDirector && !string.IsNullOrEmpty( Builder.CurrentDirectedTaskId ) )
				{
					var taskId = Builder.CurrentDirectedTaskId;
					if ( !ConstructionDirector.AuthorizeExecution( taskId, _npcId ) )
					{
						Log.Warning( $"Lute: VillageBuilderController '{_npcId}' could not authorize {taskId}." );
						Controller.WishVelocity = Vector3.Zero;
						return;
					}

					_activeDirectedTaskId = taskId;
					State = NpcState.Building;
					_stateTimer = 0;
					_buildingTaskIndex = Builder.CurrentTaskIndex;
					Controller.WishVelocity = Vector3.Zero;
					Log.Info( $"Lute: VillageBuilderController arrived at '{Builder.CurrentTask.Name}'. Building." );
					return;
				}

				// Non-director path: claim it before building so other NPCs
				// don't stack on the same spot.
				if ( UseBlackboard && !TryClaimSite( _currentTarget ) )
				{
					// Site is contested. Wait here and retry next tick; the
					// other NPC will release when its task completes.
					Controller.WishVelocity = Vector3.Zero;
					return;
				}

				State = NpcState.Building;
				_stateTimer = 0;
				_buildingTaskIndex = Builder.CurrentTaskIndex;
				Controller.WishVelocity = Vector3.Zero;
				Log.Info( $"Lute: VillageBuilderController arrived at '{Builder.CurrentTask.Name}'. Building." );
				return;
			}

			// Move toward target — teleport when far away (production: no walking)
			MoveToSite( _currentTarget );
		}

		/// <summary>
		/// Teleport the NPC to the build site, spawning warp effects at both
		/// the departure and arrival points. Each builder gets a distinct
		/// tint color so the effects are visually distinguishable.
		/// </summary>
		void MoveToSite( Vector3 target )
		{
			float dist = Vector3.DistanceBetween( WorldPosition, target );

			// Teleport threshold: if more than ~5m away, warp directly.
			const float TeleportThreshold = 5f * 39.37f; // 5m in inches

			if ( dist > TeleportThreshold )
			{
				// Spawn departure poof at current position
				var tint = WarpEffect.GetBuilderTint( BuilderId );
				WarpEffect.Spawn( Scene, WorldPosition, tint, 0.4f );

				// Teleport to the target site (slightly above ground)
				WorldPosition = target + Vector3.Up * 4f;
				Controller.WishVelocity = Vector3.Zero;

				// Spawn arrival poof at the destination
				WarpEffect.Spawn( Scene, target, tint, 0.6f );

				Log.Info( $"Lute: VillageBuilderController '{_npcId}' warped to '{Builder.CurrentTask?.Name}' at {target} (dist {dist / 39.37f:F1}m)." );
				return;
			}

			// Close enough — walk normally for the last few meters
			MoveToward( target );
		}

		void HandleBuilding()
		{
			Controller.WishVelocity = Vector3.Zero;

			// Director mode: the TaskCompleted event drives the transition
			// out of Building. Do NOT infer completion from CurrentTaskIndex
			// or CurrentTask.Status — the executor (VillageBuilder) is the
			// sole authority for task completion. The controller just waits.
			if ( UseDirector )
			{
				// Safety: if the directed task id was cleared (e.g. village
				// complete or task failed), transition to idle.
				if ( string.IsNullOrEmpty( _activeDirectedTaskId ) )
				{
					if ( Builder.IsComplete )
					{
						State = NpcState.VillageComplete;
						ReleaseActiveClaim();
						Log.Info( "Lute: VillageBuilderController — village complete!" );
						return;
					}

					State = NpcState.Idle;
					_stateTimer = 0;
				}
				return;
			}

			// Legacy/non-director path: infer completion from task state.
			if ( Builder.CurrentTask is null )
			{
				if ( Builder.IsComplete )
				{
					State = NpcState.VillageComplete;
					ReleaseActiveClaim();
					Log.Info( "Lute: VillageBuilderController — village complete!" );
					return;
				}

				Controller.WishVelocity = Vector3.Zero;
				return;
			}

			if ( _buildingTaskIndex >= 0 && Builder.CurrentTaskIndex != _buildingTaskIndex )
			{
				ReportTaskComplete();
				ReleaseActiveClaim();
				State = NpcState.WalkingToNextSite;
				_stateTimer = 0;
				Controller.WishVelocity = Vector3.Zero;
				Log.Info( $"Lute: VillageBuilderController task #{_buildingTaskIndex} done (builder moved to #{Builder.CurrentTaskIndex}). Walking to next site." );
				return;
			}

			if ( Builder.CurrentTask.Status == 2 )
			{
				ReportTaskComplete();
				ReleaseActiveClaim();
				State = NpcState.WalkingToNextSite;
				_stateTimer = 0;
				Controller.WishVelocity = Vector3.Zero;
				Log.Info( $"Lute: VillageBuilderController task '{Builder.CurrentTask.Name}' done. Walking to next site." );
				return;
			}
		}

		/// <summary>
		/// Claim the build site at <paramref name="site"/> if it is clear
		/// of other NPCs' claims. Stores the claim ID in
		/// <see cref="_activeClaimId"/> on success. Returns false (and
		/// logs) if another NPC already holds an overlapping claim.
		///
		/// When UseDirector is true, the claim goes through the
		/// ConstructionDirector's authoritative transaction system
		/// (BlackboardRequest → BlackboardResult) instead of directly
		/// calling SpatialBlackboard.
		/// </summary>
		bool TryClaimSite( Vector3 site )
		{
			// Director path: if the VillageBuilder already claimed this task
			// through the ConstructionDirector (CurrentDirectedTaskId is set),
			// the reservation is already held - just validate ownership and
			// skip the redundant claim transaction.
			if ( UseDirector && _registeredWithDirector && Builder?.CurrentTask is not null )
			{
				if ( !string.IsNullOrEmpty( Builder.CurrentDirectedTaskId ) )
				{
					// Task already claimed by the executor - validate our ownership.
					var existing = ConstructionDirector.GetTask( Builder.CurrentDirectedTaskId );
				if ( existing != null && existing.AssignedBuilder == Builder.BuilderId )
					{
						_activeClaimId = existing.ReservationId ?? Builder.CurrentDirectedTaskId;
						return true;
					}
				}

				// No existing director claim - submit a Claim transaction.
				var taskName = Builder.CurrentTask.Name;
				var req = new BlackboardRequest
				{
					Actor = _npcId,
					Operation = BlackboardOperation.Claim,
					Key = taskName,
					Tick = (long)SpatialBlackboard.CurrentTime,
				};
				var result = ConstructionDirector.ProcessRequest( req );
				if ( result.Success )
				{
					_activeClaimId = result.Value?.ToString() ?? taskName;
					SpatialBlackboard.PostMessage( _npcId, "", "request", $"claiming site at {site}", site );
					return true;
				}
				Log.Info( $"Lute: VillageBuilderController '{_npcId}' director claim rejected at {site}: {result.Reason}" );
				return false;
			}

			// Fallback: direct SpatialBlackboard claim.
			// Skip this when using the director — direct claims have no
			// TaskId and will block the director's task reservations for
			// other builders, causing reservation conflicts.
			if ( UseDirector )
			{
				Log.Info( $"Lute: VillageBuilderController '{_npcId}' no director claim available — waiting." );
				return false;
			}

			var blocker = SpatialBlackboard.CheckClear( site, ClaimRadius, _npcId );
			if ( blocker is not null )
			{
				Log.Info( $"Lute: VillageBuilderController '{_npcId}' site at {site} blocked by '{blocker}' — waiting." );
				return false;
			}

			if ( SpatialBlackboard.Claim( _npcId, site, ClaimRadius, "building", 0 ) )
			{
				_activeClaimId = SpatialBlackboard.GetClaimsByOwner( _npcId )
					.OrderByDescending( c => c.Timestamp )
					.First().Id;
				SpatialBlackboard.PostMessage( _npcId, "", "request", $"claiming site at {site}", site );
				return true;
			}

			Log.Info( $"Lute: VillageBuilderController '{_npcId}' claim rejected at {site}." );
			return false;
		}

		/// <summary>
		/// Release the current build-site claim (if any) and announce it
		/// on the blackboard so waiting NPCs can proceed.
		/// </summary>
		void ReleaseActiveClaim()
		{
			if ( !UseBlackboard || string.IsNullOrEmpty( _activeClaimId ) )
				return;

			SpatialBlackboard.ReleaseClaim( _activeClaimId );
			SpatialBlackboard.PostMessage( _npcId, "", "done", "site released", WorldPosition );
			_activeClaimId = null;
		}

		/// <summary>
		/// Report task completion to the ConstructionDirector and fire
		/// a TaskCompleted event. Called when the builder finishes a task.
		/// Also updates self-beliefs (energy, morale, task history).
		/// </summary>
		void ReportTaskComplete()
		{
			if ( Builder?.CurrentTask is not null )
			{
				var taskName = Builder.CurrentTask.Name;

				// Record in task history
				if ( _beliefs != null )
				{
					_beliefs.RecordTaskHistory( taskName, taskName, completed: true );
					_beliefs.Self.TasksCompleted++;
					_beliefs.Self.DrainEnergy( 10f ); // building is tiring
					_beliefs.Self.AdjustMorale( +5f ); // completion feels good
				}

				// Report to director
				if ( UseDirector && _registeredWithDirector )
				{
					var req = new BlackboardRequest
					{
						Actor = _npcId,
						Operation = BlackboardOperation.Complete,
						Key = taskName,
						Tick = (long)SpatialBlackboard.CurrentTime,
					};
					ConstructionDirector.ProcessRequest( req );
				}
			}
		}

		void MoveToward( Vector3 target )
		{
			// Try NavMesh first
			if ( UseNavMesh && NavAgent is not null && Scene.NavMesh is not null && Scene.NavMesh.IsEnabled )
			{
				NavAgent.MoveTo( target );
				if ( NavAgent.WishVelocity.Length > 1f )
				{
					Controller.WishVelocity = NavAgent.WishVelocity.Normal * WalkSpeed;
					return;
				}
			}

			// Fallback: direct steering
			var toTarget = (target - WorldPosition).WithZ( 0 );
			float dist = toTarget.Length;

			if ( dist > 1f )
			{
				Controller.WishVelocity = toTarget.Normal * WalkSpeed;
			}
			else
			{
				Controller.WishVelocity = Vector3.Zero;
			}
		}

		void LogVillageStatus()
		{
			if ( Builder is null ) return;

			int done = Builder.Tasks.Count( t => t.Status == 2 );
			int inProg = Builder.Tasks.Count( t => t.Status == 1 );
			int pending = Builder.Tasks.Count( t => t.Status == 0 );
			string current = Builder.CurrentTask?.Name ?? "none";

			Log.Info( $"Lute: Village status — {done}/{Builder.Tasks.Count} complete, {inProg} in progress, {pending} pending. Current: {current}. Elapsed: {Builder.ElapsedTime/60:F1} min." );
		}
	}
}
