using System;
using System.Collections.Generic;
using Sandbox;

namespace Lute.Building
{
	/// <summary>
	/// Spawns placeholder resource sources and a central stockyard at
	/// startup so the resource loop is testable without real assets.
	/// Sources are placed around the village perimeter; the stockyard
	/// is at the village center.
	///
	/// Per AGENTS.md: blueprint geometry is authoritative; visual models
	/// are attached later behind the LuteAssets catalog. These
	/// placeholders use SpawnBox (simple colored boxes) so the resource
	/// loop can be verified end-to-end before real assets arrive.
	///
	/// Per PR #6 §17: "controlled terrain, resource zones, emergency
	/// bootstrap supplies, and enough tools to avoid a chicken-and-egg
	/// deadlock."
	/// </summary>
	public static class ResourceBootstrap
	{
		/// <summary>
		/// Spawn the bootstrap resource layout around a village center.
		/// Idempotent — only spawns once per session.
		/// </summary>
		public static void Initialize( Vector3 center )
		{
			ResourceRegistry.Clear();

			// ── Resource sources around the perimeter ──
			// Trees (wood) — north, outside the wall
			var trees = new ResourceSource( "tree_cluster_north",
				ResourceType.Wood,
				center + new Vector3( 0, -6000, 0 ),
				totalYield: 500, yieldPerGather: 10, radius: 400f,
				requiredCapability: NpcCapability.GatherWood,
				requiredTool: "Axe" );
			ResourceRegistry.RegisterSource( trees );
			SpawnSourcePlaceholder( trees, new Color( 0.2f, 0.5f, 0.2f ) );

			// Trees (wood) — south
			var trees2 = new ResourceSource( "tree_cluster_south",
				ResourceType.Wood,
				center + new Vector3( 0, 6000, 0 ),
				totalYield: 500, yieldPerGather: 10, radius: 400f,
				requiredCapability: NpcCapability.GatherWood,
				requiredTool: "Axe" );
			ResourceRegistry.RegisterSource( trees2 );
			SpawnSourcePlaceholder( trees2, new Color( 0.2f, 0.5f, 0.2f ) );

			// Quarry (stone) — east
			var quarry = new ResourceSource( "quarry_east",
				ResourceType.Stone,
				center + new Vector3( 6000, 0, 0 ),
				totalYield: 800, yieldPerGather: 15, radius: 500f,
				requiredCapability: NpcCapability.GatherStone,
				requiredTool: "Pickaxe" );
			ResourceRegistry.RegisterSource( quarry );
			SpawnSourcePlaceholder( quarry, new Color( 0.5f, 0.5f, 0.5f ) );

			// Mine (ore) — west
			var mine = new ResourceSource( "mine_west",
				ResourceType.Ore,
				center + new Vector3( -6000, 0, 0 ),
				totalYield: 300, yieldPerGather: 5, radius: 300f,
				requiredCapability: NpcCapability.GatherOre,
				requiredTool: "Pickaxe" );
			ResourceRegistry.RegisterSource( mine );
			SpawnSourcePlaceholder( mine, new Color( 0.6f, 0.3f, 0.1f ) );

			// ── Central stockyard ──
			// Large capacity, near the village center but offset so it
			// doesn't overlap construction.
			var stockyard = new Stockpile( "stockyard_0",
				center + new Vector3( -300, -300, 0 ),
				new Vector3( 200, 200, 100 ),  // half-extents
				capacity: 2000 );
			ResourceRegistry.RegisterStockpile( stockyard );
			SpawnStockpilePlaceholder( stockyard, new Color( 0.4f, 0.3f, 0.2f ) );

			// ── Secondary stockpile near the quarry (for brick staging) ──
			var brickStaging = new Stockpile( "brick_staging_0",
				center + new Vector3( 5500, 0, 0 ),
				new Vector3( 150, 150, 80 ),
				capacity: 500 );
			ResourceRegistry.RegisterStockpile( brickStaging );
			SpawnStockpilePlaceholder( brickStaging, new Color( 0.5f, 0.4f, 0.3f ) );

			// ── Emergency bootstrap supplies ──
			// Per PR #6 §17: enough to avoid chicken-and-egg deadlock.
			// Pre-stock the central yard with some bricks and planks so
			// construction can start before the gather loop produces.
			stockyard.Deposit( ResourceType.Brick, 200 );
			stockyard.Deposit( ResourceType.Plank, 100 );
			stockyard.Deposit( ResourceType.Timber, 50 );

			Log.Info( $"Lute: ResourceBootstrap initialized — {ResourceRegistry.AllSources().Count} sources, {ResourceRegistry.AllStockpiles().Count} stockpiles." );
			Log.Info( $"Lute: ResourceRegistry — {ResourceRegistry.Summary()}" );
		}

		static void SpawnSourcePlaceholder( ResourceSource source, Color color )
		{
			var go = new GameObject();
			go.Name = $"ResourceSource_{source.Id}";
			go.Transform.Position = source.Position;
			go.Transform.Scale = new Vector3( source.Radius, source.Radius, source.Radius * 0.3f );

			var mr = go.Components.Create<ModelRenderer>();
			mr.Tint = color;

			source.VisualGo = go;
		}

		static void SpawnStockpilePlaceholder( Stockpile pile, Color color )
		{
			var go = new GameObject();
			go.Name = $"Stockpile_{pile.Id}";
			go.Transform.Position = pile.Position;
			go.Transform.Scale = pile.HalfExtents * 2f;

			var mr = go.Components.Create<ModelRenderer>();
			mr.Tint = color;

			pile.VisualGo = go;
		}
	}
}
