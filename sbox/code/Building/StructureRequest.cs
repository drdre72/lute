using System;
using System.Collections.Generic;
using Sandbox;

namespace Lute.Building
{
	/// <summary>
	/// A request to build a specific structure type at a suitable site.
	/// This is the interface that breaks the dependency on predetermined
	/// coordinates — the settlement planner generates requests based
	/// on needs, the surveyor selects a valid site, and the
	/// ConstructionDirector schedules the build.
	///
	/// Flow:
	/// <code>
	/// SettlementNeed
	///   ↓
	/// StructureRequest (type + requirements, no position)
	///   ↓
	/// Surveyor: query SpatialRegistry, terrain, roads → candidate parcels
	///   ↓
	/// Spatial validator: validate the chosen parcel
	///   ↓
	/// Plot reservation
	///   ↓
	/// Blueprint generation (Blueprint/DAG)
	///   ↓
	/// ConstructionDirector: register as DirectedTask(s)
	///   ↓
	/// NPC workforce builds it
	/// </code>
	/// </summary>
	public sealed class StructureRequest
	{
		/// <summary> Unique id for this request. </summary>
		public string Id { get; init; } = $"req_{Guid.NewGuid():N}".Substring( 0, 12 );

		/// <summary>
		/// The structure type to build (e.g. "cottage", "sawmill",
		/// "well", "wall", "road", "smithy", "tavern", "chapel").
		/// </summary>
		public string StructureType { get; init; }

		/// <summary>
		/// Who requested this structure (e.g. "SettlementPlanner",
		/// "Quartermaster", a profession name, or "player:Name").
		/// </summary>
		public string RequestedBy { get; init; }

		/// <summary>
		/// Priority 0.0–1.0. Higher = more urgent. Determines ordering
		/// relative to other pending requests.
		/// </summary>
		public float Priority { get; init; }

		/// <summary>
		/// The <see cref="SettlementNeed"/> that generated this request,
		/// if any. Null for direct requests (e.g. player-initiated).
		/// </summary>
		public string NeedId { get; init; }

		/// <summary>
		/// Site requirements — constraints the surveyor must satisfy
		/// when selecting a build location.
		/// </summary>
		public SiteRequirements Requirements { get; init; }

		/// <summary>
		/// Human-readable reason this structure is being requested
		/// (for logging/debugging).
		/// </summary>
		public string Reason { get; init; }

		/// <summary>
		/// The resolved build position, set by the surveyor after
		/// site selection. Null until a site is chosen.
		/// </summary>
		public Vector3? ResolvedPosition { get; set; }

		/// <summary>
		/// The resolved rotation (yaw degrees), set by the surveyor.
		/// </summary>
		public float? ResolvedRotation { get; set; }

		/// <summary>
		/// True when the surveyor has selected and validated a site.
		/// </summary>
		public bool IsResolved => ResolvedPosition.HasValue;

		/// <summary>
		/// Status of this request in the pipeline.
		/// </summary>
		public StructureRequestStatus Status { get; set; } = StructureRequestStatus.Pending;

		public StructureRequest()
		{
		}
	}

	/// <summary>
	/// Status of a StructureRequest in the planning pipeline.
	/// </summary>
	public enum StructureRequestStatus
	{
		/// <summary> Just created, waiting for surveyor. </summary>
		Pending,
		/// <summary> Surveyor is evaluating candidate sites. </summary>
		Surveying,
		/// <summary> Surveyor found a valid site; awaiting blueprint + registration. </summary>
		Resolved,
		/// <summary> Registered with ConstructionDirector as task(s). </summary>
		Dispatched,
		/// <summary> No valid site found. </summary>
		NoValidSite,
		/// <summary> Cancelled (need withdrawn or overridden). </summary>
		Cancelled,
	}

	/// <summary>
	/// Spatial constraints for a structure site. The surveyor uses these
	/// to score and filter candidate parcels.
	/// </summary>
	public sealed class SiteRequirements
	{
		/// <summary> Must be adjacent to or near a road. </summary>
		public bool NearRoad { get; init; }

		/// <summary> Maximum distance from a road (inches, 0 = no limit). </summary>
		public float MaxRoadDistance { get; init; } = 0f;

		/// <summary> Minimum footprint area (width × depth, in inches). </summary>
		public float MinArea { get; init; } = 0f;

		/// <summary> Required width (inches, 0 = any). </summary>
		public float MinWidth { get; init; } = 0f;

		/// <summary> Required depth (inches, 0 = any). </summary>
		public float MinDepth { get; init; } = 0f;

		/// <summary> Maximum ground slope angle (degrees, 0 = flat only). </summary>
		public float MaxSlope { get; init; } = 0f;

		/// <summary> Must avoid being near industrial structures (smithy, mine). </summary>
		public bool AvoidIndustrial { get; init; }

		/// <summary> Must avoid being near residential structures. </summary>
		public bool AvoidResidential { get; init; }

		/// <summary> Must avoid overlapping with walls or fortifications. </summary>
		public bool AvoidWalls { get; init; }

		/// <summary> Must be within this distance of a stockpile (inches, 0 = no constraint). </summary>
		public float NearStockpile { get; init; } = 0f;

		/// <summary> Must be within this distance of a water source (inches, 0 = no constraint). </summary>
		public float NearWater { get; init; } = 0f;

		/// <summary>
		/// Preferred center to build near (e.g. village center).
		/// Candidates are scored by proximity to this point.
		/// </summary>
		public Vector3? NearCenter { get; init; }

		/// <summary>
		/// Maximum distance from NearCenter (inches, 0 = no limit).
		/// </summary>
		public float MaxCenterDistance { get; init; } = 0f;
	}
}
