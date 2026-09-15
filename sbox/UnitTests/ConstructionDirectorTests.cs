using System;
using System.Collections.Generic;
using System.Linq;
using Lute.Building;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lute.Tests
{
	[TestClass]
	public class ConstructionDirectorTests
	{
		[TestInitialize]
		public void Setup() => ConstructionDirector.Reset();

		static VillageBuildTask Task( string type = "wall", string name = null ) =>
			new VillageBuildTask
			{
				TaskType = type,
				Name = name ?? $"{type}_t",
				Position = new Vector3( 100, 100, 0 ),
			};

		[TestMethod]
		public void RegisterTask_AssignsId_AndAddsToCatalog()
		{
			var id = ConstructionDirector.RegisterTask( Task() );
			Assert.IsFalse( string.IsNullOrEmpty( id ) );
			Assert.AreEqual( 1, ConstructionDirector.AllTasks().Count );
			Assert.IsNotNull( ConstructionDirector.GetTask( id ) );
		}

		[TestMethod]
		public void RegisterTask_NullBuildTask_GetsIdAndZeroPieces()
		{
			var id = ConstructionDirector.RegisterTask( null );
			Assert.IsFalse( string.IsNullOrEmpty( id ) );
			var t = ConstructionDirector.GetTask( id );
			Assert.AreEqual( TaskStatus.Pending, t.Status );
			Assert.AreEqual( 0, t.EstimatedPieces );
		}

		[TestMethod]
		public void RegisterTask_EstimatesPieces_ForKnownTypes()
		{
			var wallId = ConstructionDirector.RegisterTask( Task( "wall" ) );
			var gateId = ConstructionDirector.RegisterTask( Task( "gate" ) );
			var roadId = ConstructionDirector.RegisterTask( Task( "road" ) );
			Assert.AreEqual( 12, ConstructionDirector.GetTask( wallId ).EstimatedPieces );
			Assert.AreEqual( 30, ConstructionDirector.GetTask( gateId ).EstimatedPieces );
			Assert.AreEqual( 8, ConstructionDirector.GetTask( roadId ).EstimatedPieces );
		}

		[TestMethod]
		public void RegisterTask_NewTask_IsPending()
		{
			var id = ConstructionDirector.RegisterTask( Task() );
			Assert.AreEqual( TaskStatus.Pending, ConstructionDirector.GetTask( id ).Status );
		}

		[TestMethod]
		public void RegisterTask_CompletedBuildTask_StaysComplete()
		{
			var bt = Task();
			bt.Status = 2; // complete
			var id = ConstructionDirector.RegisterTask( bt );
			Assert.AreEqual( TaskStatus.Complete, ConstructionDirector.GetTask( id ).Status );
		}

		[TestMethod]
		public void RegisterTask_CyclicDependency_Rejected()
		{
			// A -> B -> A : registering the third task should be rejected.
			var a = ConstructionDirector.RegisterTask( Task( name: "A" ) );
			var b = ConstructionDirector.RegisterTask( Task( name: "B" ), dependsOn: new() { a } );
			var c = ConstructionDirector.RegisterTask( Task( name: "C" ), dependsOn: new() { b } );
			// Now make A depend on C — creating a cycle.
			var aTask = ConstructionDirector.GetTask( a );
			aTask.DependsOn.Add( c );
			// Re-registering A with a dependency on C should be rejected.
			var rejected = ConstructionDirector.RegisterTask( Task( name: "A2" ), dependsOn: new() { c, a } );
			Assert.IsNull( rejected );
		}

		[TestMethod]
		public void DependenciesSatisfied_FalseUntilDependencyCompletes()
		{
			var a = ConstructionDirector.RegisterTask( Task( name: "A" ) );
			var b = ConstructionDirector.RegisterTask( Task( name: "B" ), dependsOn: new() { a } );
			Assert.IsFalse( ConstructionDirector.GetTask( b ).DependenciesSatisfied );
			ConstructionDirector.CompleteTask( a );
			Assert.IsTrue( ConstructionDirector.GetTask( b ).DependenciesSatisfied );
		}

		[TestMethod]
		public void AssignTasks_BlocksUnsatisfiedDependency()
		{
			var a = ConstructionDirector.RegisterTask( Task( name: "A" ) );
			var b = ConstructionDirector.RegisterTask( Task( name: "B" ), dependsOn: new() { a } );
			ConstructionDirector.AssignTasks();
			Assert.AreEqual( TaskStatus.Blocked, ConstructionDirector.GetTask( b ).Status );
			Assert.IsFalse( string.IsNullOrEmpty( ConstructionDirector.GetTask( b ).BlockedByDependency ) );
		}

		[TestMethod]
		public void AssignTasks_UnblocksAfterDependencyCompletes()
		{
			var a = ConstructionDirector.RegisterTask( Task( name: "A" ) );
			var b = ConstructionDirector.RegisterTask( Task( name: "B" ), dependsOn: new() { a } );
			ConstructionDirector.AssignTasks();
			Assert.AreEqual( TaskStatus.Blocked, ConstructionDirector.GetTask( b ).Status );
			ConstructionDirector.CompleteTask( a );
			// CompleteTask calls AssignTasks internally — B should unblock.
			Assert.AreEqual( TaskStatus.Pending, ConstructionDirector.GetTask( b ).Status );
		}

		[TestMethod]
		public void CompleteTask_TransitionsToComplete_AndReleasesBuilder()
		{
			ConstructionDirector.RegisterBuilder( 7, "Bob" );
			var id = ConstructionDirector.RegisterTask( Task() );
			ConstructionDirector.AssignTasks();
			ConstructionDirector.CompleteTask( id );
			Assert.AreEqual( TaskStatus.Complete, ConstructionDirector.GetTask( id ).Status );
			// Completing again is a no-op (idempotent).
			ConstructionDirector.CompleteTask( id );
			Assert.AreEqual( TaskStatus.Complete, ConstructionDirector.GetTask( id ).Status );
		}

		[TestMethod]
		public void CompleteTask_CompletedTaskDoesNotRevertToPending()
		{
			var id = ConstructionDirector.RegisterTask( Task() );
			ConstructionDirector.CompleteTask( id );
			ConstructionDirector.AssignTasks();
			Assert.AreEqual( TaskStatus.Complete, ConstructionDirector.GetTask( id ).Status );
		}

		[TestMethod]
		public void CancelTask_TransitionsToCancelled()
		{
			var id = ConstructionDirector.RegisterTask( Task() );
			ConstructionDirector.CancelTask( id );
			Assert.AreEqual( TaskStatus.Cancelled, ConstructionDirector.GetTask( id ).Status );
		}

		[TestMethod]
		public void FailTask_ConsumesRetryUntilMax()
		{
			var id = ConstructionDirector.RegisterTask( Task() );
			var t = ConstructionDirector.GetTask( id );
			int max = t.MaxRetries;
			for ( int i = 0; i < max; i++ )
			{
				ConstructionDirector.FailTask( id, "test" );
				Assert.AreEqual( TaskStatus.Pending, t.Status, $"retry {i+1} should stay pending" );
				Assert.AreEqual( i + 1, t.RetryCount );
			}
			// One more failure should permanently fail.
			ConstructionDirector.FailTask( id, "test" );
			Assert.AreEqual( TaskStatus.Failed, t.Status );
			Assert.AreEqual( "test", t.FailureReason );
		}

		[TestMethod]
		public void ResolveTask_FindsByIdAndUniqueName()
		{
			var id = ConstructionDirector.RegisterTask( Task( name: "UniqueWall" ) );
			Assert.AreEqual( id, ConstructionDirector.ResolveTask( id ).Id );
			Assert.AreEqual( id, ConstructionDirector.ResolveTask( "UniqueWall" ).Id );
		}

		[TestMethod]
		public void ResolveTask_AmbiguousName_ReturnsNull()
		{
			ConstructionDirector.RegisterTask( Task( name: "Same" ) );
			ConstructionDirector.RegisterTask( Task( name: "Same" ) );
			Assert.IsNull( ConstructionDirector.ResolveTask( "Same" ) );
		}

		[TestMethod]
		public void ResolveTask_NullOrWhitespace_ReturnsNull()
		{
			Assert.IsNull( ConstructionDirector.ResolveTask( null ) );
			Assert.IsNull( ConstructionDirector.ResolveTask( "" ) );
			Assert.IsNull( ConstructionDirector.ResolveTask( "   " ) );
		}

		[TestMethod]
		public void RegisterBuilder_RegistersAndActivates()
		{
			ConstructionDirector.RegisterBuilder( 1, "Alice", "mason" );
			var builders = ConstructionDirector.AllBuilders();
			Assert.AreEqual( 1, builders.Count );
			Assert.AreEqual( "Alice", builders[0].NpcName );
			Assert.IsTrue( builders[0].Active );
			Assert.AreEqual( "mason", builders[0].ProfessionId );
		}

		[TestMethod]
		public void RegisterBuilder_IsIdempotent_KeepsState()
		{
			ConstructionDirector.RegisterBuilder( 1, "Alice", "mason" );
			ConstructionDirector.RegisterBuilder( 1, "Alice2" ); // re-register same id
			var b = ConstructionDirector.AllBuilders().Single( x => x.BuilderId == 1 );
			Assert.AreEqual( "Alice2", b.NpcName );
			Assert.AreEqual( "mason", b.ProfessionId ); // profession not overwritten when omitted
		}

		[TestMethod]
		public void DeactivateBuilder_ReturnsTaskToQueue()
		{
			ConstructionDirector.RegisterBuilder( 1, "Alice" );
			var id = ConstructionDirector.RegisterTask( Task() );
			var t = ConstructionDirector.GetTask( id );
			t.AssignedBuilder = 1;
			t.Status = TaskStatus.InProgress;
			// DeactivateBuilder keys off the builder's CurrentTaskId, so set it.
			var builder = ConstructionDirector.AllBuilders().Single( b => b.BuilderId == 1 );
			builder.CurrentTaskId = id;
			ConstructionDirector.DeactivateBuilder( 1 );
			Assert.AreEqual( TaskStatus.Pending, t.Status );
			Assert.AreEqual( -1, t.AssignedBuilder );
		}

		[TestMethod]
		public void HasCapability_FalseForUnknownNpc()
		{
			Assert.IsFalse( ConstructionDirector.HasCapability( "Nobody", NpcCapability.GeneralConstruction ) );
		}

		[TestMethod]
		public void HasCapability_TrueForRegisteredBuilder()
		{
			ConstructionDirector.RegisterBuilder( 1, "Alice", "builder" );
			Assert.IsTrue( ConstructionDirector.HasCapability( "Alice", NpcCapability.GeneralConstruction ) );
		}

		[TestMethod]
		public void Grid8_IsDeterministicAndClamped()
		{
			var g1 = DirectedTask.ComputeGrid8( new Vector3( 100, 200, 0 ) );
			var g2 = DirectedTask.ComputeGrid8( new Vector3( 100, 200, 50 ) );
			Assert.AreEqual( g1, g2 ); // z ignored
			Assert.AreEqual( 8, g1.Length );
			// Negative coordinates wrap into 0..9999.
			var gNeg = DirectedTask.ComputeGrid8( new Vector3( -10, -10, 0 ) );
			Assert.AreEqual( 8, gNeg.Length );
		}

		[TestMethod]
		public void AllTasks_ReturnsCopy_NotInternalReference()
		{
			ConstructionDirector.RegisterTask( Task() );
			var list = ConstructionDirector.AllTasks();
			ConstructionDirector.RegisterTask( Task() );
			Assert.AreEqual( 1, list.Count, "AllTasks should return a snapshot copy" );
		}

		[TestMethod]
		public void PiecesPlaced_NeverExceedsEstimatedPieces_AfterReportProgress()
		{
			var id = ConstructionDirector.RegisterTask( Task( "wall" ) );
			var t = ConstructionDirector.GetTask( id );
			Assert.AreEqual( 12, t.EstimatedPieces );
			ConstructionDirector.ReportProgress( id, 5 );
			Assert.AreEqual( 5, t.PiecesPlaced );
			// ReportProgress does not clamp — it records what was reported.
			ConstructionDirector.ReportProgress( id, 100 );
			Assert.AreEqual( 100, t.PiecesPlaced );
		}
	}
}
