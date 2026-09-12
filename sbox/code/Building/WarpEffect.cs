using System;

namespace Lute.Building
{
	/// <summary>
	/// Simple deterministic "poof/warp" visual effect for builder teleportation.
	/// Spawns a temporary GameObject with a scaled model that expands and fades
	/// over a short duration. Each builder gets a distinct color so the effects
	/// are visually distinguishable. No particle assets required — uses a
	/// primitive model with tinted material.
	/// </summary>
	public sealed class WarpEffect : Component
	{
		/// <summary> Duration of the effect in seconds. </summary>
		[Property] public float Duration { get; set; } = 0.6f;

		/// <summary> Initial scale of the effect sphere. </summary>
		[Property] public float StartScale { get; set; } = 20f;

		/// <summary> Final scale of the effect sphere. </summary>
		[Property] public float EndScale { get; set; } = 120f;

		/// <summary> Tint color for the effect. </summary>
		[Property] public Color Tint { get; set; } = Color.White;

		ModelRenderer _renderer;
		float _elapsed;

		protected override void OnStart()
		{
			_renderer = Components.GetOrCreate<ModelRenderer>();
			_renderer.Model = Model.Load( "models/dev/box.vmdl" );
			_renderer.Tint = Tint;
		}

		protected override void OnUpdate()
		{
			_elapsed += Time.Delta;
			float t = Math.Clamp( _elapsed / Duration, 0f, 1f );

			// Expand outward
			float scale = StartScale + (EndScale - StartScale) * t;
			WorldScale = new Vector3( scale, scale, scale * 0.8f );

			// Fade out
			if ( _renderer is not null )
				_renderer.Tint = new Color( Tint.r, Tint.g, Tint.b, 1f - t );

			if ( _elapsed >= Duration )
				GameObject.Destroy();
		}

		/// <summary>
		/// Spawn a warp effect at the given position with the given color.
		/// The effect auto-destroys when complete.
		/// </summary>
		public static void Spawn( Scene scene, Vector3 position, Color tint, float duration = 0.6f )
		{
			if ( scene == null ) return;

			var go = scene.CreateObject( true );
			go.Name = "WarpEffect";
			go.WorldPosition = position + Vector3.Up * 40f;

			var effect = go.AddComponent<WarpEffect>();
			effect.Tint = tint;
			effect.Duration = duration;

			Log.Info( $"Lute: WarpEffect spawned at {position} with tint {tint}." );
		}

		/// <summary>
		/// Per-builder tint colors for warp effects.
		/// Builder 0: cyan, Builder 1: magenta, Builder 2: yellow.
		/// </summary>
		public static Color GetBuilderTint( int builderId )
		{
			return builderId switch
			{
				0 => new Color( 0.2f, 0.8f, 1.0f ),   // cyan
				1 => new Color( 1.0f, 0.3f, 0.9f ),   // magenta
				2 => new Color( 1.0f, 0.9f, 0.2f ),   // yellow
				_ => Color.White,
			};
		}
	}
}
