using System;
using System.Linq;
using Lute.Building;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lute.Tests
{
	/// <summary>
	/// Tests for <see cref="BlueprintValidator"/>. These exercise the
	/// pure-data validation path (no Log, no Scene, no engine init).
	/// </summary>
	[TestClass]
	public class BlueprintValidatorTests
	{
		static BlueprintPiece Piece( float x, float y, float z, string type = "WALL" )
			=> new() { Position = new Vector3( x, y, z ), PieceType = type };

		static Blueprint ValidRoom()
		{
			// A simple 2x2 room: 4 walls on a floor.
			var bp = new Blueprint { Name = "TestRoom", WallHeight = 200f, FloorThickness = 10f };
			bp.Pieces.Add( Piece( 0, 0, 0, "FLOOR" ) );
			bp.Pieces.Add( Piece( 0, 0, 0, "WALL" ) );
			bp.Pieces.Add( Piece( 100, 0, 0, "WALL" ) );
			bp.Pieces.Add( Piece( 0, 100, 0, "WALL" ) );
			bp.Pieces.Add( Piece( 100, 100, 0, "WALL" ) );
			return bp;
		}

		[TestMethod]
		public void NullBlueprint_IsInvalid()
		{
			var result = BlueprintValidator.Validate( null );
			Assert.IsFalse( result.IsValid );
			Assert.AreEqual( 1, result.ErrorCount );
			Assert.IsTrue( result.TypedIssues.Any( i => i.Code == IssueCode.Nan ) );
		}

		[TestMethod]
		public void EmptyBlueprint_IsValidWithWarning()
		{
			var bp = new Blueprint { Name = "Empty" };
			var result = BlueprintValidator.Validate( bp );
			Assert.IsTrue( result.IsValid );
			Assert.AreEqual( 0, result.ErrorCount );
			Assert.IsTrue( result.WarningCount >= 1 );
		}

		[TestMethod]
		public void ValidRoom_IsValid()
		{
			var result = BlueprintValidator.Validate( ValidRoom() );
			Assert.IsTrue( result.IsValid, $"Expected valid, got: {result.Summary}" );
			Assert.AreEqual( 0, result.ErrorCount );
		}

		[TestMethod]
		public void NaNPosition_IsError()
		{
			var bp = ValidRoom();
			bp.Pieces.Add( Piece( float.NaN, 0, 0 ) );
			var result = BlueprintValidator.Validate( bp );
			Assert.IsFalse( result.IsValid );
			Assert.IsTrue( result.TypedIssues.Any( i => i.Code == IssueCode.Nan ) );
		}

		[TestMethod]
		public void InfinityPosition_IsError()
		{
			var bp = ValidRoom();
			bp.Pieces.Add( Piece( float.PositiveInfinity, 0, 0 ) );
			var result = BlueprintValidator.Validate( bp );
			Assert.IsFalse( result.IsValid );
			Assert.IsTrue( result.TypedIssues.Any( i => i.Code == IssueCode.Nan ) );
		}

		[TestMethod]
		public void PieceTooFarFromOrigin_IsError()
		{
			var bp = ValidRoom();
			bp.Pieces.Add( Piece( 60000, 0, 0 ) ); // > 50000in max
			var result = BlueprintValidator.Validate( bp );
			Assert.IsFalse( result.IsValid );
			Assert.IsTrue( result.TypedIssues.Any( i => i.Code == IssueCode.OutOfBounds ) );
		}

		[TestMethod]
		public void WallsWithoutFloor_IsError()
		{
			var bp = new Blueprint { Name = "FloatingWalls", WallHeight = 200f, FloorThickness = 10f };
			bp.Pieces.Add( Piece( 0, 0, 0, "WALL" ) );
			bp.Pieces.Add( Piece( 100, 0, 0, "WALL" ) );
			var result = BlueprintValidator.Validate( bp );
			Assert.IsFalse( result.IsValid );
			Assert.IsTrue( result.TypedIssues.Any( i => i.Code == IssueCode.MissingFloor ) );
		}

		[TestMethod]
		public void DuplicatePieces_AreOverlapWarning()
		{
			var bp = ValidRoom();
			bp.Pieces.Add( Piece( 0, 0, 0, "WALL" ) ); // exact dup of existing wall
			var result = BlueprintValidator.Validate( bp );
			// Warnings don't block validity
			Assert.IsTrue( result.IsValid );
			Assert.IsTrue( result.WarningCount >= 1 );
			Assert.IsTrue( result.TypedIssues.Any( i => i.Code == IssueCode.Overlap ) );
		}

		[TestMethod]
		public void EmptyPieceType_IsError()
		{
			var bp = ValidRoom();
			bp.Pieces.Add( Piece( 50, 50, 0, "" ) );
			var result = BlueprintValidator.Validate( bp );
			Assert.IsFalse( result.IsValid );
			Assert.IsTrue( result.TypedIssues.Any( i => i.Code == IssueCode.EmptyType ) );
		}

		[TestMethod]
		public void OversizedFootprint_IsError()
		{
			var bp = ValidRoom();
			bp.Pieces.Add( Piece( 21000, 0, 0, "FLOOR" ) );
			bp.Pieces.Add( Piece( 21000, 0, 0, "WALL" ) );
			var result = BlueprintValidator.Validate( bp );
			Assert.IsFalse( result.IsValid );
			Assert.IsTrue( result.TypedIssues.Any( i => i.Code == IssueCode.FootprintExceeded ) );
		}

		[TestMethod]
		public void BadWallHeight_IsWarning()
		{
			var bp = ValidRoom();
			bp.WallHeight = 5f; // < 50
			var result = BlueprintValidator.Validate( bp );
			Assert.IsTrue( result.TypedIssues.Any( i => i.Code == IssueCode.BadWallHeight ) );
		}

		[TestMethod]
		public void BadFloorThickness_IsWarning()
		{
			var bp = ValidRoom();
			bp.FloorThickness = 200f; // > 100
			var result = BlueprintValidator.Validate( bp );
			Assert.IsTrue( result.TypedIssues.Any( i => i.Code == IssueCode.BadFloorThickness ) );
		}

		[TestMethod]
		public void BelowGroundNonFloor_IsWarning()
		{
			var bp = ValidRoom();
			// z < -FloorThickness*2 = -20, and not a FLOOR
			bp.Pieces.Add( Piece( 50, 50, -100, "WALL" ) );
			var result = BlueprintValidator.Validate( bp );
			Assert.IsTrue( result.TypedIssues.Any( i => i.Code == IssueCode.BelowGround ) );
		}

		[TestMethod]
		public void TypedIssues_ArePopulated()
		{
			var bp = new Blueprint { Name = "Bad", WallHeight = 200f, FloorThickness = 10f };
			bp.Pieces.Add( Piece( float.NaN, 0, 0, "WALL" ) );
			var result = BlueprintValidator.Validate( bp );
			Assert.IsTrue( result.TypedIssues.Count >= 1 );
			var issue = result.TypedIssues[0];
			Assert.AreEqual( IssueSeverity.Error, issue.Severity );
			Assert.IsFalse( string.IsNullOrEmpty( issue.Code ) );
			Assert.IsFalse( string.IsNullOrEmpty( issue.Message ) );
		}
	}
}
