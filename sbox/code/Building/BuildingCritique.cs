using System.Collections.Generic;

namespace Lute.Building
{
	/// <summary>
	/// Structured critique payload from the LLM/NLP brain. The LLM outputs
	/// this as JSON, and <see cref="BlueprintModifier"/> applies it to the
	/// village task queue. This is the bridge between qualitative reasoning
	/// (style, scale, modules) and deterministic geometry execution.
	///
	/// Critique sources:
	/// 1. Game Master (Director Rules) — global game state directives
	/// 2. Partner NPCs (Multi-Agent Sync) — spatial coordination
	/// 3. Devin AI (Dev/Validation Mode) — automated testing feedback
	/// </summary>
	public class BuildingCritique
	{
		/// <summary> Brief reason for the critique (for logs). </summary>
		public string Reason { get; set; } = "";

		/// <summary> Multiplier for WealthFactor on the target task. 1.0 = no change. </summary>
		public float WealthModifier { get; set; } = 1.0f;

		/// <summary> Style tag: "Romanesque", "Gothic", "Classical", "Vernacular", "Fortress". Empty = no change. </summary>
		public string StyleTag { get; set; } = "";

		/// <summary> Modules to add (creates new VillageBuildTasks). e.g. ["Watchtower", "Forge"]. </summary>
		public List<string> AddModules { get; set; } = new();

		/// <summary> Modules to remove (cancels pending tasks by type). e.g. ["Roof"]. </summary>
		public List<string> RemoveModules { get; set; } = new();

		/// <summary> Position offset to shift the structure (inches). Null = no change. </summary>
		public Vector3? PositionOffset { get; set; }

		/// <summary> Target task index to modify. -1 = next pending task. </summary>
		public int TargetTaskIndex { get; set; } = -1;

		/// <summary> Material override path. Empty = no change. </summary>
		public string MaterialOverride { get; set; } = "";

		/// <summary> Source of the critique (GameMaster, NPC, DevinAI). </summary>
		public string CriticSource { get; set; } = "Unknown";
	}
}
