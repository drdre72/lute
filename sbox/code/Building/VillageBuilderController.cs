using System.Linq;
using Sandbox;

namespace Lute.Building
{
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

		protected override void OnStart()
		{
			Log.Info( $"Lute: VillageBuilderController '{GameObject.Name}' started." );

			if ( NavAgent is not null )
			{
				NavAgent.UpdatePosition = false;
				NavAgent.UpdateRotation = false;
			}
		}

		protected override void OnFixedUpdate()
		{
			if ( Builder is null )
				return;

			_stateTimer += Time.Delta;
			_logTimer += Time.Delta;

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
					break;
			}

			// Periodic status log (every 60s)
			if ( _logTimer >= 60f )
			{
				_logTimer = 0;
				LogVillageStatus();
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
			if ( Builder.CurrentTask is null )
			{
				// No current task — check if village is complete
				if ( Builder.IsComplete )
				{
					State = NpcState.VillageComplete;
					Log.Info( "Lute: VillageBuilderController — village complete!" );
					return;
				}
				// Wait for the builder to assign a task
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
				// Arrived at site — start building
				State = NpcState.Building;
				_stateTimer = 0;
				Controller.WishVelocity = Vector3.Zero;
				Log.Info( $"Lute: VillageBuilderController arrived at '{Builder.CurrentTask.Name}'. Building." );
				return;
			}

			// Move toward target
			MoveToward( _currentTarget );
		}

		void HandleBuilding()
		{
			if ( Builder.CurrentTask is null )
			{
				// Task completed — check for next or done
				if ( Builder.IsComplete )
				{
					State = NpcState.VillageComplete;
					Log.Info( "Lute: VillageBuilderController — village complete!" );
					return;
				}

				// Wait for next task to be assigned
				Controller.WishVelocity = Vector3.Zero;
				return;
			}

			// Check if the current task is complete (Status == 2)
			if ( Builder.CurrentTask.Status == 2 )
			{
				// Task done — walk to next site
				State = NpcState.WalkingToNextSite;
				_stateTimer = 0;
				Controller.WishVelocity = Vector3.Zero;
				Log.Info( $"Lute: VillageBuilderController task '{Builder.CurrentTask.Name}' done. Moving to next site." );
				return;
			}

			// Stand by while building — small idle movement
			Controller.WishVelocity = Vector3.Zero;
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
