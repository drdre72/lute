using System;
using System.Collections.Generic;
using Sandbox;

namespace Lute.Building
{
	/// <summary>
	/// Type of structural junction between two assemblies.
	/// Each type has a dedicated resolver that produces a placement plan.
	/// </summary>
	public enum JunctionType
	{
		/// <summary> Two perpendicular walls meeting at a corner. </summary>
		WallCorner,
		/// <summary> Wall meeting a gate structure. </summary>
		WallGate,
		/// <summary> Wall meeting a doorway opening. </summary>
		WallDoorway,
		/// <summary> Wall meeting a floor slab. </summary>
		WallFloor,
		/// <summary> Wall meeting a roof piece. </summary>
		WallRoof,
		/// <summary> Wall meeting a tower or pillar. </summary>
		WallTower,
		/// <summary> Foundation meeting terrain. </summary>
		FoundationTerrain,
		/// <summary> Two walls meeting in a T-junction (not a corner). </summary>
		WallTJunction,
		/// <summary> Two walls meeting end-to-end (straight continuation). </summary>
		WallSplice,
	}

	/// <summary>
	/// Authoritative plan for a structural junction. All junction types
	/// (corners, gates, doorways, floor/wall, roof/wall) produce this
	/// same plan structure, so the builder consumes them uniformly.
	///
	/// A JunctionPlan contains:
	///   - The junction type and ID
	///   - The two participating assemblies (by name)
	///   - The junction point in world space
	///   - The placement list (StructuralPlacement objects)
	///   - The bond/overlap depth achieved
	/// </summary>
	public sealed class JunctionPlan
	{
		public string JunctionId { get; init; }
		public JunctionType Type { get; init; }
		public string AssemblyA { get; init; }
		public string AssemblyB { get; init; }
		public Vector3 Junction { get; init; }
		public int Course { get; init; }
		public IReadOnlyList<StructuralPlacement> Placements { get; init; }
		/// <summary> Bond/overlap depth in meters (how far the junction ties the two assemblies together). </summary>
		public float BondDepth { get; init; }
		/// <summary> True if the junction is structurally bonded (bond depth >= required minimum). </summary>
		public bool IsBonded { get; init; }

		public override string ToString()
			=> $"{Type} [{JunctionId}] {AssemblyA}<->{AssemblyB} course={Course} "
				+ $"placements={Placements?.Count ?? 0} bondDepth={BondDepth:F3}m bonded={IsBonded}";
	}

	/// <summary>
	/// Interface for junction resolvers. Each junction type (corner, gate,
	/// doorway, floor, roof, tower, foundation) implements this interface.
	/// The JunctionResolverRegistry dispatches to the correct resolver
	/// based on JunctionType.
	/// </summary>
	public interface IJunctionResolver
	{
		/// <summary> The junction type this resolver handles. </summary>
		JunctionType Type { get; }

		/// <summary>
		/// Resolve a junction into a placement plan for one course.
		/// Returns null if the junction cannot be resolved (e.g. the two
		/// assemblies don't actually meet).
		/// </summary>
		JunctionPlan Resolve(
			VillageBuildTask assemblyA,
			VillageBuildTask assemblyB,
			int course,
			string junctionId = null );
	}

	/// <summary>
	/// Registry of junction resolvers. The builder asks
	/// JunctionResolverRegistry.Resolve(type, a, b, course) and gets
	/// back a JunctionPlan, regardless of which resolver handles it.
	///
	/// This is the generalization of the corner-specific
	/// CornerBondResolver.TryResolveCorner path. New junction types
	/// are added by implementing IJunctionResolver and registering it.
	/// </summary>
	public static class JunctionResolverRegistry
	{
		static readonly Dictionary<JunctionType, IJunctionResolver> _resolvers = new();

		/// <summary>
		/// Register a junction resolver. Called at startup.
		/// </summary>
		public static void Register( IJunctionResolver resolver )
		{
			if ( resolver is null ) return;
			_resolvers[resolver.Type] = resolver;
		}

		/// <summary>
		/// Resolve a junction. Returns null if no resolver is registered
		/// for the type, or if the resolver returns null (assemblies don't meet).
		/// </summary>
		public static JunctionPlan Resolve(
			JunctionType type,
			VillageBuildTask assemblyA,
			VillageBuildTask assemblyB,
			int course,
			string junctionId = null )
		{
			if ( !_resolvers.TryGetValue( type, out var resolver ) )
			{
				Log.Warning( $"Lute: JunctionResolverRegistry — no resolver for {type}" );
				return null;
			}
			return resolver.Resolve( assemblyA, assemblyB, course, junctionId );
		}

		/// <summary>
		/// Get all registered junction types.
		/// </summary>
		public static IEnumerable<JunctionType> RegisteredTypes => _resolvers.Keys;

		/// <summary>
		/// Clear all resolvers (e.g. on scene reset).
		/// </summary>
		public static void Reset() => _resolvers.Clear();
	}

	/// <summary>
	/// Junction resolver for wall↔wall corners. This wraps the existing
	/// CornerBondResolver, adapting its output to the JunctionPlan format.
	/// This is the first concrete IJunctionResolver implementation.
	/// </summary>
	public sealed class WallCornerJunctionResolver : IJunctionResolver
	{
		public JunctionType Type => JunctionType.WallCorner;

		public JunctionPlan Resolve(
			VillageBuildTask assemblyA,
			VillageBuildTask assemblyB,
			int course,
			string junctionId = null )
		{
			var cornerPlan = CornerBondResolver.TryResolveCorner(
				assemblyA, assemblyB, course, junctionId );

			if ( cornerPlan is null ) return null;

			// Convert CornerBrickPlacements to StructuralPlacements
			var placements = new List<StructuralPlacement>();
			float z = 0f; // Z is set by the builder at placement time
			for ( int i = 0; i < cornerPlan.Placements.Count; i++ )
			{
				var p = cornerPlan.Placements[i];
				var sp = StructuralPlacement.FromCornerBrick(
					p, z, assemblyA.Name, i );
				placements.Add( sp );
			}

			junctionId ??= cornerPlan.CornerId;
			float bondDepth = 2f * 0.25f; // 2-module assembly = 0.5m bond depth

			return new JunctionPlan
			{
				JunctionId = junctionId,
				Type = JunctionType.WallCorner,
				AssemblyA = assemblyA.Name,
				AssemblyB = assemblyB.Name,
				Junction = cornerPlan.Junction,
				Course = course,
				Placements = placements,
				BondDepth = bondDepth,
				IsBonded = bondDepth >= 0.25f,
			};
		}
	}

	/// <summary>
	/// Static initializer — registers the built-in junction resolvers.
	/// Call JunctionResolverInit.Initialize() once at startup (e.g. from
	/// LuteGame.OnStart) before any junction resolution is needed.
	/// </summary>
	public static class JunctionResolverInit
	{
		static bool _initialized;

		public static void Initialize()
		{
			if ( _initialized ) return;
			_initialized = true;

			JunctionResolverRegistry.Reset();
			JunctionResolverRegistry.Register( new WallCornerJunctionResolver() );
			// Future: WallGateJunctionResolver, WallDoorwayJunctionResolver,
			// WallFloorJunctionResolver, WallRoofJunctionResolver,
			// WallTowerJunctionResolver, FoundationTerrainJunctionResolver,
			// WallTJunctionResolver, WallSpliceJunctionResolver

			Log.Info( $"Lute: JunctionResolverRegistry initialized — "
				+ $"{string.Join( ", ", JunctionResolverRegistry.RegisteredTypes )}" );
		}

		public static void Reset()
		{
			_initialized = false;
			JunctionResolverRegistry.Reset();
		}

		/// <summary>
		/// Console command: list registered junction resolvers.
		/// Usage: junction_resolvers
		/// </summary>
		[ConCmd( "junction_resolvers" )]
		public static void ListResolversCommand()
		{
			Log.Info( $"Lute: JunctionResolverRegistry — {string.Join( ", ", JunctionResolverRegistry.RegisteredTypes )}" );
		}

		/// <summary>
		/// Console command: test-resolve a wall corner junction.
		/// Usage: junction_test Wall_S_0 Wall_E_0 0
		/// </summary>
		[ConCmd( "junction_test" )]
		public static void TestResolveCommand( string wallAName = "", string wallBName = "", int course = 0 )
		{
			if ( string.IsNullOrEmpty( wallAName ) || string.IsNullOrEmpty( wallBName ) )
			{
				Log.Warning( "Lute: junction_test — usage: junction_test <wallAName> <wallBName> [course]" );
				return;
			}

			// Find the village builder to get wall tasks
			var scene = Game.ActiveScene;
			if ( scene is null )
			{
				Log.Warning( "Lute: junction_test — no active scene." );
				return;
			}
			var builder = scene.GetAllComponents<VillageBuilder>().FirstOrDefault();
			if ( builder is null )
			{
				Log.Warning( "Lute: junction_test — no VillageBuilder found in scene." );
				return;
			}

			var taskA = builder.Tasks?.FirstOrDefault( t => t.Name == wallAName );
			var taskB = builder.Tasks?.FirstOrDefault( t => t.Name == wallBName );
			if ( taskA is null || taskB is null )
			{
				Log.Warning( $"Lute: junction_test — wall task not found: {wallAName}={taskA != null} {wallBName}={taskB != null}" );
				return;
			}

			var plan = JunctionResolverRegistry.Resolve(
				JunctionType.WallCorner, taskA, taskB, course );

			if ( plan is null )
			{
				Log.Warning( $"Lute: junction_test — resolve returned null for {wallAName}<->{wallBName} course {course}" );
				return;
			}

			Log.Info( $"Lute: junction_test — {plan}" );
			foreach ( var p in plan.Placements )
				Log.Info( $"  {p}" );
		}
	}
}
