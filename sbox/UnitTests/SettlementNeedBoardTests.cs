using System;
using System.Linq;
using Lute.Building;
using Lute.Items;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lute.Tests
{
	[TestClass]
	public class SettlementNeedBoardTests
	{
		[TestInitialize]
		public void Setup()
		{
			ConstructionDirector.Reset();
			SettlementNeedBoard.Clear();
		}

		static SettlementNeed Need( SettlementNeedType type = SettlementNeedType.Shelter,
			string structureType = "cottage", int desired = 3, float urgency = 0.5f ) =>
			new SettlementNeed( "need_test" )
			{
				Type = type,
				StructureType = structureType,
				DesiredCount = desired,
				ExistingCount = 0,
				Urgency = urgency,
				Reason = "test",
			};

		[TestMethod]
		public void Clear_EmptiesNeedsAndRequests()
		{
			SettlementNeedBoard.RegisterNeed( Need() );
			SettlementNeedBoard.Clear();
			Assert.AreEqual( 0, SettlementNeedBoard.Needs.Count );
			Assert.AreEqual( 0, SettlementNeedBoard.Requests.Count );
		}

		[TestMethod]
		public void RegisterNeed_AddsNeed()
		{
			SettlementNeedBoard.RegisterNeed( Need() );
			Assert.AreEqual( 1, SettlementNeedBoard.Needs.Count() );
		}

		[TestMethod]
		public void RegisterNeed_DuplicateTypeMerges_UrgencyTakesMax()
		{
			SettlementNeedBoard.RegisterNeed( Need( urgency: 0.4f, desired: 3 ) );
			SettlementNeedBoard.RegisterNeed( Need( urgency: 0.8f, desired: 5 ) );
			var needs = SettlementNeedBoard.Needs.ToList();
			Assert.AreEqual( 1, needs.Count, "duplicate type+structureType should merge" );
			Assert.AreEqual( 0.8f, needs[0].Urgency, 0.001f );
			Assert.AreEqual( 5, needs[0].DesiredCount );
		}

		[TestMethod]
		public void RegisterNeed_AssignsDeterministicId_WhenUnassigned()
		{
			var n = new SettlementNeed( "need_unassigned" )
			{
				Type = SettlementNeedType.Shelter,
				StructureType = "cottage",
				DesiredCount = 1,
				Urgency = 0.5f,
			};
			SettlementNeedBoard.RegisterNeed( n );
			var registered = SettlementNeedBoard.Needs.Single();
			Assert.AreNotEqual( "need_unassigned", registered.Id );
			Assert.IsTrue( registered.Id.StartsWith( "need_" ) );
		}

		[TestMethod]
		public void WithdrawNeed_RemovesById()
		{
			SettlementNeedBoard.RegisterNeed( Need() );
			var id = SettlementNeedBoard.Needs.Single().Id;
			SettlementNeedBoard.WithdrawNeed( id );
			Assert.AreEqual( 0, SettlementNeedBoard.Needs.Count() );
		}

		[TestMethod]
		public void WithdrawNeed_UnknownId_NoOp()
		{
			SettlementNeedBoard.WithdrawNeed( "does_not_exist" );
			Assert.AreEqual( 0, SettlementNeedBoard.Needs.Count() );
		}

		[TestMethod]
		public void PruneFulfilled_RemovesFulfilledNeeds()
		{
			SettlementNeedBoard.RegisterNeed( Need( desired: 2 ) );
			var n = SettlementNeedBoard.Needs.Single();
			n.ExistingCount = 2; // fulfilled
			Assert.IsTrue( n.IsFulfilled );
			SettlementNeedBoard.PruneFulfilled();
			Assert.AreEqual( 0, SettlementNeedBoard.Needs.Count() );
		}

		[TestMethod]
		public void PruneFulfilled_KeepsUnfulfilledNeeds()
		{
			SettlementNeedBoard.RegisterNeed( Need( desired: 5 ) );
			var n = SettlementNeedBoard.Needs.Single();
			n.ExistingCount = 2; // not fulfilled (2 < 5)
			Assert.IsFalse( n.IsFulfilled );
			SettlementNeedBoard.PruneFulfilled();
			Assert.AreEqual( 1, SettlementNeedBoard.Needs.Count() );
		}

		[TestMethod]
		public void IsFulfilled_TrueWhenExistingMeetsOrExceedsDesired()
		{
			var n = Need( desired: 3 );
			Assert.IsFalse( n.IsFulfilled );
			n.ExistingCount = 3;
			Assert.IsTrue( n.IsFulfilled );
			n.ExistingCount = 5;
			Assert.IsTrue( n.IsFulfilled );
		}

		[TestMethod]
		public void PipelineCount_SumsExistingPlannedInProgress()
		{
			var n = Need( desired: 10 );
			n.ExistingCount = 2;
			n.PlannedCount = 3;
			n.InProgressCount = 1;
			Assert.AreEqual( 6, n.PipelineCount );
		}

		[TestMethod]
		public void ReconcileFromDirector_DerivesCountsFromTaskCatalog()
		{
			// Register a need for cottages.
			SettlementNeedBoard.RegisterNeed( Need( structureType: "cottage", desired: 5 ) );
			// Register 2 completed cottages and 1 pending cottage in the Director.
			var c1 = ConstructionDirector.RegisterTask( new VillageBuildTask
			{
				TaskType = "cottage", Name = "c1", Position = new Vector3( 0, 0, 0 ),
			} );
			var c2 = ConstructionDirector.RegisterTask( new VillageBuildTask
			{
				TaskType = "cottage", Name = "c2", Position = new Vector3( 10, 0, 0 ),
			} );
			ConstructionDirector.CompleteTask( c1 );
			ConstructionDirector.CompleteTask( c2 );
			var c3 = ConstructionDirector.RegisterTask( new VillageBuildTask
			{
				TaskType = "cottage", Name = "c3", Position = new Vector3( 20, 0, 0 ),
			} ); // pending

			SettlementNeedBoard.ReconcileFromDirector();
			var n = SettlementNeedBoard.Needs.Single();
			Assert.AreEqual( 2, n.ExistingCount, "two completed cottages" );
			Assert.AreEqual( 1, n.PlannedCount, "one pending cottage" );
			Assert.AreEqual( 0, n.InProgressCount );
		}

		[TestMethod]
		public void ReconcileFromDirector_IgnoresNonMatchingTaskTypes()
		{
			SettlementNeedBoard.RegisterNeed( Need( structureType: "cottage", desired: 5 ) );
			ConstructionDirector.RegisterTask( new VillageBuildTask
			{
				TaskType = "wall", Name = "w1", Position = new Vector3( 0, 0, 0 ),
			} );
			ConstructionDirector.RegisterTask( new VillageBuildTask
			{
				TaskType = "wall", Name = "w2", Position = new Vector3( 10, 0, 0 ),
			} );
			SettlementNeedBoard.ReconcileFromDirector();
			var n = SettlementNeedBoard.Needs.Single();
			Assert.AreEqual( 0, n.ExistingCount );
			Assert.AreEqual( 0, n.PlannedCount );
		}

		[TestMethod]
		public void ReconcileFromDirector_NoTasks_NoOp()
		{
			SettlementNeedBoard.RegisterNeed( Need( desired: 5 ) );
			SettlementNeedBoard.ReconcileFromDirector();
			var n = SettlementNeedBoard.Needs.Single();
			Assert.AreEqual( 0, n.ExistingCount );
		}

		[TestMethod]
		public void ResourcePressureNeed_IsFulfilledWhenNoPressure()
		{
			var n = new SettlementNeed( "need_rp" )
			{
				Type = SettlementNeedType.ResourcePressure,
				StructureType = null,
				DesiredCount = 0,
				Urgency = 0.5f,
				ResourcePressure = new(),
			};
			Assert.IsTrue( n.IsFulfilled );
			n.ResourcePressure[ItemType.Wood] = 5;
			Assert.IsFalse( n.IsFulfilled );
		}
	}
}
