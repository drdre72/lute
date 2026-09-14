using System;
using System.Collections.Generic;
using Lute.Items;

namespace Lute.Building
{
	/// <summary>
	/// Types of settlement needs. A need is a condition the settlement
	/// should respond to — not a direct order. Needs generate
	/// <see cref="StructureRequest"/>s and resource pressure, which
	/// the existing authorities (ConstructionDirector, LogisticsBoard)
	/// act on.
	///
	/// Per the adaptive settlement planning proposal: the settlement
	/// decides what should exist based on world state, not from a
	/// prewritten village layout.
	/// </summary>
	public enum SettlementNeedType
	{
		/// <summary> Not enough housing for the population. </summary>
		Shelter,
		/// <summary> Need a production structure (sawmill, brickyard, smithy). </summary>
		Production,
		/// <summary> Need storage capacity (stockpile, warehouse). </summary>
		Storage,
		/// <summary> Need a civic structure (well, market, chapel, town hall). </summary>
		Civic,
		/// <summary> Need defensive structures (wall, gate, guardhouse). </summary>
		Defense,
		/// <summary> Need infrastructure (road, bridge). </summary>
		Infrastructure,
		/// <summary> Resource shortage is creating production pressure. </summary>
		ResourcePressure,
	}

	/// <summary>
	/// A settlement need — a condition the settlement should respond to.
	/// Needs are informational: they generate StructureRequests and
	/// resource pressure, but do not directly mutate the world.
	/// The existing authorities remain authoritative.
	/// </summary>
	public sealed class SettlementNeed
	{
		/// <summary> Unique id for this need instance. </summary>
		public string Id { get; init; }

		/// <summary> What kind of need this is. </summary>
		public SettlementNeedType Type { get; init; }

		/// <summary>
		/// Urgency 0.0–1.0. Higher = more pressing. Determined by
		/// deterministic rules (e.g. housing capacity vs population,
		/// resource demand vs supply).
		/// </summary>
		public float Urgency { get; set; }

		/// <summary> How many of this thing are desired. </summary>
		public int DesiredCount { get; set; }

		/// <summary> How many currently exist. </summary>
		public int ExistingCount { get; set; }

		/// <summary>
		/// The specific structure type that would satisfy this need
		/// (e.g. "cottage", "sawmill", "well"). Null for resource
		/// pressure needs.
		/// </summary>
		public string StructureType { get; init; }

		/// <summary>
		/// Resource pressure map: which resources are in short supply
		/// and by how much. Used for ResourcePressure needs.
		/// </summary>
		public Dictionary<ItemType, int> ResourcePressure { get; init; }

		/// <summary>
		/// Human-readable reason this need exists (for logging/debugging).
		/// </summary>
		public string Reason { get; init; }

		/// <summary>
		/// When this need was created (game time). Used for priority
		/// ordering and stale-need detection.
		/// </summary>
		public float CreatedAt { get; init; }

		/// <summary>
		/// True when the need has been fulfilled (ExistingCount >= DesiredCount
		/// or resource pressure resolved).
		/// </summary>
		public bool IsFulfilled =>
			Type == SettlementNeedType.ResourcePressure
				? (ResourcePressure == null || ResourcePressure.Count == 0)
				: ExistingCount >= DesiredCount;

		public SettlementNeed()
		{
			Id = $"need_{Guid.NewGuid():N}".Substring( 0, 12 );
			CreatedAt = 0f;
		}
	}
}
