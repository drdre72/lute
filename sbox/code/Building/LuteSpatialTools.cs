using System.Text.Json.Nodes;
using System.Collections.Generic;
using System.Linq;
using Lute.Building;

namespace Editor.Mcp;

/// <summary>
/// Lute spatial query tools for the MCP server. Exposes the ReservationManager's
/// occupancy grid, OBB placement validation, and construction task state as
/// structured queries that Devin and other agents can call without raycasting
/// or screenshot inspection.
/// </summary>
[McpToolset( "lute_spatial", "Lute construction spatial queries - occupancy grid, placement validation, task state, and spatial relations" )]
public static class LuteSpatialTools
{
	/// <summary>
	/// List all occupied construction regions with their bounds and source task.
	/// </summary>
	[McpTool.ReadOnly( "lute_get_occupied_regions" )]
	public static OccupiedRegionsResult GetOccupiedRegions()
	{
		var regions = ReservationManager.GetOccupiedRegions();
		return new OccupiedRegionsResult
		{
			Count = regions.Count,
			Regions = regions.Select( r => new OccupiedRegionInfo
			{
				Id = r.Id,
				TaskId = r.TaskId ?? "",
				Source = r.Source ?? r.Id,
				BoundsMin = $"{r.Bounds.Mins.x:F1},{r.Bounds.Mins.y:F1},{r.Bounds.Mins.z:F1}",
				BoundsMax = $"{r.Bounds.Maxs.x:F1},{r.Bounds.Maxs.y:F1},{r.Bounds.Maxs.z:F1}",
			} ).ToArray()
		};
	}

	/// <summary>
	/// Validate whether a structure can be placed at a position without colliding
	/// with existing geometry, reservations, or built structures.
	/// </summary>
	/// <param name="x">World X position (inches)</param>
	/// <param name="y">World Y position (inches)</param>
	/// <param name="z">World Z position (inches)</param>
	/// <param name="rotation">Y-axis rotation in degrees</param>
	/// <param name="taskType">Structure type: wall, gate, road, well, market_square, or building</param>
	/// <param name="width">Override width (inches). 0 for default.</param>
	/// <param name="depth">Override depth (inches). 0 for default.</param>
	/// <param name="height">Override height (inches). 0 for default.</param>
	[McpTool.ReadOnly( "lute_check_placement" )]
	public static PlacementResult CheckPlacement(
		float x, float y, float z, float rotation,
		string taskType, float width = 0, float depth = 0, float height = 0 )
	{
		var position = new Vector3( x, y, z );
		Vector3? overrideSize = null;
		if ( width > 0 || depth > 0 || height > 0 )
			overrideSize = new Vector3( width, depth, height );

		var validation = ReservationManager.ValidatePlacement(
			position, rotation, taskType, overrideSize );

		return new PlacementResult
		{
			IsValid = validation.IsValid,
			Reason = validation.Reason ?? "",
			BlockingEntity = validation.BlockingEntity ?? "",
			IntersectionVolume = validation.IntersectionVolume,
			NearestValidPosition = validation.NearestValidPosition.HasValue
				? $"{validation.NearestValidPosition.Value.x:F1},{validation.NearestValidPosition.Value.y:F1},{validation.NearestValidPosition.Value.z:F1}"
				: "",
		};
	}

	/// <summary>
	/// Find all occupied regions that overlap a given world-space bounding box.
	/// Useful for "what's at this location?" queries.
	/// </summary>
	/// <param name="minX">Min X of the query box (inches)</param>
	/// <param name="minY">Min Y of the query box (inches)</param>
	/// <param name="minZ">Min Z of the query box (inches)</param>
	/// <param name="maxX">Max X of the query box (inches)</param>
	/// <param name="maxY">Max Y of the query box (inches)</param>
	/// <param name="maxZ">Max Z of the query box (inches)</param>
	[McpTool.ReadOnly( "lute_query_region" )]
	public static RegionQueryResult QueryRegion(
		float minX, float minY, float minZ, float maxX, float maxY, float maxZ )
	{
		var queryBox = new BBox(
			new Vector3( minX, minY, minZ ),
			new Vector3( maxX, maxY, maxZ ) );

		var regions = ReservationManager.GetOccupiedRegions()
			.Where( r => AabbOverlap( queryBox, r.Bounds ) )
			.Select( r => new OccupiedRegionInfo
			{
				Id = r.Id,
				TaskId = r.TaskId ?? "",
				Source = r.Source ?? r.Id,
				BoundsMin = $"{r.Bounds.Mins.x:F1},{r.Bounds.Mins.y:F1},{r.Bounds.Mins.z:F1}",
				BoundsMax = $"{r.Bounds.Maxs.x:F1},{r.Bounds.Maxs.y:F1},{r.Bounds.Maxs.z:F1}",
			} ).ToArray();

		return new RegionQueryResult
		{
			QueryBounds = $"{minX:F1},{minY:F1},{minZ:F1} to {maxX:F1},{maxY:F1},{maxZ:F1}",
			MatchCount = regions.Length,
			Regions = regions
		};
	}

	/// <summary>
	/// Get the current construction task state — total tasks, completed, in progress,
	/// pending, and per-builder assignments.
	/// </summary>
	[McpTool.ReadOnly( "lute_get_task_state" )]
	public static TaskStateResult GetTaskState()
	{
		var tasks = ConstructionDirector.AllTasks();
		var builders = ConstructionDirector.AllBuilders();

		return new TaskStateResult
		{
			TotalTasks = tasks.Count,
			Completed = tasks.Count( t => t.Status == TaskStatus.Complete ),
			InProgress = tasks.Count( t => t.Status == TaskStatus.InProgress ),
			Pending = tasks.Count( t => t.Status == TaskStatus.Pending ),
			PendingExecution = tasks.Count( t => t.Status == TaskStatus.PendingExecution ),
			Failed = tasks.Count( t => t.Status == TaskStatus.Failed ),
			Builders = builders.Select( b => new BuilderInfo
			{
				Name = b.NpcName ?? "",
				CurrentTaskId = b.CurrentTaskId ?? "",
				PiecesBuilt = b.PiecesBuilt,
				Active = b.Active,
			} ).ToArray()
		};
	}

	static bool AabbOverlap( BBox a, BBox b )
	{
		return a.Mins.x < b.Maxs.x && a.Maxs.x > b.Mins.x &&
			a.Mins.y < b.Maxs.y && a.Maxs.y > b.Mins.y &&
			a.Mins.z < b.Maxs.z && a.Maxs.z > b.Mins.z;
	}
}

// Result types

public class OccupiedRegionsResult
{
	public int Count { get; set; }
	public OccupiedRegionInfo[] Regions { get; set; }
}

public class OccupiedRegionInfo
{
	public string Id { get; set; }
	public string TaskId { get; set; }
	public string Source { get; set; }
	public string BoundsMin { get; set; }
	public string BoundsMax { get; set; }
}

public class PlacementResult
{
	public bool IsValid { get; set; }
	public string Reason { get; set; }
	public string BlockingEntity { get; set; }
	public float IntersectionVolume { get; set; }
	public string NearestValidPosition { get; set; }
}

public class RegionQueryResult
{
	public string QueryBounds { get; set; }
	public int MatchCount { get; set; }
	public OccupiedRegionInfo[] Regions { get; set; }
}

public class TaskStateResult
{
	public int TotalTasks { get; set; }
	public int Completed { get; set; }
	public int InProgress { get; set; }
	public int Pending { get; set; }
	public int PendingExecution { get; set; }
	public int Failed { get; set; }
	public BuilderInfo[] Builders { get; set; }
}

public class BuilderInfo
{
	public string Name { get; set; }
	public string CurrentTaskId { get; set; }
	public int PiecesBuilt { get; set; }
	public bool Active { get; set; }
}
