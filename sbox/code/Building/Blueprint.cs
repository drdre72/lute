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
	/// Structural constraint on a blueprint (e.g. "must have a courtyard",
	/// "facade must be symmetric"). Used by the validator's style/grammar
	/// checks and by the AI authority model to express hard requirements
	/// the LLM must satisfy.
	/// </summary>
	public class BlueprintConstraint
	{
		/// <summary> Constraint code, e.g. "MUST_HAVE.COURTYARD". </summary>
		public string Code { get; set; }

		/// <summary> Human-readable description. </summary>
		public string Description { get; set; }

		/// <summary> True if the constraint is enforced (false = advisory). </summary>
		public bool Enforced { get; set; } = true;
	}

	/// <summary>
	/// A named anchor point in a blueprint — a semantic location other
	/// blueprints or systems can reference (e.g. "ENTRANCE", "ALTAR",
	/// "BELL_TOWER_TOP"). Decouples intent from raw coordinates.
	/// </summary>
	public class BlueprintAnchor
	{
		public string Name { get; set; }
		public Vector3 Position { get; set; }
		public string Kind { get; set; } // ENTRANCE, CONNECTION, LANDMARK, etc.
	}

	/// <summary>
	/// A dependency on another blueprint (by id+version). Lets the
	/// ConstructionDirector build a dependency graph: e.g. a village
	/// depends on its chapel, which depends on its foundation.
	/// </summary>
	public class BlueprintDependency
	{
		public string BlueprintId { get; set; }
		public int MinVersion { get; set; }
		public string Role { get; set; } // e.g. "FOUNDATION", "ROOF"
	}

	/// <summary>
	/// Provenance: where this blueprint came from. Lets an agent answer
	/// "who/what produced this and with what parameters?" without
	/// guessing from the name.
	/// </summary>
	public class BlueprintProvenance
	{
		/// <summary> Producer name, e.g. "MonumentBlueprintProducer". </summary>
		public string Producer { get; set; }

		/// <summary> Preset/spec name, e.g. "StPetersBasilica". </summary>
		public string Preset { get; set; }

		/// <summary> Seed used for any deterministic randomness. </summary>
		public int Seed { get; set; }

		/// <summary> Free-form parameters (JSON-serializable). </summary>
		public Dictionary<string, string> Parameters { get; set; } = new();

		/// <summary> ISO timestamp of creation (UTC). </summary>
		public string CreatedUtc { get; set; }
	}

	/// <summary>
	/// A build blueprint: an ordered list of pieces that, when placed
	/// by an executor, construct a complete structure. This is inert data —
	/// it doesn't know or care where it came from (grammar, monument spec,
	/// or hand-authored). The executor just iterates the list and places
	/// each piece.
	///
	/// As of Phase 2, a Blueprint is a versioned, identifiable intermediate
	/// representation: it carries an ID, version, content hash, metadata,
	/// constraints, anchors, dependencies, and provenance. This lets the
	/// system diff versions, roll back failed executions, and let an LLM
	/// reason over typed requirements rather than raw geometry.
	/// </summary>
	public class Blueprint
	{
		static int _gridSeq;
		/// <summary> Stable identifier for this structure (e.g. "chapel_001"). </summary>
		public string Id { get; set; } = "";

		/// <summary> Monotonic version number. Incremented on each modification. </summary>
		public int Version { get; set; } = 1;

		/// <summary>
		/// Content hash of the piece list (SHA-256 of serialized pieces).
		/// Computed by <see cref="ComputeHash"/>; used to detect
		/// unauthorized drift and to compare versions.
		/// </summary>
		public string Hash { get; set; } = "";

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

		/// <summary> Hard constraints the blueprint must satisfy (style/grammar). </summary>
		public List<BlueprintConstraint> Constraints { get; set; } = new();

		/// <summary> Named semantic anchor points (entrances, landmarks, connections). </summary>
		public List<BlueprintAnchor> Anchors { get; set; } = new();

		/// <summary> Dependencies on other blueprints (by id+version). </summary>
		public List<BlueprintDependency> Dependencies { get; set; } = new();

		/// <summary> Where this blueprint came from. </summary>
		public BlueprintProvenance Provenance { get; set; }

		/// <summary> Free-form metadata (tags, category, author, etc.). </summary>
		public Dictionary<string, string> Metadata { get; set; } = new();

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
				Id = $"grid_{_gridSeq++}",
				Version = 1,
				Name = "GridBuilding",
				Origin = origin,
				Rotation = rotation,
				CellSize = cellSize,
				WallHeight = wallHeight,
				FloorThickness = floorThickness,
				WallMaterial = wallMaterial,
				FloorMaterial = floorMaterial,
				Provenance = new BlueprintProvenance
				{
					Producer = "Blueprint.FromGridLayout",
					Parameters = new Dictionary<string, string>
					{
						["layoutCells"] = layout.Count.ToString(),
						["cellSize"] = cellSize.ToString(),
					},
				},
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

			bp.ComputeHash();
			BlueprintRegistry.Register( bp );
			return bp;
		}

		/// <summary> Total piece count. </summary>
		public int PieceCount => Pieces.Count;

		/// <summary>
		/// Compute a SHA-256 content hash over the serialized piece list and
		/// store it in <see cref="Hash"/>. Used to detect drift and to
		/// compare blueprint versions deterministically.
		/// </summary>
		public string ComputeHash()
		{
			try
			{
				using var sha = System.Security.Cryptography.SHA256.Create();
				var json = System.Text.Json.JsonSerializer.Serialize( Pieces );
				var bytes = System.Text.Encoding.UTF8.GetBytes( json );
				var hash = sha.ComputeHash( bytes );
				Hash = System.BitConverter.ToString( hash ).Replace( "-", "" ).ToLowerInvariant();
				return Hash;
			}
			catch ( System.Exception e )
			{
				Log.Warning( $"Lute: Blueprint hash failed: {e.Message}" );
				return Hash = "";
			}
		}

		/// <summary>
		/// Compute the axis-aligned bounding box of all pieces (relative to
		/// <see cref="Origin"/>). Returns null if there are no pieces.
		/// </summary>
		public (Vector3 Min, Vector3 Max)? ComputeBounds()
		{
			if ( Pieces.Count == 0 )
				return null;

			float minX = float.MaxValue, maxX = float.MinValue;
			float minY = float.MaxValue, maxY = float.MinValue;
			float minZ = float.MaxValue, maxZ = float.MinValue;

			foreach ( var p in Pieces )
			{
				if ( p.Position.x < minX ) minX = p.Position.x;
				if ( p.Position.x > maxX ) maxX = p.Position.x;
				if ( p.Position.y < minY ) minY = p.Position.y;
				if ( p.Position.y > maxY ) maxY = p.Position.y;
				if ( p.Position.z < minZ ) minZ = p.Position.z;
				if ( p.Position.z > maxZ ) maxZ = p.Position.z;
			}

			return ( new Vector3( minX, minY, minZ ), new Vector3( maxX, maxY, maxZ ) );
		}

		/// <summary>
		/// Bump <see cref="Version"/> and recompute <see cref="Hash"/>.
		/// Call after any structural modification so the versioning
		/// registry can track the new revision.
		/// </summary>
		public void BumpVersion()
		{
			Version++;
			ComputeHash();
		}

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
