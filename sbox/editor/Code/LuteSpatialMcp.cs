using Lute.Building;

namespace Editor.Mcp;

/// <summary>
/// MCP wrapper for Lute spatial query tools. This file lives in the
/// sbox/editor folder, which creates an editor project that can access
/// both the tools APIs (McpTool attributes) and the game code
/// (LuteSpatialApi). No reflection needed.
/// </summary>
[McpToolset( "lute_spatial", "Lute construction spatial queries - occupancy grid, placement validation, task state" )]
public static class LuteSpatialMcp
{
	/// <summary>
	/// List all occupied construction regions with their bounds and source task.
	/// </summary>
	[McpTool.ReadOnly( "lute_get_occupied_regions" )]
	public static OccupiedRegionsResult GetOccupiedRegions()
	{
		return LuteSpatialApi.GetOccupiedRegions();
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
		return LuteSpatialApi.CheckPlacement( x, y, z, rotation, taskType, width, depth, height );
	}

	/// <summary>
	/// Find all occupied regions that overlap a given world-space bounding box.
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
		return LuteSpatialApi.QueryRegion( minX, minY, minZ, maxX, maxY, maxZ );
	}

	/// <summary>
	/// Get the current construction task state — total tasks, completed, in progress,
	/// pending, and per-builder assignments.
	/// </summary>
	[McpTool.ReadOnly( "lute_get_task_state" )]
	public static TaskStateResult GetTaskState()
	{
		return LuteSpatialApi.GetTaskState();
	}
}
