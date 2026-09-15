namespace Lute.Core.Building;

/// <summary>
/// Lifecycle states for a directed construction task. Mirrors the
/// engine-side <c>Lute.Building.TaskStatus</c> enum; kept in core so
/// the dependency graph and scheduling logic can reason about task
/// state without referencing the engine project.
/// </summary>
public enum TaskStatus
{
	Pending = 0,
	InProgress = 1,
	Complete = 2,
	Blocked = 3,
	Failed = 4,
	Cancelled = 5,
	/// <summary>
	/// Task has been claimed and reserved, but the builder has not yet
	/// arrived at the site. The executor must wait for authorization
	/// before placing geometry.
	/// </summary>
	PendingExecution = 6,
}
