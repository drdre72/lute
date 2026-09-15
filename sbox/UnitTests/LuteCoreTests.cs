using System;
using System.Collections.Generic;
using System.Numerics;
using Lute.Core.Building;
using Lute.Core.Spatial;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lute.Tests
{
	[TestClass]
	public class LuteCoreTests
	{
		// ── Aabb3 ──

		[TestMethod]
		public void Aabb3_Size_And_Center()
		{
			var box = new Aabb3( new Vector3( 0, 0, 0 ), new Vector3( 10, 20, 30 ) );
			Assert.AreEqual<Vector3>( new Vector3( 10, 20, 30 ), box.Size );
			Assert.AreEqual<Vector3>( new Vector3( 5, 10, 15 ), box.Center );
		}

		[TestMethod]
		public void Aabb3_Contains_InclusiveBoundary()
		{
			var box = new Aabb3( new Vector3( 0, 0, 0 ), new Vector3( 10, 10, 10 ) );
			Assert.IsTrue( box.Contains( new Vector3( 5, 5, 5 ) ) );
			Assert.IsTrue( box.Contains( new Vector3( 0, 0, 0 ) ) );  // min corner
			Assert.IsTrue( box.Contains( new Vector3( 10, 10, 10 ) ) ); // max corner
			Assert.IsFalse( box.Contains( new Vector3( -1, 5, 5 ) ) );
			Assert.IsFalse( box.Contains( new Vector3( 11, 5, 5 ) ) );
		}

		[TestMethod]
		public void Aabb3_IntersectsVolume_TrueForPenetration()
		{
			var a = new Aabb3( new Vector3( 0, 0, 0 ), new Vector3( 10, 10, 10 ) );
			var b = new Aabb3( new Vector3( 5, 5, 5 ), new Vector3( 15, 15, 15 ) );
			Assert.IsTrue( a.IntersectsVolume( b ) );
		}

		[TestMethod]
		public void Aabb3_IntersectsVolume_FalseForTouching()
		{
			var a = new Aabb3( new Vector3( 0, 0, 0 ), new Vector3( 10, 10, 10 ) );
			var b = new Aabb3( new Vector3( 10, 0, 0 ), new Vector3( 20, 10, 10 ) );
			// Faces touch at x=10 but no volume overlap.
			Assert.IsFalse( a.IntersectsVolume( b ) );
		}

		[TestMethod]
		public void Aabb3_TouchesOrIntersects_TrueForTouching()
		{
			var a = new Aabb3( new Vector3( 0, 0, 0 ), new Vector3( 10, 10, 10 ) );
			var b = new Aabb3( new Vector3( 10, 0, 0 ), new Vector3( 20, 10, 10 ) );
			Assert.IsTrue( a.TouchesOrIntersects( b ) );
		}

		[TestMethod]
		public void Aabb3_TouchesOrIntersects_FalseForSeparated()
		{
			var a = new Aabb3( new Vector3( 0, 0, 0 ), new Vector3( 10, 10, 10 ) );
			var b = new Aabb3( new Vector3( 20, 0, 0 ), new Vector3( 30, 10, 10 ) );
			Assert.IsFalse( a.TouchesOrIntersects( b ) );
		}

		[TestMethod]
		public void Aabb3_IntersectsVolume_ToleranceShrinks()
		{
			var a = new Aabb3( new Vector3( 0, 0, 0 ), new Vector3( 10, 10, 10 ) );
			var b = new Aabb3( new Vector3( 9.5f, 0, 0 ), new Vector3( 19.5f, 10, 10 ) );
			// 0.5 unit overlap on X. With tolerance 1, this is NOT a volume intersection.
			Assert.IsFalse( a.IntersectsVolume( b, 1f ) );
			// With tolerance 0, it IS.
			Assert.IsTrue( a.IntersectsVolume( b, 0f ) );
		}

		[TestMethod]
		public void Aabb3_ExpandedBy_GrowsOnAllSides()
		{
			var box = new Aabb3( new Vector3( 5, 5, 5 ), new Vector3( 10, 10, 10 ) );
			var expanded = box.ExpandedBy( 2f );
			Assert.AreEqual<Vector3>( new Vector3( 3, 3, 3 ), expanded.Min );
			Assert.AreEqual<Vector3>( new Vector3( 12, 12, 12 ), expanded.Max );
		}

		[TestMethod]
		public void Aabb3_TranslatedBy_Shifts()
		{
			var box = new Aabb3( new Vector3( 0, 0, 0 ), new Vector3( 10, 10, 10 ) );
			var moved = box.TranslatedBy( new Vector3( 100, 200, 300 ) );
			Assert.AreEqual<Vector3>( new Vector3( 100, 200, 300 ), moved.Min );
			Assert.AreEqual<Vector3>( new Vector3( 110, 210, 310 ), moved.Max );
		}

		[TestMethod]
		public void Aabb3_Empty_IsZeroVolume()
		{
			Assert.AreEqual<Vector3>( Vector3.Zero, Aabb3.Empty.Min );
			Assert.AreEqual<Vector3>( Vector3.Zero, Aabb3.Empty.Max );
			Assert.AreEqual<Vector3>( Vector3.Zero, Aabb3.Empty.Size );
		}

		[TestMethod]
		public void Aabb3_Equality_IsValueEquality()
		{
			var a = new Aabb3( new Vector3( 1, 2, 3 ), new Vector3( 4, 5, 6 ) );
			var b = new Aabb3( new Vector3( 1, 2, 3 ), new Vector3( 4, 5, 6 ) );
			var c = new Aabb3( new Vector3( 1, 2, 3 ), new Vector3( 4, 5, 7 ) );
			Assert.AreEqual( a, b );
			Assert.AreNotEqual( a, c );
			Assert.IsTrue( a == b );
			Assert.IsFalse( a != b );
		}

		// ── LuteUnits ──

		[TestMethod]
		public void LuteUnits_Meters_RoundTrip()
		{
			float wu = LuteUnits.Meters( 2f );
			Assert.AreEqual( 2f * 39.37f, wu );
			Assert.AreEqual( 2f, LuteUnits.ToMeters( wu ), 0.0001f );
		}

		[TestMethod]
		public void LuteUnits_ConstantIs39_37()
		{
			Assert.AreEqual( 39.37f, LuteUnits.WorldUnitsPerMeter );
		}

		// ── SpatialKey ──

		[TestMethod]
		public void SpatialKey_Quantize_Floors()
		{
			var key = SpatialKey.Quantize( new Vector3( 15.5f, 25.9f, -5f ), 10f );
			Assert.AreEqual<SpatialKey>( new SpatialKey( 1, 2, -1 ), key );
		}

		[TestMethod]
		public void SpatialKey_QuantizeHorizontal_ZIsZero()
		{
			var key = SpatialKey.QuantizeHorizontal( new Vector3( 15f, 25f, 99f ), 10f );
			Assert.AreEqual( 0, key.Z );
			Assert.AreEqual( 1, key.X );
			Assert.AreEqual( 2, key.Y );
		}

		[TestMethod]
		public void SpatialKey_Equality_IsValueEquality()
		{
			var a = new SpatialKey( 1, 2, 3 );
			var b = new SpatialKey( 1, 2, 3 );
			var c = new SpatialKey( 1, 2, 4 );
			Assert.AreEqual( a, b );
			Assert.AreNotEqual( a, c );
		}

		[TestMethod]
		public void SpatialKey_StableHash_AcrossSameValues()
		{
			var a = new SpatialKey( 100, 200, 300 );
			var b = new SpatialKey( 100, 200, 300 );
			Assert.AreEqual( a.GetHashCode(), b.GetHashCode() );
		}

		// ── DirectedTask ──

		[TestMethod]
		public void DirectedTask_Grid8_DeterministicAndZIgnored()
		{
			var g1 = DirectedTask.ComputeGrid8( new Vector3( 100, 200, 0 ) );
			var g2 = DirectedTask.ComputeGrid8( new Vector3( 100, 200, 50 ) );
			Assert.AreEqual( g1, g2 );
			Assert.AreEqual( 8, g1.Length );
		}

		[TestMethod]
		public void DirectedTask_Grid8_NegativeWraps()
		{
			var g = DirectedTask.ComputeGrid8( new Vector3( -10, -10, 0 ) );
			Assert.AreEqual( 8, g.Length );
			// -10/10 = -1; ((-1 % 10000) + 10000) % 10000 = 9999
			Assert.AreEqual( "99999999", g );
		}

		[TestMethod]
		public void DirectedTask_DependenciesSatisfied_TrueWhenAllComplete()
		{
			var t = new DirectedTask { Id = "t1", DependsOn = new() { "a", "b" } };
			var statuses = new Dictionary<string, TaskStatus>
			{
				{ "a", TaskStatus.Complete },
				{ "b", TaskStatus.Complete },
			};
			Assert.IsTrue( t.DependenciesSatisfied( id => statuses.TryGetValue( id, out var s ) ? s : null ) );
		}

		[TestMethod]
		public void DirectedTask_DependenciesSatisfied_FalseWhenAnyIncomplete()
		{
			var t = new DirectedTask { Id = "t1", DependsOn = new() { "a", "b" } };
			var statuses = new Dictionary<string, TaskStatus>
			{
				{ "a", TaskStatus.Complete },
				{ "b", TaskStatus.Pending },
			};
			Assert.IsFalse( t.DependenciesSatisfied( id => statuses.TryGetValue( id, out var s ) ? s : null ) );
		}

		[TestMethod]
		public void DirectedTask_DependenciesSatisfied_TrueWhenNoDependencies()
		{
			var t = new DirectedTask { Id = "t1" };
			Assert.IsTrue( t.DependenciesSatisfied( _ => null ) );
		}

		[TestMethod]
		public void DirectedTask_DependenciesSatisfied_FalseForUnknownDependency()
		{
			var t = new DirectedTask { Id = "t1", DependsOn = new() { "ghost" } };
			Assert.IsFalse( t.DependenciesSatisfied( _ => null ) );
		}

		// ── DependencyGraph ──

		static Dictionary<string, List<string>> Graph( params (string, string[])[] edges )
		{
			var g = new Dictionary<string, List<string>>();
			foreach ( var (node, deps) in edges )
				g[node] = new List<string>( deps );
			return g;
		}

		[TestMethod]
		public void DependencyGraph_NoCycle_OnAcyclicGraph()
		{
			var g = Graph( ("a", new[] { "b" }), ("b", new[] { "c" }), ("c", Array.Empty<string>() ) );
			Assert.IsFalse( DependencyGraph.DetectCycleFrom( "a", id => (g.TryGetValue(id, out var deps) ? deps : new List<string>()), out var path ) );
			Assert.IsNull( path );
		}

		[TestMethod]
		public void DependencyGraph_DetectsCycle_SelfLoop()
		{
			var g = Graph( ("a", new[] { "a" }) );
			Assert.IsTrue( DependencyGraph.DetectCycleFrom( "a", id => (g.TryGetValue(id, out var deps) ? deps : new List<string>()), out var path ) );
			Assert.IsNotNull( path );
			Assert.IsTrue( path.Contains( "a" ) );
		}

		[TestMethod]
		public void DependencyGraph_DetectsCycle_ThreeNode()
		{
			var g = Graph( ("a", new[] { "b" }), ("b", new[] { "c" }), ("c", new[] { "a" }) );
			Assert.IsTrue( DependencyGraph.DetectCycleFrom( "a", id => (g.TryGetValue(id, out var deps) ? deps : new List<string>()), out var path ) );
			Assert.IsNotNull( path );
		}

		[TestMethod]
		public void DependencyGraph_GetBlockingDependency_ReturnsFirstIncomplete()
		{
			var g = Graph( ("t", new[] { "a", "b", "c" }) );
			var statuses = new Dictionary<string, TaskStatus>
			{
				{ "a", TaskStatus.Complete },
				{ "b", TaskStatus.Pending },
				{ "c", TaskStatus.Pending },
			};
			var blocker = DependencyGraph.GetBlockingDependency( "t",
				id => (g.TryGetValue(id, out var deps) ? deps : new List<string>()),
				id => statuses.TryGetValue( id, out var s ) ? s : null );
			Assert.AreEqual( "b", blocker );
		}

		[TestMethod]
		public void DependencyGraph_GetBlockingDependency_NullWhenAllComplete()
		{
			var g = Graph( ("t", new[] { "a", "b" }) );
			var statuses = new Dictionary<string, TaskStatus>
			{
				{ "a", TaskStatus.Complete },
				{ "b", TaskStatus.Complete },
			};
			var blocker = DependencyGraph.GetBlockingDependency( "t",
				id => (g.TryGetValue(id, out var deps) ? deps : new List<string>()),
				id => statuses.TryGetValue( id, out var s ) ? s : null );
			Assert.IsNull( blocker );
		}

		[TestMethod]
		public void DependencyGraph_TopologicalOrder_Acyclic()
		{
			var g = Graph( ("a", new[] { "b" }), ("b", new[] { "c" }), ("c", Array.Empty<string>() ) );
			var order = DependencyGraph.TopologicalOrder( new[] { "a" }, id => (g.TryGetValue(id, out var deps) ? deps : new List<string>()) );
			Assert.IsNotNull( order );
			// c must come before b, b before a (dependencies first).
			Assert.IsTrue( order.IndexOf( "c" ) < order.IndexOf( "b" ) );
			Assert.IsTrue( order.IndexOf( "b" ) < order.IndexOf( "a" ) );
		}

		[TestMethod]
		public void DependencyGraph_TopologicalOrder_NullOnCycle()
		{
			var g = Graph( ("a", new[] { "b" }), ("b", new[] { "a" }) );
			var order = DependencyGraph.TopologicalOrder( new[] { "a" }, id => (g.TryGetValue(id, out var deps) ? deps : new List<string>()) );
			Assert.IsNull( order );
		}

		[TestMethod]
		public void DependencyGraph_TopologicalOrder_HandlesDiamond()
		{
			// a -> b -> d, a -> c -> d
			var g = Graph(
				("a", new[] { "b", "c" }),
				("b", new[] { "d" }),
				("c", new[] { "d" }),
				("d", Array.Empty<string>() )
			);
			var order = DependencyGraph.TopologicalOrder( new[] { "a" }, id => (g.TryGetValue(id, out var deps) ? deps : new List<string>()) );
			Assert.IsNotNull( order );
			Assert.IsTrue( order.IndexOf( "d" ) < order.IndexOf( "b" ) );
			Assert.IsTrue( order.IndexOf( "d" ) < order.IndexOf( "c" ) );
			Assert.IsTrue( order.IndexOf( "b" ) < order.IndexOf( "a" ) );
			Assert.IsTrue( order.IndexOf( "c" ) < order.IndexOf( "a" ) );
		}
	}
}
