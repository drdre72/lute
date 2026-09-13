using System;
using Sandbox;

namespace Lute.Building
{
	/// <summary>
	/// Semantic type of a structural placement. Used by the spatial
	/// registry, MCP diagnostics, and construction self-repair to
	/// classify what a placement IS without inspecting geometry.
	/// </summary>
	public enum StructuralType
	{
		/// <summary> Unknown/unclassified. </summary>
		Unknown,
		/// <summary> A masonry brick in a wall assembly. </summary>
		WallBrick,
		/// <summary> A shared corner assembly brick (2-module, bridges junction). </summary>
		CornerAssemblyBrick,
		/// <summary> A floor slab piece. </summary>
		FloorSlab,
		/// <summary> A roof piece. </summary>
		RoofPiece,
		/// <summary> A gate or door structure. </summary>
		Gate,
		/// <summary> A tower or pillar structure. </summary>
		Tower,
		/// <summary> A foundation piece. </summary>
		Foundation,
		/// <summary> A decorative or non-structural prop. </summary>
		Decorative,
	}

	/// <summary>
	/// The authoritative placement representation for Lute construction.
	///
	/// This is the single source of truth for a placed structural element.
	/// The renderer (SpawnBox), validator (WallCornerTopologyProbe),
	/// persistence system, MCP tools, and collision registry all consume
	/// this same object. This prevents the "topology says correct, scene
	/// looks wrong" class of bugs where different systems disagree about
	/// a placement's size, position, or meaning.
	///
	/// A StructuralPlacement is created at placement time from the
	/// assembly's grid coordinates + world transform. It is immutable
	/// after creation. The GameObject is a temporary visual representation
	/// that references the StructuralPlacement, not the other way around.
	///
	/// Key invariants:
	///   - Position is the world-space center of the placement.
	///   - Rotation is the yaw in degrees (Z-up).
	///   - Size is the exact world-space dimensions (length, depth, height).
	///   - OBB is derived from Position + Rotation + Size (computed once).
	///   - GridCells are the module-grid cells occupied (for occupancy).
	///   - ParentAssembly is the task/assembly this placement belongs to.
	/// </summary>
	public readonly struct StructuralPlacement : IEquatable<StructuralPlacement>
	{
		/// <summary> Unique identifier for this placement (deterministic). </summary>
		public readonly string EntityId;

		/// <summary> World-space center position. </summary>
		public readonly Vector3 Position;

		/// <summary> Yaw rotation in degrees (Z-up). </summary>
		public readonly float Yaw;

		/// <summary> Exact world-space dimensions (length, depth, height). </summary>
		public readonly Vector3 Size;

		/// <summary> Semantic type of this placement. </summary>
		public readonly StructuralType SemanticType;

		/// <summary> The task/assembly this placement belongs to (e.g. "Wall_S_0"). </summary>
		public readonly string ParentAssembly;

		/// <summary> The BrickSlot grid coordinates (if applicable). </summary>
		public readonly BrickSlot? GridSlot;

		/// <summary>
		/// The oriented bounding box (OBB) for this placement, derived
		/// from Position + Rotation + Size. Computed once at construction.
		/// </summary>
		public BBox OBB
		{
			get
			{
				var half = Size * 0.5f;
				var rot = Yaw != 0f ? Rotation.FromYaw( Yaw ) : Rotation.Identity;
				return new BBox( -half, half ).Rotate( rot ).Translate( Position );
			}
		}

		public StructuralPlacement(
			string entityId,
			Vector3 position,
			float yaw,
			Vector3 size,
			StructuralType semanticType,
			string parentAssembly,
			BrickSlot? gridSlot = null )
		{
			EntityId = entityId;
			Position = position;
			Yaw = yaw;
			Size = size;
			SemanticType = semanticType;
			ParentAssembly = parentAssembly;
			GridSlot = gridSlot;
		}

		/// <summary>
		/// Create a StructuralPlacement from a CornerBrickPlacement
		/// (the resolver's output) with the Z coordinate set.
		/// </summary>
		public static StructuralPlacement FromCornerBrick(
			CornerBrickPlacement corner, float z, string parentAssembly, int index )
		{
			var center = corner.WorldCenter;
			center.z = z;
			return new StructuralPlacement(
				entityId: $"{parentAssembly}_corner_{index}",
				position: center,
				yaw: corner.WorldYaw * 180f / MathF.PI,
				size: corner.WorldSize,
				semanticType: StructuralType.CornerAssemblyBrick,
				parentAssembly: parentAssembly,
				gridSlot: corner.Slot );
		}

		/// <summary>
		/// Create a StructuralPlacement for a wall brick.
		/// </summary>
		public static StructuralPlacement ForWallBrick(
			Vector3 position, Vector3 size, float yaw,
			string parentAssembly, BrickSlot slot, int pieceIndex )
		{
			return new StructuralPlacement(
				entityId: $"{parentAssembly}_{pieceIndex}",
				position: position,
				yaw: yaw,
				size: size,
				semanticType: StructuralType.WallBrick,
				parentAssembly: parentAssembly,
				gridSlot: slot );
		}

		public bool Equals( StructuralPlacement other )
			=> EntityId == other.EntityId;

		public override bool Equals( object obj )
			=> obj is StructuralPlacement other && Equals( other );

		public override int GetHashCode()
			=> EntityId?.GetHashCode() ?? 0;

		public override string ToString()
			=> $"{SemanticType} [{EntityId}] pos={Position} rot={Yaw:F1} size={Size}";

		public static bool operator ==( StructuralPlacement a, StructuralPlacement b ) => a.Equals( b );
		public static bool operator !=( StructuralPlacement a, StructuralPlacement b ) => !a.Equals( b );
	}
}
