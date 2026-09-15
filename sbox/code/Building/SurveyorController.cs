using System;
using System.Collections.Generic;
using System.Linq;
using Sandbox;
using Lute.Items;

namespace Lute.Building
{
	/// <summary>
	/// Surveyor NPC controller. The Surveyor is the first profession
	/// that changes the town — it receives StructureRequests (not
	/// build coordinates) and autonomously selects valid build sites
	/// by querying the SpatialRegistry, terrain, roads, and nearby
	/// structures.
	///
	/// Per the adaptive settlement planning proposal:
	/// <code>
	/// StructureRequest (type + requirements, no position)
	///   ↓
	/// Surveyor: query SpatialRegistry, terrain, roads → candidate parcels
	///   ↓
	/// Score candidates (road access, flat terrain, clearance, etc.)
	///   ↓
	/// Select best candidate
	///   ↓
	/// Resolve request (set position + rotation)
	///   ↓
	/// Create VillageBuildTask + register with ConstructionDirector
	/// </code>
	///
	/// All decisions are deterministic: given the same world state
	/// and request, the surveyor selects the same site.
	/// </summary>
	public sealed class SurveyorController : Component
	{
		[RequireComponent] public PlayerController Controller { get; set; }
		[RequireComponent] public NavMeshAgent NavAgent { get; set; }

		[Property] public string NpcName { get; set; } = "Surveyor";

		[Property] public float WalkSpeed { get; set; } = 5f * 39.37f;

		[Property] public bool UseNavMesh { get; set; } = true;

		/// <summary>
		/// How far from the village center to search for candidate sites
		/// (in inches). The search ring expands from inner to outer.
		/// </summary>
		[Property] public float SearchInnerRadius { get; set; } = 20f * 39.37f; // 20m
		[Property] public float SearchOuterRadius { get; set; } = 180f * 39.37f; // 180m

		/// <summary>
		/// Grid step for candidate generation (inches). Smaller = more
		/// candidates but slower search.
		/// </summary>
		[Property] float CandidateStep { get; set; } = 10f * 39.37f; // 10m grid

		/// <summary> Surveyor state machine. </summary>
		public enum SurveyorState
		{
			Idle,
			Surveying,
			WalkingToSite,
			InspectingSite,
			Resolving,
		}

		[Property] public SurveyorState State { get; set; } = SurveyorState.Idle;

		int _builderId = -1;
		StructureRequest _currentRequest;
		StructureDefinition _structureDef;
		float _stateTimer;
		float _logTimer;
		bool _usingNavMesh;
		bool _navMeshDiagLogged;

		// Current candidate evaluation
		List<CandidateSite> _candidates;
		int _candidateIndex;

		struct CandidateSite
		{
			public Vector3 Position;
			public float Rotation;
			public float Score;
			public string Reason;
		}

		protected override void OnStart()
		{
			Log.Info( $"Lute: SurveyorController '{NpcName}' started at {WorldPosition}." );

			_builderId = -1 - Math.Abs( GameObject.Id.GetHashCode() );
			ConstructionDirector.RegisterBuilder( _builderId, NpcName, professionId: "surveyor" );
			Log.Info( $"Lute: SurveyorController '{NpcName}' registered as surveyor (builderId={_builderId})." );
		}

		protected override void OnUpdate()
		{
			_logTimer += Time.Delta;

			switch ( State )
			{
				case SurveyorState.Idle:
				{
					_stateTimer += Time.Delta;
					if ( _stateTimer > 1f )
					{
						_stateTimer = 0f;
						PickNextRequest();
					}
					break;
				}

				case SurveyorState.Surveying:
				{
					DoSurvey();
					break;
				}

				case SurveyorState.WalkingToSite:
				{
					if ( _currentRequest == null )
					{
						ResetToIdle();
						break;
					}
					var target = _currentRequest.ResolvedPosition.Value;
					SteerToward( target );
					if ( WithinStopRadius( target ) )
					{
						Stop();
						State = SurveyorState.InspectingSite;
						_stateTimer = 0f;
						Log.Info( $"Lute: Surveyor '{NpcName}' arrived at candidate site {target} → inspecting." );
					}
					break;
				}

				case SurveyorState.InspectingSite:
				{
					// Brief inspection pause, then resolve.
					_stateTimer += Time.Delta;
					if ( _stateTimer > 1f )
					{
						State = SurveyorState.Resolving;
						_stateTimer = 0f;
					}
					break;
				}

				case SurveyorState.Resolving:
				{
					ResolveAndDispatch();
					break;
				}
			}
		}

		// ── State transitions ──

		void PickNextRequest()
		{
			var req = SettlementNeedBoard.PendingRequests
				.OrderBy( r => -r.Priority )
				.FirstOrDefault();

			if ( req == null )
				return;

			_currentRequest = req;
			req.Status = StructureRequestStatus.Surveying;
			_candidates = null;
			_candidateIndex = 0;

			Log.Info( $"Lute: Surveyor '{NpcName}' picked up request {req.Id} ({req.StructureType}) priority={req.Priority:F2}." );
			State = SurveyorState.Surveying;
			_stateTimer = 0f;
		}

		void DoSurvey()
		{
			if ( _currentRequest == null )
			{
				ResetToIdle();
				return;
			}

			// Compile the authoritative StructureDefinition BEFORE site
			// selection. The Surveyor validates against the exact footprint,
			// bounds, and piece count from the compiled Blueprint — not the
			// approximate MinWidth/MinDepth from SiteRequirements. Per the
			// professor's review: "One definition. Surveyor validates
			// BuildPlan.Bounds, ReservationManager reserves BuildPlan.Bounds,
			// ConstructionDirector uses BuildPlan.Materials, StructureExecutor
			// realizes BuildPlan geometry."
			if ( _structureDef == null )
			{
				int baseW = EstimateBaseWidth( _currentRequest.StructureType );
				int baseH = EstimateBaseHeight( _currentRequest.StructureType );
				int seed = (int)( _currentRequest.Id.GetHashCode() ) % 100000;
				if ( seed < 0 ) seed = -seed;
				_structureDef = StructureDefinition.Compile(
					_currentRequest.StructureType, baseW, baseH,
					wealthFactor: 1.5f, layoutSeed: seed );
				_currentRequest.StructureDefinitionId = _structureDef.Id;
				Log.Info( $"Lute: Surveyor '{NpcName}' compiled StructureDefinition {_structureDef.Id}" +
					$" for {_currentRequest.StructureType}: { _structureDef.TotalPieces} pieces," +
					$" bounds={_structureDef.LocalBounds.Mins}..{_structureDef.LocalBounds.Maxs}." );
			}

			// Generate candidates if not done yet.
			if ( _candidates == null )
			{
				_candidates = GenerateCandidates( _currentRequest );
				Log.Info( $"Lute: Surveyor '{NpcName}' generated {_candidates.Count} candidate sites for {_currentRequest.StructureType}." );

				if ( _candidates.Count == 0 )
				{
					Log.Warning( $"Lute: Surveyor '{NpcName}' no valid candidate sites for {_currentRequest.StructureType}." );
					SettlementNeedBoard.RejectRequest( _currentRequest.Id, "no valid candidate sites" );
					_currentRequest = null;
					_structureDef = null;
					ResetToIdle();
					return;
				}
			}

			// Walk to the best candidate (first in sorted list).
			var best = _candidates[0];
			_currentRequest.ResolvedPosition = best.Position;
			_currentRequest.ResolvedRotation = best.Rotation;

			Log.Info( $"Lute: Surveyor '{NpcName}' selected site {best.Position} (score={best.Score:F2}) for {_currentRequest.StructureType}. {best.Reason}" );
			State = SurveyorState.WalkingToSite;
			_stateTimer = 0f;
		}

		void ResolveAndDispatch()
		{
			if ( _currentRequest == null || !_currentRequest.IsResolved )
			{
				ResetToIdle();
				return;
			}

			var pos = _currentRequest.ResolvedPosition.Value;
			var rot = _currentRequest.ResolvedRotation ?? 0f;

			// Mark the request as resolved.
			SettlementNeedBoard.ResolveRequest( _currentRequest.Id, pos, rot );

			// Create a VillageBuildTask for this structure, linked to the
			// precompiled StructureDefinition so the executor builds from
			// the exact same Blueprint the Surveyor validated against.
			var def = _structureDef;
			int baseW = EstimateBaseWidth( _currentRequest.StructureType );
			int baseH = EstimateBaseHeight( _currentRequest.StructureType );
			int seed = def?.Blueprint?.Provenance?.Seed ?? 0;

			// Deterministic name: structureType + definition id suffix.
			// No more Guid.NewGuid() — the professor flagged this as
			// non-deterministic.
			string defSuffix = def != null ? def.Id.Substring( Math.Max( 0, def.Id.Length - 6 ) ) : "000000";
			var buildTask = new VillageBuildTask
			{
				Name = $"{_currentRequest.StructureType}_{defSuffix}",
				Position = pos,
				Rotation = rot,
				TaskType = _currentRequest.StructureType,
				Priority = 10 + (int)( _currentRequest.Priority * 100 ),
				BaseWidth = baseW,
				BaseHeight = baseH,
				WealthFactor = 1.5f,
				LayoutSeed = seed,
				Style = ArchitecturalStyle.Vernacular,
				StructureDefinitionId = def?.Id,
				TotalPieces = def?.TotalPieces ?? 0,
			};

			// Register with ConstructionDirector.
			var taskId = ConstructionDirector.RegisterTask( buildTask );
			SettlementNeedBoard.DispatchRequest( _currentRequest.Id, taskId );

			Log.Info( $"Lute: Surveyor '{NpcName}' dispatched '{buildTask.Name}' ({_currentRequest.StructureType})" +
				$" at {pos} as task {taskId}." );

			_currentRequest = null;
			_structureDef = null;
			ResetToIdle();
		}

		void ResetToIdle()
		{
			State = SurveyorState.Idle;
			_stateTimer = 0f;
		}

		// ── Candidate generation + scoring ──

		List<CandidateSite> GenerateCandidates( StructureRequest req )
		{
			var reqs = req.Requirements;
			var center = reqs.NearCenter ?? VillageCenter();
			if ( center == null ) return new();

			var candidates = new List<CandidateSite>();
			float minR = SearchInnerRadius;
			float maxR = reqs.MaxCenterDistance > 0
				? MathF.Min( reqs.MaxCenterDistance, SearchOuterRadius )
				: SearchOuterRadius;

			// Grid search in rings around the center.
			for ( float r = minR; r <= maxR; r += CandidateStep )
			{
				int circumference = (int)MathF.Max( 8, ( 2f * MathF.PI * r / CandidateStep ) );
				for ( int i = 0; i < circumference; i++ )
				{
					float angle = ( i * 2f * MathF.PI ) / circumference;
					var pos = center.Value + new Vector3(
						r * MathF.Cos( angle ),
						r * MathF.Sin( angle ),
						0f );

					var score = ScoreSite( pos, reqs );
					if ( score.score > 0f )
					{
						candidates.Add( new CandidateSite
						{
							Position = pos,
							Rotation = 0f,
							Score = score.score,
							Reason = score.reason,
						} );
					}
				}
			}

			// Sort by score (highest first).
			candidates.Sort( ( a, b ) => b.Score.CompareTo( a.Score ) );
			return candidates;
		}

		(float score, string reason) ScoreSite( Vector3 pos, SiteRequirements reqs )
		{
			float score = 1.0f;
			var reasons = new List<string>();

			// 0. Reject sites already dispatched/reserved by a prior survey
			//    cycle (before the builder has registered it in SpatialRegistry).
			if ( SettlementNeedBoard.IsSiteTaken( pos ) )
			{
				return ( 0f, "site already dispatched this session" );
			}

			// 1. Is the site free? Use the EXACT bounds from the compiled
			//    StructureDefinition, not the approximate MinWidth/MinDepth
			//    from SiteRequirements. This is the professor's key fix:
			//    the Surveyor validates the same footprint the executor builds.
			BBox footprint;
			if ( _structureDef != null )
			{
				footprint = _structureDef.WorldBoundsAt( pos );
			}
			else
			{
				// Fallback for tasks without a precompiled definition.
				footprint = new BBox(
					pos - new Vector3( reqs.MinWidth / 2f, reqs.MinDepth / 2f, 0f ),
					pos + new Vector3( reqs.MinWidth / 2f, reqs.MinDepth / 2f, 100f ) );
			}

			if ( !SpatialRegistry.IsVolumeFree( footprint ).IsFree )
			{
				return ( 0f, "overlaps existing structure" );
			}

			// 3. Near road?
			if ( reqs.NearRoad )
			{
				float roadDist = DistanceToNearestRoad( pos );
				if ( reqs.MaxRoadDistance > 0 && roadDist > reqs.MaxRoadDistance )
				{
					return ( 0f, $"too far from road ({roadDist / 39.37f:F1}m)" );
				}
				if ( roadDist < float.MaxValue )
				{
					// Closer to road = better.
					float roadScore = 1f - ( roadDist / ( 30f * 39.37f ) );
					score += MathF.Max( 0f, roadScore ) * 0.3f;
					reasons.Add( $"road={roadDist / 39.37f:F1}m" );
				}
				else
				{
					// No road found at all — penalize but don't reject.
					score -= 0.3f;
					reasons.Add( "no road nearby" );
				}
			}

			// 4. Terrain slope (raycast-based). Use exact bounds from
			//    the compiled definition if available.
			if ( reqs.MaxSlope > 0 )
			{
				float slopeW = _structureDef != null
					? ( _structureDef.LocalBounds.Maxs.x - _structureDef.LocalBounds.Mins.x )
					: reqs.MinWidth;
				float slopeD = _structureDef != null
					? ( _structureDef.LocalBounds.Maxs.y - _structureDef.LocalBounds.Mins.y )
					: reqs.MinDepth;
				float slope = EstimateSlope( pos, slopeW, slopeD );
				if ( slope > reqs.MaxSlope )
				{
					return ( 0f, $"slope too steep ({slope:F1}°)" );
				}
				reasons.Add( $"slope={slope:F1}°" );
			}

			// 5. Avoid industrial (smithy, forge) nearby?
			if ( reqs.AvoidIndustrial )
			{
				bool nearIndustrial = false;
				foreach ( var task in ConstructionDirector.AllTasks() )
				{
					if ( task.BuildTask == null ) continue;
					var tt = task.BuildTask.TaskType ?? "";
					if ( tt != "smithy" && tt != "forge" ) continue;
					var dist = Vector3.DistanceBetween( pos, task.BuildTask.Position );
					if ( dist < 50f * 39.37f )
					{
						nearIndustrial = true;
						break;
					}
				}
				if ( nearIndustrial )
				{
					score -= 0.2f;
					reasons.Add( "near industrial" );
				}
			}

			// 6. Near center preference (closer = slightly better, but
			//    not so close that it overlaps the core).
			if ( reqs.NearCenter.HasValue )
			{
				float centerDist = Vector3.DistanceBetween( pos, reqs.NearCenter.Value );
				if ( reqs.MaxCenterDistance > 0 && centerDist > reqs.MaxCenterDistance )
				{
					return ( 0f, "too far from center" );
				}
				// Moderate distance from center is preferred (not too
				// close, not too far).
				float centerScore = 1f - MathF.Abs( centerDist - 80f * 39.37f ) / ( 100f * 39.37f );
				score += MathF.Max( 0f, centerScore ) * 0.2f;
			}

			return ( score, string.Join( ", ", reasons ) );
		}

		/// <summary>
		/// Estimate distance to the nearest road task. Roads are
		/// ConstructionDirector tasks with TaskType "road".
		/// </summary>
		float DistanceToNearestRoad( Vector3 pos )
		{
			float best = float.MaxValue;
			foreach ( var task in ConstructionDirector.AllTasks() )
			{
				if ( task.BuildTask?.TaskType != "road" ) continue;
				if ( task.Status != TaskStatus.Complete &&
					 task.Status != TaskStatus.InProgress &&
					 task.Status != TaskStatus.Pending ) continue;

				var dist = Vector3.DistanceBetween( pos, task.BuildTask.Position );
				if ( dist < best ) best = dist;
			}
			return best;
		}

		/// <summary>
		/// Estimate terrain slope at a candidate site by raycasting
		/// at several points and comparing heights.
		/// </summary>
		float EstimateSlope( Vector3 center, float width, float depth )
		{
			// Sample 4 corners + center, get ground heights.
			float halfW = width / 2f;
			float halfD = depth / 2f;
			var samples = new Vector3[]
			{
				center + new Vector3( -halfW, -halfD, 0 ),
				center + new Vector3(  halfW, -halfD, 0 ),
				center + new Vector3( -halfW,  halfD, 0 ),
				center + new Vector3(  halfW,  halfD, 0 ),
			};

			float minZ = float.MaxValue;
			float maxZ = float.MinValue;
			foreach ( var s in samples )
			{
				var hit = Scene.Trace.Ray( s + Vector3.Up * 5000, s - Vector3.Up * 5000 ).Run();
				if ( hit.Hit )
				{
					var z = hit.EndPosition.z;
					if ( z < minZ ) minZ = z;
					if ( z > maxZ) maxZ = z;
				}
			}

			if ( minZ == float.MaxValue ) return 0f; // no ground found

			float heightDiff = maxZ - minZ;
			float span = MathF.Max( width, depth );
			return MathF.Atan( heightDiff / span ) * 180f / MathF.PI;
		}

		Vector3? VillageCenter()
		{
			var tasks = ConstructionDirector.AllTasks();
			if ( tasks.Count > 0 )
			{
				float sx = 0, sy = 0;
				foreach ( var t in tasks )
				{
					var p = t.BuildTask?.Position ?? Vector3.Zero;
					sx += p.x; sy += p.y;
				}
				return new Vector3( sx / tasks.Count, sy / tasks.Count, 0f );
			}
			return null;
		}

		int EstimateBaseWidth( string structureType ) => structureType switch
		{
			"cottage" => 4,
			"shop" => 4,
			"smithy" => 5,
			"tavern" => 6,
			"chapel" => 5,
			"storage" => 3,
			"guardhouse" => 4,
			_ => 4,
		};

		int EstimateBaseHeight( string structureType ) => structureType switch
		{
			"chapel" => 7,
			"tavern" => 5,
			"smithy" => 5,
			_ => 4,
		};

		// ── Movement (same pattern as HaulerController) ──

		bool WithinStopRadius( Vector3 target )
		{
			var dist = Vector3.DistanceBetween(
				WorldPosition.WithZ( 0 ), target.WithZ( 0 ) );
			return dist < 80f;
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
				Log.Info( $"Lute: Surveyor '{NpcName}' walking — pos={WorldPosition}, dist={dist:F1}, navmesh={_usingNavMesh}." );
			}
		}

		bool TrySteerWithNavMesh( Vector3 target )
		{
			_usingNavMesh = false;
			var nav = NavAgent;
			if ( nav is null || !nav.Enabled ) return false;

			var sceneNav = Scene?.NavMesh;
			if ( sceneNav is null || !sceneNav.IsEnabled ) return false;

			nav.MoveTo( target );
			var wish = nav.WishVelocity;
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
