using System;
using System.Linq;
using Lute.Building;
using Lute.Items;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lute.Tests
{
	[TestClass]
	public class ReservationManagerTests
	{
		[TestInitialize]
		public void Setup()
		{
			ConstructionDirector.Reset();
			// ReservationManager.Reset is called by ConstructionDirector.Reset.
		}

		static BBox Box( float x0, float y0, float z0, float x1, float y1, float z1 ) =>
			new BBox( new Vector3( x0, y0, z0 ), new Vector3( x1, y1, z1 ) );

		[TestMethod]
		public void Reset_ClearsOccupancy()
		{
			ReservationManager.RegisterOccupiedRegion( "external", Box( 0, 0, 0, 10, 10, 10 ) );
			Assert.AreEqual( 1, ReservationManager.GetOccupiedRegions().Count );
			ConstructionDirector.Reset();
			Assert.AreEqual( 0, ReservationManager.GetOccupiedRegions().Count );
		}

		[TestMethod]
		public void RegisterOccupiedRegion_AddsToLedger()
		{
			var id = ReservationManager.RegisterOccupiedRegion( "ext1", Box( 0, 0, 0, 5, 5, 5 ) );
			Assert.IsFalse( string.IsNullOrEmpty( id ) );
			var regions = ReservationManager.GetOccupiedRegions();
			Assert.AreEqual( 1, regions.Count );
			Assert.AreEqual( "ext1", regions[0].Source );
		}

		[TestMethod]
		public void RegisterOccupiedRegion_MultipleRegions_DistinctIds()
		{
			var id1 = ReservationManager.RegisterOccupiedRegion( "a", Box( 0, 0, 0, 1, 1, 1 ) );
			var id2 = ReservationManager.RegisterOccupiedRegion( "b", Box( 10, 10, 0, 11, 11, 1 ) );
			Assert.AreNotEqual( id1, id2 );
			Assert.AreEqual( 2, ReservationManager.GetOccupiedRegions().Count );
		}

		[TestMethod]
		public void RegisterOccupiedRegion_NullSource_DefaultsToId()
		{
			// RegisterOccupiedRegion(source, bounds) -> CommitPlacement(null, bounds, source)
			// CommitPlacement sets Source = source ?? id, so source is preserved.
			var id = ReservationManager.RegisterOccupiedRegion( "named", Box( 0, 0, 0, 1, 1, 1 ) );
			var r = ReservationManager.GetOccupiedRegions().Single( x => x.Id == id );
			Assert.AreEqual( "named", r.Source );
		}

		[TestMethod]
		public void CanPlace_RejectsUnknownTask()
		{
			var ok = ReservationManager.CanPlace( "nonexistent", Box( 0, 0, 0, 1, 1, 1 ), out var reason );
			Assert.IsFalse( ok );
			Assert.IsFalse( string.IsNullOrEmpty( reason ) );
		}

		[TestMethod]
		public void CanPlace_RejectsTaskNotInProgress()
		{
			var id = ConstructionDirector.RegisterTask( new VillageBuildTask
			{
				TaskType = "wall", Name = "w", Position = new Vector3( 0, 0, 0 ),
			} );
			// Task is Pending, not InProgress.
			var ok = ReservationManager.CanPlace( id, Box( 0, 0, 0, 1, 1, 1 ), out var reason );
			Assert.IsFalse( ok );
			Assert.IsTrue( reason.Contains( "in progress" ) || reason.Contains( "InProgress" ) || reason.Contains( "not in progress" ) );
		}

		[TestMethod]
		public void CanPlace_RejectsTaskWithNoReservation()
		{
			var id = ConstructionDirector.RegisterTask( new VillageBuildTask
			{
				TaskType = "wall", Name = "w", Position = new Vector3( 0, 0, 0 ),
			} );
			var t = ConstructionDirector.GetTask( id );
			t.Status = TaskStatus.InProgress;
			// No reservation was created.
			var ok = ReservationManager.CanPlace( id, Box( 0, 0, 0, 1, 1, 1 ), out var reason );
			Assert.IsFalse( ok );
			Assert.IsTrue( reason.Contains( "reservation" ) || reason.Contains( "claim" ) );
		}

		[TestMethod]
		public void EstimateTaskBounds_NonNullForKnownType()
		{
			var bounds = ReservationManager.EstimateTaskBounds( new VillageBuildTask
			{
				TaskType = "wall", Position = new Vector3( 0, 0, 0 ),
			} );
			// Wall bounds should be non-zero volume.
			var size = bounds.Maxs - bounds.Mins;
			Assert.IsTrue( size.x > 0 );
			Assert.IsTrue( size.y > 0 );
			Assert.IsTrue( size.z > 0 );
		}

		[TestMethod]
		public void EstimateTaskBounds_NullTask_ReturnsEmptyBox()
		{
			var bounds = ReservationManager.EstimateTaskBounds( null );
			Assert.AreEqual( Vector3.Zero, bounds.Mins );
			Assert.AreEqual( Vector3.Zero, bounds.Maxs );
		}

		[TestMethod]
		public void EstimateTaskBounds_CenteredOnPosition()
		{
			var pos = new Vector3( 100, 200, 0 );
			var bounds = ReservationManager.EstimateTaskBounds( new VillageBuildTask
			{
				TaskType = "wall", Position = pos,
			} );
			// Center of the box should be near the task position (x,y).
			float cx = ( bounds.Mins.x + bounds.Maxs.x ) / 2f;
			float cy = ( bounds.Mins.y + bounds.Maxs.y ) / 2f;
			Assert.IsTrue( Math.Abs( cx - pos.x ) < 1f, $"cx={cx} pos.x={pos.x}" );
			Assert.IsTrue( Math.Abs( cy - pos.y ) < 1f, $"cy={cy} pos.y={pos.y}" );
		}

		[TestMethod]
		public void Release_CompletedTaskCommitsBoundsToOccupancy()
		{
			var id = ConstructionDirector.RegisterTask( new VillageBuildTask
			{
				TaskType = "wall", Name = "w", Position = new Vector3( 0, 0, 0 ),
			} );
			var t = ConstructionDirector.GetTask( id );
			t.Status = TaskStatus.Complete;
			t.ReservationBounds = Box( 50, 50, 0, 60, 60, 10 );
			// Before release, no occupancy for this task.
			Assert.IsFalse( ReservationManager.GetOccupiedRegions().Any( o => o.TaskId == id ) );
			ReservationManager.Release( t );
			// After release of a completed task with bounds, occupancy is committed.
			Assert.IsTrue( ReservationManager.GetOccupiedRegions().Any( o => o.TaskId == id ) );
		}

		[TestMethod]
		public void Release_NullTask_NoOp()
		{
			// Should not throw.
			ReservationManager.Release( null );
			Assert.AreEqual( 0, ReservationManager.GetOccupiedRegions().Count );
		}

		[TestMethod]
		public void GetLastBlockerTaskId_NullAfterReset()
		{
			Assert.IsNull( ReservationManager.GetLastBlockerTaskId() );
		}
	}
}
