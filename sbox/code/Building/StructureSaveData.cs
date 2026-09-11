using System.Collections.Generic;
using System.Linq;
using Lute.Building;
using Sandbox;

namespace Lute.Building
{
	/// <summary>
	/// Serialized form of a built structure: world origin + flat grid map
	/// keyed by "x,y" strings. JSON-friendly so it survives
	/// FileSystem.Data.WriteJson / FileSystem.Data.ReadJson.
	/// </summary>
	public class StructureSaveData
	{
		public Vector3 WorldOrigin { get; set; }
		public Dictionary<string, string> GridData { get; set; } = new();
	}

	/// <summary>
	/// Save/load pair for built structures. The original guide only wrote
	/// the JSON; this adds the read-back path so a load can restore a
	/// finished structure instantly (no per-segment build delay — replaying
	/// the build delay on every scene load would be wrong).
	/// </summary>
	public static class StructurePersistence
	{
		public static void SaveStructure( string filename, Vector3 origin, Dictionary<Vector2Int, string> layout )
		{
			var data = new StructureSaveData { WorldOrigin = origin };

			foreach ( var kvp in layout )
				data.GridData[$"{kvp.Key.X},{kvp.Key.Y}"] = kvp.Value;

			FileSystem.Data.WriteJson( filename, data );
		}

		public static Dictionary<Vector2Int, string> LoadLayout( string filename )
		{
			var data = FileSystem.Data.ReadJson<StructureSaveData>( filename );
			var layout = new Dictionary<Vector2Int, string>();

			if ( data?.GridData == null )
				return layout;

			foreach ( var kvp in data.GridData )
			{
				var parts = kvp.Key.Split( ',' );
				if ( parts.Length != 2 )
					continue;
				if ( !int.TryParse( parts[0], out int x ) || !int.TryParse( parts[1], out int y ) )
					continue;
				layout[new Vector2Int( x, y )] = kvp.Value;
			}

			return layout;
		}

		public static Vector3 LoadOrigin( string filename )
		{
			var data = FileSystem.Data.ReadJson<StructureSaveData>( filename );
			return data?.WorldOrigin ?? Vector3.Zero;
		}
	}
}
