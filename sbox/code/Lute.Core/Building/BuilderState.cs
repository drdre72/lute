namespace Lute.Core.Building;

/// <summary>
/// Per-builder runtime state. Pure data — no engine references. The
/// engine-side <c>BuilderState</c> adds capability sets; this core
/// model carries only the scheduling-relevant fields.
/// </summary>
public sealed class BuilderState
{
	public int BuilderId { get; set; }
	public string? NpcName { get; set; }
	public string? CurrentTaskId { get; set; }
	public int PiecesBuilt { get; set; }
	public string? LastCompletedTaskId { get; set; }
	public bool Active { get; set; }

	/// <summary>
	/// Profession id (e.g. "mason", "carpenter"). Defaults to "builder"
	/// for backward compatibility with NPCs that don't have a profession
	/// profile.
	/// </summary>
	public string ProfessionId { get; set; } = "builder";
}
