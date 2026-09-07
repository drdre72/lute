using Sandbox.Utility;

/// <summary>
/// Procedurally generates the Lute world terrain using the S&amp;Box
/// <see cref="Noise"/> utilities (Perlin / Simplex / Fbm).
///
/// Builds a <see cref="Terrain"/> component with a freshly allocated
/// <see cref="TerrainStorage"/>, fills its heightmap from layered fractal
/// noise, and syncs the result to the GPU + collider so it is immediately
/// walkable. Intended to be called once from <see cref="LuteWorld.Build"/>
/// during world construction (server-authoritative).
/// </summary>
public sealed class LuteTerrainGenerator : Component
{
	/// <summary> World units across one side of the terrain square. </summary>
	[Property] public float TerrainSize { get; set; } = 20000f;

	/// <summary> World units of max height (raw ushort.MaxValue maps to this). </summary>
	[Property] public float TerrainHeight { get; set; } = 4000f;

	/// <summary> Heightmap resolution (texels per side). Powers of two work best. </summary>
	[Property] public int Resolution { get; set; } = 512;

	/// <summary> Seed shared with the noise fields so the world is deterministic. </summary>
	[Property] public int Seed { get; set; } = 5633;

	/// <summary>
	/// Builds a terrain under <paramref name="parent"/> and returns the
	/// GameObject holding the <see cref="Terrain"/> component.
	/// </summary>
	public GameObject Build( GameObject parent )
	{
		var go = Scene.CreateObject( true );
		go.Name = "WorldTerrain";
		go.SetParent( parent );
		go.WorldPosition = new Vector3( -TerrainSize * 0.5f, -TerrainSize * 0.5f, 0f );

		var terrain = go.AddComponent<Terrain>();

		var storage = new TerrainStorage();
		storage.SetResolution( Resolution );
		storage.TerrainSize = TerrainSize;
		storage.TerrainHeight = TerrainHeight;

		GenerateHeightmap( storage );

		terrain.Storage = storage; // triggers Create() internally

		// Push the CPU heightmap we just wrote into the GPU texture + collider.
		terrain.SyncGPUTexture();
		terrain.UpdateCollision(
			Terrain.SyncFlags.Height,
			new RectInt( 0, 0, Resolution, Resolution ) );

		Log.Info( $"Lute: terrain generated ({Resolution}x{Resolution}, " +
			$"{TerrainSize}x{TerrainSize} world units, {TerrainHeight} max height)." );

		return go;
	}

	/// <summary>
	/// Fills <paramref name="storage"/>'s heightmap with layered fractal noise.
	/// A low-frequency continent mask is combined with mid-frequency hills and
	/// high-frequency detail, then normalized to the full ushort range.
	/// </summary>
	void GenerateHeightmap( TerrainStorage storage )
	{
		var res = storage.Resolution;
		var heightScale = (float)ushort.MaxValue;

		// Three noise fields at different frequencies for layered terrain.
		// FractalParameters gives us octaves so the result isn't a single smooth blob.
		var continents = Noise.PerlinField( new Noise.FractalParameters(
			Seed: Seed, Frequency: 0.0008f, Octaves: 3, Gain: 0.5f, Lacunarity: 2.0f ) );

		var hills = Noise.SimplexField( new Noise.FractalParameters(
			Seed: Seed + 1, Frequency: 0.004f, Octaves: 4, Gain: 0.5f, Lacunarity: 2.0f ) );

		var detail = Noise.PerlinField( new Noise.FractalParameters(
			Seed: Seed + 2, Frequency: 0.02f, Octaves: 2, Gain: 0.5f, Lacunarity: 2.0f ) );

		// Sample in normalized [0,1] space then scale to world coords so the
		// noise frequencies are independent of resolution.
		float worldPerTexel = storage.TerrainSize / res;

		for ( int y = 0; y < res; y++ )
		{
			for ( int x = 0; x < res; x++ )
			{
				float wx = x * worldPerTexel;
				float wy = y * worldPerTexel;

				// Continent mask: broad landmasses with a falloff toward the edges
				// so the map borders drop into low terrain (visually like sea/void).
				float c = continents.Sample( wx, wy );
				float edgeFalloff = EdgeFalloff( x, y, res );
				float continent = c * edgeFalloff;

				// Hills: mid-frequency relief, only where there is land.
				float h = hills.Sample( wx, wy );

				// Detail: small bumps layered on top.
				float d = detail.Sample( wx, wy );

				// Weighted blend. continent dominates macro shape, hills add
				// vertical relief, detail adds surface roughness.
				float n = continent * 0.6f + h * 0.3f + d * 0.1f;

				// Apply a gentle power curve to flatten low areas (plains/sea bed)
				// and sharpen peaks.
				n = MathF.Pow( n.Clamp( 0f, 1f ), 1.3f );

				storage.HeightMap[y * res + x] = (ushort)( n * heightScale );
			}
		}
	}

	/// <summary>
	/// Returns a 0..1 multiplier that fades terrain down near the map borders,
	/// producing a radial-ish island/continent shape rather than noise clipping
	/// hard at the edges. Center is 1, edges approach 0.
	/// </summary>
	static float EdgeFalloff( int x, int y, int res )
	{
		// Normalized distance from center (0 at center, ~0.707 at corners).
		float nx = ( x / (float)res ) * 2f - 1f;
		float ny = ( y / (float)res ) * 2f - 1f;
		float dist = MathF.Sqrt( nx * nx + ny * ny );

		// Start fading at 0.55, fully gone by 0.85.
		float fade = 1f - ( dist - 0.55f ) / 0.30f;
		return fade.Clamp( 0f, 1f );
	}
}
