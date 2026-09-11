using System.Collections.Generic;

namespace Lute.Building
{
	/// <summary>
	/// A single piece in a build blueprint: a box at a world-relative
	/// position with a type, material, and rotation. This is the universal
	/// data format that all producers (BuildingGrammar, StyleGrammar,
	/// MonumentBlueprint) output and that all executors (NPCBuilder,
	/// VillageBuilder) consume.
	/// </summary>
	public struct BlueprintPiece
	{
		/// <summary> Position relative to the structure's origin (inches). </summary>
		public Vector3 Position { get; set; }

		/// <summary> Piece type: "WALL", "DOOR", "FLOOR", "ROOF", "COLUMN", "ARCH". </summary>
		public string PieceType { get; set; }

		/// <summary> Material path (e.g. "materials/medieval/stone_wall.vmat"). </summary>
		public string Material { get; set; }

		/// <summary> Yaw rotation in degrees. </summary>
		public float Rotation { get; set; }

		/// <summary> Size override (if zero, executor uses default for PieceType). </summary>
		public Vector3 Size { get; set; }
	}

	/// <summary>
	/// A build blueprint: an ordered list of pieces that, when placed
	/// by an executor, construct a complete structure. This is inert data —
	/// it doesn't know or care where it came from (grammar, monument spec,
	/// or hand-authored). The executor just iterates the list and places
	/// each piece.
	/// </summary>
	public class Blueprint
	{
		/// <summary> Display name for logs. </summary>
		public string Name { get; set; } = "Unnamed";

		/// <summary> World origin for the structure (pieces are relative to this). </summary>
		public Vector3 Origin { get; set; }

		/// <summary> Yaw rotation for the whole structure (degrees). </summary>
		public float Rotation { get; set; }

		/// <summary> Ordered list of pieces to place. </summary>
		public List<BlueprintPiece> Pieces { get; set; } = new();

		/// <summary> Cell size used by the original grid (inches). </summary>
		public float CellSize { get; set; } = 100f;

		/// <summary> Wall height (inches). </summary>
		public float WallHeight { get; set; } = 200f;

		/// <summary> Floor thickness (inches). </summary>
		public float FloorThickness { get; set; } = 10f;

		/// <summary> Default wall material. </summary>
		public string WallMaterial { get; set; } = "materials/medieval/stone_wall.vmat";

		/// <summary> Default floor material. </summary>
		public string FloorMaterial { get; set; } = "materials/medieval/plaza.vmat";

		/// <summary> Default roof material. </summary>
		public string RoofMaterial { get; set; } = "materials/medieval/roof.vmat";

		/// <summary> Default column material. </summary>
		public string ColumnMaterial { get; set; } = "materials/medieval/stone_tower.vmat";

		/// <summary>
		/// Convert a BuildingGrammar grid layout into a Blueprint piece list.
		/// This bridges the existing grammar output to the new Blueprint format.
		/// </summary>
		public static Blueprint FromGridLayout(
			Dictionary<Vector2Int, string> layout,
			Vector3 origin, float rotation, float cellSize,
			float wallHeight, float floorThickness,
			string wallMaterial, string floorMaterial )
		{
			var bp = new Blueprint
			{
				Name = "GridBuilding",
				Origin = origin,
				Rotation = rotation,
				CellSize = cellSize,
				WallHeight = wallHeight,
				FloorThickness = floorThickness,
				WallMaterial = wallMaterial,
				FloorMaterial = floorMaterial,
			};

			foreach ( var kvp in layout )
			{
				var piece = new BlueprintPiece
				{
					Position = new Vector3( kvp.Key.X * cellSize, kvp.Key.Y * cellSize, 0 ),
					PieceType = kvp.Value,
					Rotation = 0,
				};

				switch ( kvp.Value )
				{
					case "WALL":
						piece.Material = wallMaterial;
						break;
					case "DOOR":
						piece.Material = wallMaterial;
						break;
					case "FLOOR":
					default:
						piece.Material = floorMaterial;
						break;
				}

				bp.Pieces.Add( piece );
			}

			return bp;
		}

		/// <summary> Total piece count. </summary>
		public int PieceCount => Pieces.Count;

		// ── Serialization ──

		/// <summary>
		/// Export this blueprint to a JSON file in FileSystem.Data.
		/// The file can be inspected, hand-edited, and re-imported with
		/// <see cref="LoadFromFile"/>. Useful for debugging producer
		/// output and for hand-authoring structures.
		/// </summary>
		/// <param name="filename">Filename within FileSystem.Data (e.g. "blueprints/chapel.json").</param>
		/// <returns>True if written successfully.</returns>
		public bool SaveToFile( string filename )
		{
			try
			{
				FileSystem.Data.WriteJson( filename, this );
				Log.Info( $"Lute: Blueprint '{Name}' exported to {filename} ({Pieces.Count} pieces)." );
				return true;
			}
			catch ( System.Exception e )
			{
				Log.Warning( $"Lute: Blueprint export failed: {e.Message}" );
				return false;
			}
		}

		/// <summary>
		/// Load a blueprint from a JSON file in FileSystem.Data.
		/// The file should have been created by <see cref="SaveToFile"/>
		/// or hand-authored in the same format.
		/// </summary>
		/// <param name="filename">Filename within FileSystem.Data.</param>
		/// <returns>The loaded blueprint, or null on failure.</returns>
		public static Blueprint LoadFromFile( string filename )
		{
			try
			{
				if ( !FileSystem.Data.FileExists( filename ) )
				{
					Log.Warning( $"Lute: Blueprint file not found: {filename}" );
					return null;
				}

				var bp = FileSystem.Data.ReadJson<Blueprint>( filename );
				Log.Info( $"Lute: Blueprint loaded from {filename} — '{bp.Name}' ({bp.Pieces.Count} pieces)." );
				return bp;
			}
			catch ( System.Exception e )
			{
				Log.Warning( $"Lute: Blueprint load failed: {e.Message}" );
				return null;
			}
		}

		/// <summary>
		/// Export this blueprint to a JSON string (for console output,
		/// MCP inspection, or clipboard transfer). The string can be
		/// parsed back with <see cref="FromJsonString"/>.
		/// </summary>
		public string ToJsonString()
		{
			try
			{
				return System.Text.Json.JsonSerializer.Serialize( this,
					new System.Text.Json.JsonSerializerOptions { WriteIndented = true } );
			}
			catch ( System.Exception e )
			{
				Log.Warning( $"Lute: Blueprint JSON export failed: {e.Message}" );
				return "{}";
			}
		}

		/// <summary>
		/// Parse a blueprint from a JSON string (the format produced by
		/// <see cref="ToJsonString"/>).
		/// </summary>
		public static Blueprint FromJsonString( string json )
		{
			try
			{
				return System.Text.Json.JsonSerializer.Deserialize<Blueprint>( json );
			}
			catch ( System.Exception e )
			{
				Log.Warning( $"Lute: Blueprint JSON parse failed: {e.Message}" );
				return null;
			}
		}
	}
}
