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
				existing.ExistingCount = need.ExistingCount;
				existing.DesiredCount = Math.Max( existing.DesiredCount, need.DesiredCount );
				return;
			}

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

				// Don't create a duplicate request for the same structure type
				// that's still pending or being surveyed.
				bool hasPending = _requests.Any( r =>
					r.StructureType == need.StructureType &&
					( r.Status == StructureRequestStatus.Pending ||
					  r.Status == StructureRequestStatus.Surveying ||
					  r.Status == StructureRequestStatus.Resolved ) );

				if ( hasPending ) continue;

				var req = new StructureRequest
				{
					StructureType = need.StructureType,
					RequestedBy = "SettlementNeedBoard",
					Priority = need.Urgency,
					NeedId = need.Id,
					Reason = need.Reason,
					Requirements = SiteRequirementsFor( need ),
				};

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
		/// Mark a request as dispatched (registered with ConstructionDirector).
		/// </summary>
		public static void DispatchRequest( string requestId )
		{
			var req = _requests.FirstOrDefault( r => r.Id == requestId );
			if ( req == null ) return;
			req.Status = StructureRequestStatus.Dispatched;
			Log.Info( $"Lute: SettlementNeedBoard — request {req.Id} ({req.StructureType}) dispatched" );
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
		/// Periodic evaluation: prune fulfilled needs, generate new
		/// structure requests from active needs.
		/// </summary>
		public static void Evaluate( float currentTime )
		{
			if ( currentTime - _lastEvaluation < EvaluationInterval ) return;
			_lastEvaluation = currentTime;

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
			_lastEvaluation = 0f;
			_villageCenter = null;
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
					$" urgency={n.Urgency:F2} {n.ExistingCount}/{n.DesiredCount} fulfilled={n.IsFulfilled}" );
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
