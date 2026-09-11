using System.Linq;

namespace Lute.Building
{
	/// <summary>
	/// Body + AI controller for an <see cref="NPCBuilder"/>. This is NOT
	/// Merlyn (<see cref="LuteBuilderNpc"/>) and is NOT LLM-driven — it's a
	/// traditional finite-state machine that dresses a citizen body as a
	/// builder, walks to the structure's build-site center, stands by while
	/// <see cref="NPCBuilder"/> places pieces, then walks onto the finished
	/// floor to validate collision the same way Merlyn does.
	///
	/// Movement uses the proven <see cref="PlayerController"/> + Rigidbody
	/// velocity pattern from <see cref="LuteBuilderNpc.AgentTick"/>. When a
	/// sibling <see cref="NavMeshAgent"/> is present and the scene's NavMesh
	/// is enabled/loaded, the agent pathfinds to the target and its
	/// <see cref="NavMeshAgent.WishVelocity"/> is fed into
	/// <see cref="PlayerController.WishVelocity"/> (so PlayerController still
	/// owns the physics step and GroundObject). Otherwise it falls back to
	/// direct steering toward the world target.
	/// </summary>
	public sealed class NPCBuilderController : Component
	{
		[RequireComponent] public PlayerController Controller { get; set; }
		[RequireComponent] public NavMeshAgent NavAgent { get; set; }

		/// <summary> The sibling builder whose site we walk to and inspect. </summary>
		[Property] public NPCBuilder Builder { get; set; }

		/// <summary> Move speed in units/sec (~5 m/s). Used as NavMeshAgent.MaxSpeed fallback. </summary>
		[Property] public float WalkSpeed { get; set; } = 5f * 39.37f;

		/// <summary> Stop distance from a walk target (units). </summary>
		[Property] public float StopRadius { get; set; } = 80f;

		/// <summary>
		/// When true, use the sibling <see cref="NavMeshAgent"/> for pathfinding
		/// (feeds NavAgent.WishVelocity into PlayerController.WishVelocity). When
		/// the NavMesh isn't enabled/loaded or the agent isn't ready, falls back
		/// to direct steering toward the target. Toggled automatically each tick.
		/// </summary>
		[Property] public bool UseNavMesh { get; set; } = true;

		/// <summary> NPC state for the build-and-inspect sequence. </summary>
		public enum NpcState
		{
			Idle,
			WalkingToSite,
			Building,
			WalkingOntoFloor,
			Inspecting,
			Done
		}

		[Property] public NpcState State { get; set; } = NpcState.Idle;

		private float _stateTimer;
		private float _logTimer;
		private Vector3 _inspectStartPos;

		protected override void OnStart()
		{
			Log.Info( $"Lute: NPCBuilderController '{GameObject.Name}' started at {WorldPosition}." );

			// Resolve the sibling builder if not assigned in the inspector.
			Builder ??= Components.Get<NPCBuilder>();
			if ( Builder is null )
			{
				Log.Warning( $"Lute: NPCBuilderController '{GameObject.Name}' has no NPCBuilder sibling — staying idle." );
				return;
			}

			DressAsBuilder();
		}

		protected override void OnUpdate()
		{
			if ( Builder is null )
				return;

			_logTimer += Time.Delta;

			switch ( State )
			{
				case NpcState.Idle:
				{
					_stateTimer += Time.Delta;
					if ( _stateTimer > 1f )
					{
						State = NpcState.WalkingToSite;
						_stateTimer = 0f;
						Log.Info( $"Lute: NPCBuilderController '{GameObject.Name}' → WalkingToSite (target={Builder.BuildSiteCenter})." );
					}
					break;
				}

				case NpcState.WalkingToSite:
				{
					SteerToward( Builder.BuildSiteCenter );

					var dist = Vector3.DistanceBetween( WorldPosition.WithZ( 0 ), Builder.BuildSiteCenter.WithZ( 0 ) );
					if ( dist < StopRadius )
					{
						Stop();
						State = NpcState.Building;
						_stateTimer = 0f;
						Log.Info( $"Lute: NPCBuilderController '{GameObject.Name}' reached build site (dist={dist:F1}) → Building." );
					}
					break;
				}

				case NpcState.Building:
				{
					// Stand still and watch the build. Face the site center.
					Face( Builder.BuildSiteCenter );
					Stop();

					if ( Builder.IsComplete )
					{
						// Walk toward the site center again — now the floor exists.
						State = NpcState.WalkingOntoFloor;
						_stateTimer = 0f;
						Log.Info( $"Lute: NPCBuilderController '{GameObject.Name}' build complete → WalkingOntoFloor." );
					}
					break;
				}

				case NpcState.WalkingOntoFloor:
				{
					SteerToward( Builder.BuildSiteCenter );

					var dist = Vector3.DistanceBetween( WorldPosition.WithZ( 0 ), Builder.BuildSiteCenter.WithZ( 0 ) );
					if ( dist < StopRadius * 0.5f )
					{
						Stop();
						_inspectStartPos = WorldPosition;
						State = NpcState.Inspecting;
						_stateTimer = 0f;
						Log.Info( $"Lute: NPCBuilderController '{GameObject.Name}' on floor (dist={dist:F1}) → Inspecting." );
					}
					break;
				}

				case NpcState.Inspecting:
				{
					Face( Builder.BuildSiteCenter );
					Stop();

					_stateTimer += Time.Delta;
					var grounded = Controller.GroundObject.IsValid();
					var fallDelta = _inspectStartPos.z - WorldPosition.z;

					if ( _stateTimer > 2f )
					{
						var success = grounded && fallDelta < 50f;
						Log.Info( $"Lute: NPCBuilderController '{GameObject.Name}' inspect {(success ? "PASSED" : "FAILED")} — grounded={grounded}, fell={fallDelta / 39.37f:F2} m" );
						State = NpcState.Done;
					}
					break;
				}

				case NpcState.Done:
				{
					Stop();
					break;
				}
			}
		}

		/// <summary>
		/// Steer toward a world position. When a sibling <see cref="NavMeshAgent"/>
		/// is present and the scene NavMesh is enabled/loaded, uses pathfinding:
		/// calls <see cref="NavMeshAgent.MoveTo"/> and feeds the agent's
		/// <see cref="NavMeshAgent.WishVelocity"/> into
		/// <see cref="PlayerController.WishVelocity"/> (so PlayerController still
		/// owns the physics step and GroundObject). Otherwise falls back to direct
		/// steering toward the target. Faces the heading in both cases.
		/// </summary>
		void SteerToward( Vector3 target )
		{
			var toTarget = (target - WorldPosition).WithZ( 0 );
			var dist = toTarget.Length;
			if ( dist < 1f )
				return;

			Controller.EyeAngles = new Angles( 0, toTarget.EulerAngles.yaw, 0 );

			// Use WishVelocity (not Rigidbody.Velocity) so PlayerController
			// owns the movement, runs its physics step, and populates
			// GroundObject. Direct Rigidbody.Velocity overrides cancel
			// vertical physics and keep the body floating above the floor.
			if ( UseNavMesh && TrySteerWithNavMesh( target ) )
			{
				// NavMesh drove WishVelocity this tick.
			}
			else
			{
				Controller.WishVelocity = toTarget.Normal * WalkSpeed;
			}

			if ( _logTimer > 1f )
			{
				_logTimer = 0f;
				Log.Info( $"Lute: NPCBuilderController '{GameObject.Name}' walking — pos={WorldPosition}, dist={dist:F1} ({dist / 39.37f:F2} m), grounded={Controller.GroundObject.IsValid()}, navmesh={_usingNavMesh}." );
			}
		}

		/// <summary>
		/// Try to steer using the sibling NavMeshAgent. Returns true if the
		/// agent is active and drove <see cref="PlayerController.WishVelocity"/>
		/// this tick; false if the caller should fall back to direct steering.
		/// </summary>
		bool _usingNavMesh;
		bool _navMeshReadyLogged;
		bool _navMeshDiagLogged;
		bool TrySteerWithNavMesh( Vector3 target )
		{
			_usingNavMesh = false;

			var nav = NavAgent;
			if ( nav is null || !nav.Enabled )
				return false;

			// Scene.NavMesh must be enabled. If the scene has NavMesh
			// disabled, the agent's internal handle is null and MoveTo is
			// a no-op — we fall back to direct steering.
			var sceneNav = Scene?.NavMesh;
			if ( sceneNav is null || !sceneNav.IsEnabled )
				return false;

			// One-time diagnostic: log whether the navmesh has generated
			// around the agent and the target. GetClosestPoint returns null
			// if the query is uninitialized or no poly is within radius.
			if ( !_navMeshDiagLogged )
			{
				_navMeshDiagLogged = true;
				var agentHit = sceneNav.GetClosestPoint( WorldPosition, 512f );
				var targetHit = sceneNav.GetClosestPoint( target, 512f );
				Log.Info( $"Lute: NPCBuilderController '{GameObject.Name}' NavMesh diag — enabled={sceneNav.IsEnabled}, agentPos={WorldPosition} → closest={agentHit}, targetPos={target} → closest={targetHit}." );
			}

			nav.SetAgentPosition( WorldPosition );
			nav.MoveTo( target );

			// Feed the agent's desired velocity into PlayerController. If the
			// agent hasn't planned a path yet (or the target is unreachable),
			// WishVelocity is zero — fall back to direct steering so the NPC
			// still makes progress.
			var wish = nav.WishVelocity;
			if ( wish.LengthSquared < 1f )
				return false;

			// Clamp to WalkSpeed so the agent's crowd-derived velocity (which
			// can momentarily exceed MaxSpeed during avoidance) doesn't outrun
			// the PlayerController's grounded movement model.
			if ( wish.Length > WalkSpeed )
				wish = wish.Normal * WalkSpeed;

			Controller.WishVelocity = wish;
			_usingNavMesh = true;

			if ( !_navMeshReadyLogged )
			{
				_navMeshReadyLogged = true;
				Log.Info( $"Lute: NPCBuilderController '{GameObject.Name}' NavMesh steering active (agent pos={nav.AgentPosition}, target={target})." );
			}

			return true;
		}

		void Face( Vector3 target )
		{
			var toTarget = (target - WorldPosition).WithZ( 0 );
			if ( toTarget.Length > 1f )
				Controller.EyeAngles = new Angles( 0, toTarget.EulerAngles.yaw, 0 );
		}

		void Stop()
		{
			// Zero WishVelocity lets PlayerController apply its brakes and
			// settle on the ground, keeping GroundObject valid.
			Controller.WishVelocity = Vector3.Zero;

			// Also stop the NavMesh agent so it doesn't keep requesting paths.
			NavAgent?.Stop();
		}

		/// <summary>
		/// Dress the NPC as a builder: flannel shirt, cargo pants, army boots,
		/// short scruffy brown hair. Same outfit as Merlyn so the two are
		/// visually consistent, but this is a separate body.
		/// </summary>
		void DressAsBuilder()
		{
			var dresser = Components.GetOrCreate<Dresser>();
			dresser.Source = Dresser.ClothingSource.Manual;
			dresser.ManualHeight = 0.5f;
			dresser.ManualTint = 0.3f;
			dresser.ManualAge = 0.6f;
			dresser.ApplyHeightScale = true;

			var body = GetComponentInChildren<SkinnedModelRenderer>();
			if ( body.IsValid() )
				dresser.BodyTarget = body;

			var clothingPaths = new[]
			{
				"models/citizen_clothes/hair/hair_shortscruffy/hair_shortscruffy_brown.clothing",
				"models/citizen_clothes/shirt/Flannel_Shirt/Flannel_Shirt.clothing",
				"models/citizen_clothes/trousers/CargoPants/cargo_pants_army.clothing",
				"models/citizen_clothes/shoes/Boots/army_boots.clothing",
			};

			dresser.Clothing.Clear();
			foreach ( var path in clothingPaths )
			{
				var clothing = ResourceLibrary.Get<Clothing>( path );
				if ( clothing is not null )
					dresser.Clothing.Add( new ClothingContainer.ClothingEntry( clothing ) );
				else
					Log.Warning( $"Lute: NPCBuilderController clothing not found: '{path}'." );
			}

			_ = dresser.Apply();
			Log.Info( $"Lute: NPCBuilderController '{GameObject.Name}' dressed with {dresser.Clothing.Count} items." );
		}
	}
}
