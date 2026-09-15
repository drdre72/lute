using System;
using System.Collections.Generic;
using System.Linq;
using Lute.Items;
using Sandbox;

namespace Lute.Building
{
	/// <summary>
	/// Central registry for settlement needs and structure requests.
	/// The SettlementNeedBoard is the bridge between world state
	/// (population, resource levels, existing structures) and the
	/// construction pipeline (ConstructionDirector).
	///
	/// Per the adaptive settlement planning proposal:
	/// - Needs are informational — they describe conditions the
	///   settlement should respond to, not direct orders.
	/// - Needs generate StructureRequests when urgency is high enough.
	/// - StructureRequests are picked up by the Surveyor (future) who
	///   selects a valid site.
	/// - The ConstructionDirector remains the authoritative scheduler.
	///
	/// This board is deterministic: given the same world state and
	/// the same needs, it produces the same structure requests in the
	/// same order.
	/// </summary>
	public static class SettlementNeedBoard
	{
		static readonly List<SettlementNeed> _needs = new();
		static readonly List<StructureRequest> _requests = new();
		static float _lastEvaluation;
		static bool _initialized;
		static int _nextNeedId;
		static int _nextRequestId;

		/// <summary> How often (seconds) to re-evaluate needs. </summary>
		public static float EvaluationInterval { get; set; } = 10f;

		/// <summary> All active needs. </summary>
		public static IReadOnlyList<SettlementNeed> Needs => _needs;

		/// <summary> All structure requests (all statuses). </summary>
		public static IReadOnlyList<StructureRequest> Requests => _requests;

		/// <summary> Pending structure requests (not yet resolved/dispatched). </summary>
		public static IEnumerable<StructureRequest> PendingRequests =>
			_requests.Where( r => r.Status == StructureRequestStatus.Pending );

		/// <summary> Resolved requests (surveyor picked a site, awaiting dispatch). </summary>
		public static IEnumerable<StructureRequest> ResolvedRequests =>
			_requests.Where( r => r.Status == StructureRequestStatus.Resolved );

		/// <summary>
		/// Register a need. If a need with the same Type and StructureType
		/// already exists, update its urgency/count instead of duplicating.
		/// </summary>
		public static void RegisterNeed( SettlementNeed need )
		{
			var existing = _needs.FirstOrDefault( n =>
				n.Type == need.Type && n.StructureType == need.StructureType );

			if ( existing != null )
			{
				existing.Urgency = MathF.Max( existing.Urgency, need.Urgency );
				// Only update ExistingCount from a fresh need if the
				// existing need has no completed structures yet. This
				// lets world-state evaluators set the baseline existing
				// count (e.g. count of built cottages) without clobbering
				// lifecycle counts accumulated by the dispatch/complete
				// pipeline.
				if ( need.ExistingCount > existing.ExistingCount )
					existing.ExistingCount = need.ExistingCount;
				existing.DesiredCount = Math.Max( existing.DesiredCount, need.DesiredCount );
				return;
			}

			// Board owns ID allocation — assign a deterministic id if the
			// need doesn't already have one.
			if ( string.IsNullOrEmpty( need.Id ) || need.Id == "need_unassigned" )
				need = new SettlementNeed( $"need_{_nextNeedId:D6}" )
				{
					Type = need.Type,
					StructureType = need.StructureType,
					Urgency = need.Urgency,
					DesiredCount = need.DesiredCount,
					ExistingCount = need.ExistingCount,
					PlannedCount = need.PlannedCount,
					Reason = need.Reason,
				};
			_nextNeedId++;

			_needs.Add( need );
			Log.Info( $"Lute: SettlementNeedBoard — registered need {need.Type}" +
				( need.StructureType != null ? $" ({need.StructureType})" : "" ) +
				$" urgency={need.Urgency:F2} desired={need.DesiredCount} existing={need.ExistingCount}" +
				$" reason=\"{need.Reason}\"" );
		}

		/// <summary> Remove a need by id. </summary>
		public static void WithdrawNeed( string needId )
		{
			_needs.RemoveAll( n => n.Id == needId );
		}

		/// <summary> Remove fulfilled needs. </summary>
		public static void PruneFulfilled()
		{
			_needs.RemoveAll( n => n.IsFulfilled );
		}

		/// <summary>
		/// Reconcile all needs' lifecycle counts from the authoritative
		/// ConstructionDirector task catalog. This is the single source of
		/// truth — the need board does NOT maintain its own independent
		/// count of structures. Per the professor's review: "Don't
		/// synchronize copies of truth when Lute already has an authority
		/// that can derive the answer."
		///
		/// For each need with a StructureType, counts are derived by
		/// scanning all ConstructionDirector tasks matching that type:
		///   Complete  → ExistingCount
		///   Pending/PendingExecution/Blocked → PlannedCount
		///   InProgress → InProgressCount
		///   Failed/Cancelled → FailedCount
		///
		/// This handles the case where pre-existing (grammar-generated)
		/// cottages complete but have no StructureRequest/DirectedTaskId
		/// linkage — the need still sees them as ExistingCount.
		/// </summary>
		public static void ReconcileFromDirector()
		{
			var allTasks = ConstructionDirector.AllTasks();
			if ( allTasks.Count == 0 ) return;

			foreach ( var need in _needs )
			{
				if ( string.IsNullOrEmpty( need.StructureType ) ) continue;
				if ( need.Type == SettlementNeedType.ResourcePressure ) continue;

				int existing = 0, planned = 0, inProgress = 0, failed = 0;
				foreach ( var t in allTasks )
				{
					var taskType = t.BuildTask?.TaskType;
					if ( taskType != need.StructureType ) continue;

					switch ( t.Status )
					{
						case TaskStatus.Complete:
							existing++;
							break;
						case TaskStatus.Pending:
						case TaskStatus.PendingExecution:
						case TaskStatus.Blocked:
							planned++;
							break;
						case TaskStatus.InProgress:
							inProgress++;
							break;
						case TaskStatus.Failed:
						case TaskStatus.Cancelled:
							failed++;
							break;
					}
				}

				// Only log if counts changed (avoid spamming every evaluate cycle).
				if ( need.ExistingCount != existing || need.PlannedCount != planned ||
					 need.InProgressCount != inProgress || need.FailedCount != failed )
				{
					need.ExistingCount = existing;
					need.PlannedCount = planned;
					need.InProgressCount = inProgress;
					need.FailedCount = failed;
					Log.Info( $"Lute: SettlementNeedBoard — reconciled need {need.Id} ({need.StructureType})" +
						$" from Director: existing={existing} planned={planned} inProgress={inProgress} failed={failed}" +
						$" desired={need.DesiredCount} fulfilled={need.IsFulfilled}" );
				}
			}
		}

		/// <summary>
		/// Create a StructureRequest from a need if the need's urgency
		/// is above the threshold and no pending request already exists
		/// for the same structure type.
		/// </summary>
		public static void GenerateRequests( float urgencyThreshold = 0.3f )
		{
			foreach ( var need in _needs.ToList() )
			{
				if ( need.IsFulfilled ) continue;
				if ( need.Urgency < urgencyThreshold ) continue;
				if ( need.Type == SettlementNeedType.ResourcePressure ) continue;
				if ( string.IsNullOrEmpty( need.StructureType ) ) continue;

				// Duplicate-suppression: don't create a new request if the
				// pipeline already has enough structures (completed +
				// planned + in-progress) to satisfy DesiredCount. This
				// prevents redundant requests WITHOUT counting planned
				// work as fulfilling the need.
				if ( need.PipelineCount >= need.DesiredCount ) continue;

				// Also check for an existing pending/surveying/resolved
				// request for the same structure type that hasn't been
				// dispatched yet.
				bool hasPending = _requests.Any( r =>
					r.StructureType == need.StructureType &&
					( r.Status == StructureRequestStatus.Pending ||
					  r.Status == StructureRequestStatus.Surveying ||
					  r.Status == StructureRequestStatus.Resolved ) );

				if ( hasPending ) continue;

				var req = new StructureRequest( $"req_{_nextRequestId:D6}" )
				{
					StructureType = need.StructureType,
					RequestedBy = "SettlementNeedBoard",
					Priority = need.Urgency,
					NeedId = need.Id,
					Reason = need.Reason,
					Requirements = SiteRequirementsFor( need ),
				};
				_nextRequestId++;

				_requests.Add( req );
				Log.Info( $"Lute: SettlementNeedBoard — generated StructureRequest for '{need.StructureType}'" +
					$" priority={req.Priority:F2} need={need.Id}" );
			}
		}

		/// <summary>
		/// Determine site requirements for a given need type.
		/// Different structure types have different spatial constraints.
		/// </summary>
		static SiteRequirements SiteRequirementsFor( SettlementNeed need )
		{
			return need.StructureType switch
			{
				"cottage" or "shop" or "tavern" or "guardhouse" => new SiteRequirements
				{
					NearRoad = true,
					MaxRoadDistance = 15f * 39.37f, // 15m
					MinWidth = 4f * 100f,           // 4 cells
					MinDepth = 4f * 100f,
					MaxSlope = 10f,                 // 10 degrees
					AvoidIndustrial = true,
					NearCenter = VillageCenter(),
					MaxCenterDistance = 200f * 39.37f, // 200m
				},
				"smithy" => new SiteRequirements
				{
					NearRoad = true,
					MaxRoadDistance = 20f * 39.37f,
					MinWidth = 5f * 100f,
					MinDepth = 5f * 100f,
					MaxSlope = 8f,
					AvoidResidential = true,
					NearStockpile = 50f * 39.37f,
					NearCenter = VillageCenter(),
					MaxCenterDistance = 200f * 39.37f,
				},
				"sawmill" => new SiteRequirements
				{
					NearRoad = true,
					MaxRoadDistance = 30f * 39.37f,
					MinWidth = 4f * 100f,
					MinDepth = 4f * 100f,
					MaxSlope = 8f,
					NearCenter = VillageCenter(),
					MaxCenterDistance = 250f * 39.37f,
				},
				"well" => new SiteRequirements
				{
					NearCenter = VillageCenter(),
					MaxCenterDistance = 50f * 39.37f,
					MinWidth = 2f * 39.37f,
					MinDepth = 2f * 39.37f,
				},
				"market_square" or "market" => new SiteRequirements
				{
					NearRoad = true,
					MaxRoadDistance = 5f * 39.37f,
					MinWidth = 20f * 39.37f,
					MinDepth = 20f * 39.37f,
					NearCenter = VillageCenter(),
					MaxCenterDistance = 50f * 39.37f,
				},
				"chapel" => new SiteRequirements
				{
					NearRoad = true,
					MaxRoadDistance = 15f * 39.37f,
					MinWidth = 5f * 100f,
					MinDepth = 7f * 100f,
					NearCenter = VillageCenter(),
					MaxCenterDistance = 100f * 39.37f,
				},
				"storage" => new SiteRequirements
				{
					NearRoad = true,
					MaxRoadDistance = 10f * 39.37f,
					MinWidth = 3f * 100f,
					MinDepth = 3f * 100f,
					NearStockpile = 30f * 39.37f,
					NearCenter = VillageCenter(),
					MaxCenterDistance = 100f * 39.37f,
				},
				_ => new SiteRequirements
				{
					NearRoad = false,
					NearCenter = VillageCenter(),
					MaxCenterDistance = 200f * 39.37f,
				},
			};
		}

		/// <summary>
		/// Get the village center (cached from the first VillageBuilder
		/// or Gate3Benchmark found). Returns Vector3.Zero if unknown.
		/// </summary>
		static Vector3? _villageCenter;
		static Vector3? VillageCenter()
		{
			if ( _villageCenter.HasValue ) return _villageCenter;

			// Try to find the village center from existing construction tasks.
			var tasks = ConstructionDirector.AllTasks();
			if ( tasks.Count > 0 )
			{
				// Average of all task positions = approximate center
				float sx = 0, sy = 0, sz = 0;
				foreach ( var t in tasks )
				{
					var p = t.BuildTask?.Position ?? Vector3.Zero;
					sx += p.x; sy += p.y; sz += p.z;
				}
				_villageCenter = new Vector3( sx / tasks.Count, sy / tasks.Count, sz / tasks.Count );
				return _villageCenter;
			}

			return null;
		}

		/// <summary>
		/// Mark a request as resolved (surveyor selected a site).
		/// </summary>
		public static void ResolveRequest( string requestId, Vector3 position, float rotation )
		{
			var req = _requests.FirstOrDefault( r => r.Id == requestId );
			if ( req == null ) return;
			req.ResolvedPosition = position;
			req.ResolvedRotation = rotation;
			req.Status = StructureRequestStatus.Resolved;
			Log.Info( $"Lute: SettlementNeedBoard — request {req.Id} ({req.StructureType}) resolved at {position}" );
		}

		/// <summary>
		/// Dispatched/reserved site positions (rounded to ~1m grid) so the
		/// Surveyor doesn't pick the same spot repeatedly before the builder
		/// has registered it in SpatialRegistry.
		/// </summary>
		static readonly HashSet<long> _dispatchedSites = new();

		static long SiteKey( Vector3 pos )
		{
			// Round to ~1m (39.37 units) grid to deduplicate nearby sites.
			long x = (long)Math.Round( pos.x / 39.37f );
			long y = (long)Math.Round( pos.y / 39.37f );
			return ( x << 32 ) ^ ( y & 0xFFFFFFFFL );
		}

		/// <summary> True if a site within ~minSep meters of pos was already dispatched. </summary>
		public static bool IsSiteTaken( Vector3 pos, float minSepMeters = 8f )
		{
			long key = SiteKey( pos );
			// Check a small neighborhood of grid cells.
			int span = Math.Max( 1, (int)Math.Ceiling( minSepMeters / 1f ) );
			for ( int dx = -span; dx <= span; dx++ )
			for ( int dy = -span; dy <= span; dy++ )
			{
				long k = key + ( (long)dx << 32 ) + dy;
				if ( _dispatchedSites.Contains( k ) ) return true;
			}
			return false;
		}

		/// <summary>
		/// Mark a request as dispatched (registered with ConstructionDirector).
		/// Records the resolved site as taken and links the request to the
		/// ConstructionDirector task. Does NOT manually increment need
		/// counts — those are derived from the authoritative task catalog
		/// by <see cref="ReconcileFromDirector"/> during the next
		/// <see cref="Evaluate"/> cycle.
		/// </summary>
		public static void DispatchRequest( string requestId, string directedTaskId = null )
		{
			var req = _requests.FirstOrDefault( r => r.Id == requestId );
			if ( req == null ) return;
			req.Status = StructureRequestStatus.Dispatched;
			req.DirectedTaskId = directedTaskId;

			// Record the resolved site as taken so the Surveyor won't
			// pick the same spot again next cycle.
			if ( req.ResolvedPosition.HasValue )
				_dispatchedSites.Add( SiteKey( req.ResolvedPosition.Value ) );

			Log.Info( $"Lute: SettlementNeedBoard — request {req.Id} ({req.StructureType}) dispatched as task {directedTaskId}" );
		}

		/// <summary>
		/// Mark a request as having no valid site.
		/// </summary>
		public static void RejectRequest( string requestId, string reason )
		{
			var req = _requests.FirstOrDefault( r => r.Id == requestId );
			if ( req == null ) return;
			req.Status = StructureRequestStatus.NoValidSite;
			Log.Warning( $"Lute: SettlementNeedBoard — request {req.Id} ({req.StructureType}) rejected: {reason}" );
		}

		/// <summary>
		/// Cancel a request.
		/// </summary>
		public static void CancelRequest( string requestId )
		{
			var req = _requests.FirstOrDefault( r => r.Id == requestId );
			if ( req == null ) return;
			req.Status = StructureRequestStatus.Cancelled;
		}

		/// <summary>
		/// Find the StructureRequest associated with a ConstructionDirector
		/// task id (set by <see cref="DispatchRequest"/>). Returns null if
		/// no request tracks this task.
		/// </summary>
		public static StructureRequest FindRequestByTaskId( string directedTaskId )
		{
			if ( string.IsNullOrEmpty( directedTaskId ) ) return null;
			return _requests.FirstOrDefault( r => r.DirectedTaskId == directedTaskId );
		}

		/// <summary>
		/// Notify that a dispatched structure's task completed. Immediate
		/// feedback only — the authoritative count reconciliation happens
		/// in <see cref="ReconcileFromDirector"/> during the next
		/// <see cref="Evaluate"/> cycle.
		/// </summary>
		public static void OnTaskCompleted( string directedTaskId )
		{
			var req = FindRequestByTaskId( directedTaskId );
			if ( req == null ) return;
			Log.Info( $"Lute: SettlementNeedBoard — task {directedTaskId} completed (request {req.Id}, type {req.StructureType}). Need counts will reconcile on next Evaluate." );
		}

		/// <summary>
		/// Notify that a dispatched structure's task failed permanently.
		/// Immediate feedback only — the authoritative count
		/// reconciliation happens in <see cref="ReconcileFromDirector"/>.
		/// </summary>
		public static void OnTaskFailed( string directedTaskId )
		{
			var req = FindRequestByTaskId( directedTaskId );
			if ( req == null ) return;
			Log.Warning( $"Lute: SettlementNeedBoard — task {directedTaskId} failed (request {req.Id}, type {req.StructureType}). Need will reopen on next Evaluate." );
		}

		/// <summary>
		/// Notify that a dispatched structure's task was cancelled.
		/// Immediate feedback only — the authoritative count
		/// reconciliation happens in <see cref="ReconcileFromDirector"/>.
		/// </summary>
		public static void OnTaskCancelled( string directedTaskId )
		{
			var req = FindRequestByTaskId( directedTaskId );
			if ( req == null ) return;
			Log.Info( $"Lute: SettlementNeedBoard — task {directedTaskId} cancelled (request {req.Id}, type {req.StructureType}). Need will reopen on next Evaluate." );
		}

		/// <summary>
		/// Periodic evaluation: prune fulfilled needs, generate new
		/// structure requests from active needs.
		/// </summary>
		public static void Evaluate( float currentTime )
		{
			if ( currentTime - _lastEvaluation < EvaluationInterval ) return;
			_lastEvaluation = currentTime;

			// Reconcile lifecycle counts from the authoritative
			// ConstructionDirector task catalog BEFORE generating requests.
			// This is the single source of truth — the need board does not
			// maintain an independent structure count.
			ReconcileFromDirector();

			PruneFulfilled();
			GenerateRequests();

			if ( _needs.Count > 0 || _requests.Count > 0 )
			{
				Log.Info( $"Lute: SettlementNeedBoard — {_needs.Count} needs, {_requests.Count} requests" +
					$" ({PendingRequests.Count()} pending, {ResolvedRequests.Count()} resolved)" );
			}
		}

		/// <summary> Clear all needs and requests (fresh start). </summary>
		public static void Clear()
		{
			_needs.Clear();
			_requests.Clear();
			_dispatchedSites.Clear();
			_lastEvaluation = 0f;
			_villageCenter = null;
			// Reset ID counters so ids are deterministic relative to a
			// fresh simulation, not just sequential across play sessions.
			_nextNeedId = 0;
			_nextRequestId = 0;
		}

		/// <summary> Diagnostic summary. </summary>
		public static string Summary()
		{
			var sb = new System.Text.StringBuilder();
			sb.AppendLine( $"Lute: SettlementNeedBoard — {_needs.Count} needs, {_requests.Count} requests" );
			foreach ( var n in _needs.OrderBy( n => -n.Urgency ) )
			{
				sb.AppendLine( $"  Need {n.Type}" +
					( n.StructureType != null ? $" ({n.StructureType})" : "" ) +
						$" urgency={n.Urgency:F2} existing={n.ExistingCount}/{n.DesiredCount}" +
							$" planned={n.PlannedCount} inProgress={n.InProgressCount} failed={n.FailedCount}" +
							$" fulfilled={n.IsFulfilled}" );
			}
			foreach ( var r in _requests )
			{
				sb.AppendLine( $"  Request {r.Id} {r.StructureType} pri={r.Priority:F2} status={r.Status}" +
					( r.IsResolved ? $" at {r.ResolvedPosition}" : "" ) );
			}
			return sb.ToString();
		}

		[ConCmd( "settlement_needs" )]
		static void NeedsCmd()
		{
			Log.Info( Summary() );
		}
	}
}
