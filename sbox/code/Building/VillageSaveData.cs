using System;
using System.Collections.Generic;
using System.Linq;
using Sandbox;

namespace Lute.Building
{
	/// <summary>
	/// Serialized form of a village build progress. Saves the full task
	/// list with per-task status and pieces-placed count, so a build can
	/// be resumed from exactly where it left off.
	///
	/// Saves are incremental: only the progress fields (Status,
	/// PiecesPlaced, TotalPieces) change between saves. A hash of these
	/// fields is compared to the last save's hash to skip redundant saves
	/// (when no progress was made, e.g. NPC was walking between sites).
	/// </summary>
	public class VillageSaveData
	{
		public Vector3 Center { get; set; }
		public List<TaskProgress> Tasks { get; set; } = new();
		public float ElapsedTime { get; set; }
		public string ProgressHash { get; set; }
		public DateTime LastSaveTime { get; set; }
	}

	/// <summary>
	/// Per-task progress entry in the save file. Only the fields that
	/// change during a build are saved — the full layout is regenerated
	/// deterministically from VillageGrammar on load.
	/// </summary>
	public class TaskProgress
	{
		public string Name { get; set; }
		public int Status { get; set; }      // 0=pending, 1=in-progress, 2=complete
		public int PiecesPlaced { get; set; }
		public int TotalPieces { get; set; }
	}

	/// <summary>
	/// Save/load for village build progress. Uses FileSystem.Data for
	/// persistence. The save is non-redundant: if the progress hash
	/// matches the last save, the write is skipped.
	/// </summary>
	public static class VillagePersistence
	{
		const string DefaultFilename = "village_save.json";

		/// <summary>
		/// Compute a hash of all task progress. Used to detect whether
		/// anything changed since the last save. Only Status and
		/// PiecesPlaced matter — TotalPieces is set once when the task
		/// starts and doesn't change after that.
		/// </summary>
		public static string ComputeProgressHash( List<VillageBuildTask> tasks )
		{
			int hash = 17;
			foreach ( var t in tasks )
			{
				hash = hash * 31 + t.Status;
				hash = hash * 31 + t.PiecesPlaced;
			}
			return hash.ToString( "x8" );
		}

		/// <summary>
		/// Save village progress to FileSystem.Data. Skips the write if
		/// the progress hash matches the last saved hash (non-redundant).
		/// Returns true if saved, false if skipped.
		/// </summary>
		public static bool SaveProgress( string filename, Vector3 center,
			List<VillageBuildTask> tasks, float elapsedTime )
		{
			var hash = ComputeProgressHash( tasks );

			// Check if anything changed since last save
			if ( FileExists( filename ) )
			{
				var existing = LoadProgress( filename );
				if ( existing != null && existing.ProgressHash == hash )
					return false; // no progress since last save — skip
			}

			var data = new VillageSaveData
			{
				Center = center,
				ElapsedTime = elapsedTime,
				ProgressHash = hash,
				LastSaveTime = DateTime.Now,
				Tasks = tasks.Select( t => new TaskProgress
				{
					Name = t.Name,
					Status = t.Status,
					PiecesPlaced = t.PiecesPlaced,
					TotalPieces = t.TotalPieces,
				} ).ToList(),
			};

			FileSystem.Data.WriteJson( filename, data );
			return true;
		}

		/// <summary> Convenience overload using the default filename. </summary>
		public static bool SaveProgress( Vector3 center, List<VillageBuildTask> tasks, float elapsedTime )
			=> SaveProgress( DefaultFilename, center, tasks, elapsedTime );

		/// <summary>
		/// Load village progress from FileSystem.Data. Returns null if
		/// no save exists.
		/// </summary>
		public static VillageSaveData LoadProgress( string filename )
		{
			if ( !FileExists( filename ) )
				return null;

			try
			{
				return FileSystem.Data.ReadJson<VillageSaveData>( filename );
			}
			catch
			{
				return null;
			}
		}

		/// <summary> Convenience overload using the default filename. </summary>
		public static VillageSaveData LoadProgress()
			=> LoadProgress( DefaultFilename );

		/// <summary> Check if a save file exists. </summary>
		public static bool FileExists( string filename )
			=> FileSystem.Data.FileExists( filename );

		/// <summary> Check if a save exists with the default filename. </summary>
		public static bool HasSave()
			=> FileExists( DefaultFilename );

		/// <summary>
		/// Apply saved progress to a freshly-generated task list. Matches
		/// tasks by name. Tasks not in the save remain pending (Status=0).
		/// </summary>
		public static void ApplyProgress( List<VillageBuildTask> tasks, VillageSaveData save )
		{
			if ( save?.Tasks == null ) return;

			var saveMap = save.Tasks.ToDictionary( t => t.Name );
			foreach ( var task in tasks )
			{
				if ( saveMap.TryGetValue( task.Name, out var saved ) )
				{
					task.Status = saved.Status;
					task.PiecesPlaced = saved.PiecesPlaced;
					task.TotalPieces = saved.TotalPieces;
				}
			}
		}

		/// <summary> Delete the save file (for a fresh village build). </summary>
		public static void ClearSave( string filename = DefaultFilename )
		{
			if ( FileExists( filename ) )
				FileSystem.Data.DeleteFile( filename );
		}
	}
}
