using System.Collections.Generic;

namespace Lute.Core.Building;

/// <summary>
/// Pure dependency-graph operations: cycle detection, topological
/// feasibility, and blocking-dependency resolution. Extracted from
/// <c>ConstructionDirector</c> so the graph algorithms can be tested
/// headlessly and reused by the future <c>BuildPlan</c> compiler.
///
/// The graph is keyed by task id. Each task's dependencies are
/// supplied via a resolver so this class owns no task storage.
/// </summary>
public static class DependencyGraph
{
	/// <summary>
	/// DFS cycle detection starting from <paramref name="taskId"/>.
	/// Returns true and a human-readable cycle path
	/// ("task_0 -> task_1 -> task_0") if a cycle exists.
	/// </summary>
	public static bool DetectCycleFrom(
		string taskId,
		System.Func<string, IEnumerable<string>> getDependencies,
		out string? cyclePath )
	{
		cyclePath = null;
		var onStack = new HashSet<string>();
		var visited = new HashSet<string>();
		var path = new List<string>();
		string? foundCycle = null;

		bool Dfs( string current )
		{
			if ( onStack.Contains( current ) )
			{
				int start = path.IndexOf( current );
				foundCycle = string.Join( " -> ",
					path.GetRange( start, path.Count - start ) ) + " -> " + current;
				return true;
			}
			if ( visited.Contains( current ) )
				return false;
			visited.Add( current );
			onStack.Add( current );
			path.Add( current );
			foreach ( var dep in getDependencies( current ) )
				if ( Dfs( dep ) ) return true;
			path.RemoveAt( path.Count - 1 );
			onStack.Remove( current );
			return false;
		}

		if ( Dfs( taskId ) )
		{
			cyclePath = foundCycle;
			return true;
		}
		return false;
	}

	/// <summary>
	/// Find the first dependency of <paramref name="taskId"/> that is
	/// not <see cref="TaskStatus.Complete"/>. Returns null if all
	/// dependencies are satisfied.
	/// </summary>
	public static string? GetBlockingDependency(
		string taskId,
		System.Func<string, IEnumerable<string>> getDependencies,
		System.Func<string, TaskStatus?> resolveDependency )
	{
		foreach ( var dep in getDependencies( taskId ) )
		{
			var status = resolveDependency( dep );
			if ( status != TaskStatus.Complete )
				return dep;
		}
		return null;
	}

	/// <summary>
	/// Topological order of all tasks reachable from the given roots.
	/// Returns null if a cycle is detected. Stable: ties broken by
	/// insertion order (the order dependencies are enumerated).
	/// </summary>
	public static List<string>? TopologicalOrder(
		IEnumerable<string> roots,
		System.Func<string, IEnumerable<string>> getDependencies )
	{
		var visited = new HashSet<string>();
		var onStack = new HashSet<string>();
		var result = new List<string>();

		bool Dfs( string current )
		{
			if ( onStack.Contains( current ) )
				return false; // cycle
			if ( visited.Contains( current ) )
				return true;
			visited.Add( current );
			onStack.Add( current );
			foreach ( var dep in getDependencies( current ) )
				if ( !Dfs( dep ) ) return false;
			onStack.Remove( current );
			result.Add( current );
			return true;
		}

		foreach ( var root in roots )
			if ( !Dfs( root ) )
				return null;
		return result;
	}
}
