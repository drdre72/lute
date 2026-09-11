using System.Collections.Generic;
using System.Linq;

namespace Lute.Building
{
	/// <summary>
	/// Describes the difference between two blueprint versions. Lets an
	/// agent answer "what changed between v3 and v4?" without re-deriving
	/// the geometry. Used by the rollback system and by AI critique loops
	/// to understand what an LLM modification actually did.
	/// </summary>
	public class BlueprintDiff
	{
		public string BlueprintId { get; set; }
		public int FromVersion { get; set; }
		public int ToVersion { get; set; }

		/// <summary> Piece indices present in To but not in From (added). </summary>
		public List<int> Added { get; set; } = new();

		/// <summary> Piece indices present in From but not in To (removed). </summary>
		public List<int> Removed { get; set; } = new();

		/// <summary> Piece indices present in both but with changed position/type/material. </summary>
		public List<int> Modified { get; set; } = new();

		/// <summary> True if no pieces changed (only metadata/version). </summary>
		public bool IsEmpty => Added.Count == 0 && Removed.Count == 0 && Modified.Count == 0;

		public string Summary =>
			$"v{FromVersion} -> v{ToVersion}: +{Added.Count} -{Removed.Count} ~{Modified.Count}";
	}

	/// <summary>
	/// Computes <see cref="BlueprintDiff"/>s between two blueprints.
	/// Matching is by piece index (same list position). This is deliberate:
	/// producers emit deterministic ordered lists, so index-based diffing
	/// is stable and cheap. A future improvement could hash-match pieces
	/// for reorderings, but that is not needed yet.
	/// </summary>
	public static class BlueprintDiffer
	{
		/// <summary> Diff two blueprints (must share an Id). </summary>
		public static BlueprintDiff Diff( Blueprint from, Blueprint to )
		{
			var d = new BlueprintDiff
			{
				BlueprintId = to?.Id ?? from?.Id ?? "",
				FromVersion = from?.Version ?? 0,
				ToVersion = to?.Version ?? 0,
			};

			if ( from == null || to == null )
				return d;

			int max = System.Math.Max( from.Pieces.Count, to.Pieces.Count );
			for ( int i = 0; i < max; i++ )
			{
				bool hasFrom = i < from.Pieces.Count;
				bool hasTo = i < to.Pieces.Count;

				if ( hasFrom && !hasTo )
					d.Removed.Add( i );
				else if ( !hasFrom && hasTo )
					d.Added.Add( i );
				else
				{
					var pf = from.Pieces[i];
					var pt = to.Pieces[i];
					if ( pf.Position != pt.Position ||
						 pf.PieceType != pt.PieceType ||
						 pf.Material != pt.Material ||
						 pf.Rotation != pt.Rotation )
						d.Modified.Add( i );
				}
			}

			return d;
		}
	}

	/// <summary>
	/// In-memory registry of named blueprint versions. Lets the system:
	/// - store every revision of a blueprint,
	/// - retrieve a specific version,
	/// - list the version history,
	/// - roll back to a prior version if a modification fails validation
	///   or execution.
	///
	/// This is the foundation of the "AI critique -> v4 -> validate ->
	/// execute -> if fails, rollback to v3" loop the professor described.
	/// </summary>
	public static class BlueprintRegistry
	{
		// id -> (version -> blueprint)
		static readonly Dictionary<string, Dictionary<int, Blueprint>> _store = new();

		/// <summary> Register a blueprint under its Id and Version. </summary>
		public static void Register( Blueprint bp )
		{
			if ( bp == null || string.IsNullOrEmpty( bp.Id ) )
				return;

			if ( !_store.TryGetValue( bp.Id, out var versions ) )
			{
				versions = new Dictionary<int, Blueprint>();
				_store[bp.Id] = versions;
			}
			versions[bp.Version] = bp;
		}

		/// <summary> Get a specific version of a blueprint by id. </summary>
		public static Blueprint Get( string id, int version )
		{
			if ( _store.TryGetValue( id, out var versions ) &&
				 versions.TryGetValue( version, out var bp ) )
				return bp;
			return null;
		}

		/// <summary> Get the latest registered version of a blueprint. </summary>
		public static Blueprint GetLatest( string id )
		{
			if ( !_store.TryGetValue( id, out var versions ) || versions.Count == 0 )
				return null;
			return versions.OrderByDescending( kvp => kvp.Key ).First().Value;
		}

		/// <summary> List all known versions of a blueprint id (ascending). </summary>
		public static List<int> Versions( string id )
		{
			if ( !_store.TryGetValue( id, out var versions ) )
				return new();
			return versions.Keys.OrderBy( v => v ).ToList();
		}

		/// <summary> List all registered blueprint ids. </summary>
		public static List<string> Ids() => _store.Keys.OrderBy( k => k ).ToList();

		/// <summary>
		/// Roll back a blueprint id to a prior version: returns the prior
		/// blueprint and re-registers it as a new (latest) version so the
		/// forward history is preserved. Returns null if the prior version
		/// is unknown.
		/// </summary>
		public static Blueprint Rollback( string id, int toVersion )
		{
			var prior = Get( id, toVersion );
			if ( prior == null )
				return null;

			// Re-register as a new version so we keep the failed revision
			// in history but the live pointer moves back.
			var latest = GetLatest( id );
			int newVersion = ( latest?.Version ?? toVersion ) + 1;
			var restored = new Blueprint
			{
				Id = prior.Id,
				Version = newVersion,
				Name = prior.Name,
				Origin = prior.Origin,
				Rotation = prior.Rotation,
				CellSize = prior.CellSize,
				WallHeight = prior.WallHeight,
				FloorThickness = prior.FloorThickness,
				WallMaterial = prior.WallMaterial,
				FloorMaterial = prior.FloorMaterial,
				RoofMaterial = prior.RoofMaterial,
				ColumnMaterial = prior.ColumnMaterial,
				Constraints = prior.Constraints,
				Anchors = prior.Anchors,
				Dependencies = prior.Dependencies,
				Provenance = prior.Provenance,
				Metadata = prior.Metadata,
				Pieces = new List<BlueprintPiece>( prior.Pieces ),
			};
			restored.ComputeHash();
			Register( restored );
			return restored;
		}

		/// <summary> Clear all registered blueprints (test helper). </summary>
		public static void Clear() => _store.Clear();
	}
}
