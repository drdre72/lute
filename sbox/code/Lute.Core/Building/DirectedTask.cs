using System.Collections.Generic;
using System.Numerics;
using Lute.Core.Spatial;

namespace Lute.Core.Building;

/// <summary>
/// Authoritative director-side construction task. Pure data model —
/// no engine references, no static director coupling. The engine-side
/// <c>DirectedTask</c> wraps this and adds reservation/precondition
/// checks that require scene access.
/// </summary>
public sealed class DirectedTask
{
	public string Id { get; set; } = "";
	public TaskStatus Status { get; set; } = TaskStatus.Pending;
	public int AssignedBuilder { get; set; } = -1;
	public int EstimatedPieces { get; set; }
	public List<string> DependsOn { get; set; } = new();
	public string? BlueprintId { get; set; }
	public int BlueprintVersion { get; set; }
	public string? ReservationId { get; set; }
	public int RetryCount { get; set; }
	public int MaxRetries { get; set; } = 10;
	public string? FailureReason { get; set; }
	public string? BlockedByTaskId { get; set; }
	public string? BlockedByDependency { get; set; }
	public int PiecesPlaced { get; set; }

	/// <summary>
	/// Reservation bounds in core spatial representation. The engine
	/// adapter converts to/from <c>Sandbox.BBox</c>.
	/// </summary>
	public Aabb3? ReservationBounds { get; set; }

	/// <summary>
	/// 8-digit spatial grid key (XXXXYYYY) for contiguous wall
	/// assignment. Computed from the build task position.
	/// </summary>
	public string? Grid8 { get; set; }

	/// <summary>
	/// Compute an 8-digit spatial grid key from a world position.
	/// Format: XXXXYYYY where each is 4 digits of position in
	/// decimeters (10 units = 1 dm). Deterministic spatial sort key
	/// so wall segments near each other sort together.
	/// </summary>
	public static string ComputeGrid8( Vector3 pos )
	{
		int x = (int)( pos.X / 10f );
		int y = (int)( pos.Y / 10f );
		// Clamp to 4 digits (0..9999), wrap negatives.
		x = ( ( x % 10000 ) + 10000 ) % 10000;
		y = ( ( y % 10000 ) + 10000 ) % 10000;
		return $"{x:D4}{y:D4}";
	}

	/// <summary>
	/// True when every dependency in <see cref="DependsOn"/> is
	/// <see cref="TaskStatus.Complete"/>. The caller supplies a
	/// resolver so this model stays free of static director coupling.
	/// </summary>
	public bool DependenciesSatisfied( System.Func<string, TaskStatus?> resolveDependency )
	{
		foreach ( var depId in DependsOn )
		{
			var status = resolveDependency( depId );
			if ( status != TaskStatus.Complete )
				return false;
		}
		return true;
	}
}
