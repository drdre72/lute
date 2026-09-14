using System;
using System.Collections.Generic;
using System.Linq;
using Sandbox;
using Lute.Items;
using Lute.Farming;

namespace Lute.Building
{
	/// <summary>
	/// Bootstraps the resource economy at startup. Per the professor's
	/// Gate 2c hardening notes, this now unifies with the existing
	/// physical economy instead of spawning a parallel one:
	///
	/// <list type="bullet">
	/// <item><b>Sources</b>: discovers existing <see cref="ResourceNode"/>
	/// components already placed in the scene and wraps each as a
	/// <see cref="ResourceSource"/>. The physical node keeps doing tool
	/// checks, durability, and respawn; the registry just exposes it for
	/// spatial queries. Synthetic sources (no physical node) are still
	/// supported for clusters registered by position.</item>
	/// <item><b>Stockpiles</b>: each <see cref="Stockpile"/> is backed by a
	/// real <see cref="LuteInventory"/> component on a GameObject, so
	/// material lives in actual 30-slot/500-stack inventory with tool
	/// durability — no teleporting, no double-promising.</item>
	/// <item><b>Emergency supplies</b>: pre-stocked into the real
	/// inventory, not a data-class dictionary.</item>
	/// </list>
	///
	/// Per AGENTS.md: blueprint geometry is authoritative; visual models
	/// are attached later behind the LuteAssets catalog. Placeholders use
	/// ModelRenderer + tint so the loop is verifiable before real assets.
	///
	/// Per PR #6 §17: "controlled terrain, resource zones, emergency
	/// bootstrap supplies, and enough tools to avoid a chicken-and-egg
	/// deadlock."
	/// </summary>
	public static class ResourceBootstrap
	{
		static bool _initialized;

		/// <summary>
		/// Initialize the bootstrap resource layout around a village
		/// center. Idempotent — only runs once per session. Discovers
		/// existing <see cref="ResourceNode"/> components in the scene
		/// and registers them, then spawns a central stockyard and a
		/// brick staging stockpile (both backed by real
		/// <see cref="LuteInventory"/>), and pre-stocks emergency supplies.
		/// </summary>
		public static void Initialize( Vector3 center )
		{
			if ( _initialized ) return;
			_initialized = true;

			ResourceRegistry.Clear();

			int discovered = 0;
			int synthetic = 0;

			// ── Discover existing physical ResourceNodes in the scene ──
			// Each becomes a registered ResourceSource that adapts the
			// real node (tool checks, durability, respawn all stay).
			try
			{
				var nodes = Game.ActiveScene.GetAllComponents<ResourceNode>().ToList();
				foreach ( var node in nodes )
				{
					var id = $"node_{node.GameObject.Name}_{node.GameObject.Id}";
					var type = node.GetYieldType();
					var tool = node.GetRequiredTool();
					var cap = CapabilityForNode( node.Node );
					var pos = node.GameObject.WorldPosition;

					var src = new ResourceSource(
						id, type, pos,
						totalYield: node.MaxGathers * node.YieldPerGather,
						yieldPerGather: node.YieldPerGather,
						radius: 300f,
						requiredCapability: cap,
						requiredTool: tool,
						node: node );
					ResourceRegistry.RegisterSource( src );
					discovered++;
				}
			}
			catch ( Exception e )
			{
				Log.Warning( $"Lute: ResourceBootstrap scene discovery failed: {e.Message}" );
			}

			// ── Synthetic fallback sources (no physical node) ──
			// Only add these if discovery found nothing, so we don't
			// double-register. Sources are placed just 10m outside the
			// village perimeter (perimeter half-width ~669 units, so
			// sources at ~1063 units from center).
			if ( discovered == 0 )
			{
				float offset = 1063f; // ~17m perimeter + 10m outside

				var trees = new ResourceSource( "tree_cluster_north",
					ItemType.Wood,
					center + new Vector3( 0, -offset, 0 ),
					totalYield: 500, yieldPerGather: 10, radius: 100f,
					requiredCapability: NpcCapability.GatherWood,
					requiredTool: ItemType.Pickaxe );
				ResourceRegistry.RegisterSource( trees );
				SpawnSourcePlaceholder( trees, "models/sbox_props/trees/oak/tree_oak_big_a.vmdl", Vector3.One );

				var quarry = new ResourceSource( "quarry_east",
					ItemType.Stone,
					center + new Vector3( offset, 0, 0 ),
					totalYield: 800, yieldPerGather: 15, radius: 100f,
					requiredCapability: NpcCapability.GatherStone,
					requiredTool: ItemType.Pickaxe );
				ResourceRegistry.RegisterSource( quarry );
				SpawnSourcePlaceholder( quarry, "models/castle_kit/rocks-large.vmdl", Vector3.One );

				var mine = new ResourceSource( "mine_west",
					ItemType.Ore,
					center + new Vector3( -offset, 0, 0 ),
					totalYield: 300, yieldPerGather: 5, radius: 100f,
					requiredCapability: NpcCapability.GatherOre,
					requiredTool: ItemType.Pickaxe );
				ResourceRegistry.RegisterSource( mine );
				SpawnSourcePlaceholder( mine, "models/props/rock_scatter/rock_scatter_pile_02.vmdl", Vector3.One );

				var clayDeposit = new ResourceSource( "clay_south",
					ItemType.Clay,
					center + new Vector3( 0, offset, 0 ),
					totalYield: 500, yieldPerGather: 10, radius: 100f,
					requiredCapability: NpcCapability.GatherClay,
					requiredTool: ItemType.Shovel );
				ResourceRegistry.RegisterSource( clayDeposit );
				SpawnSourcePlaceholder( clayDeposit, "models/props/dirt_pile/dirt_pile_01.vmdl", Vector3.One );

				var strawField = new ResourceSource( "straw_southeast",
					ItemType.Straw,
					center + new Vector3( offset * 0.7f, offset * 0.7f, 0 ),
					totalYield: 300, yieldPerGather: 10, radius: 100f,
					requiredCapability: NpcCapability.GatherClay,
					requiredTool: ItemType.Spade );
				ResourceRegistry.RegisterSource( strawField );
				SpawnSourcePlaceholder( strawField, "models/sbox_props/nature/tallgrass/tallgrass_c.vmdl", new Vector3( 3f, 3f, 3f ) );

				synthetic = 5;
			}

			// ── Central stockyard (real LuteInventory) ──
			var stockyard = CreateStockpile( "stockyard_0",
				center + new Vector3( -300, -300, 0 ),
				new Vector3( 200, 200, 100 ),
				capacity: 2000 );
			ResourceRegistry.RegisterStockpile( stockyard );

			// ── Brick staging stockpile near the village ──
			var brickStaging = CreateStockpile( "brick_staging_0",
				center + new Vector3( 800, 0, 0 ),
				new Vector3( 150, 150, 80 ),
				capacity: 500 );
			ResourceRegistry.RegisterStockpile( brickStaging );

			// ── Emergency bootstrap supplies (into the real inventory) ──
			// Per PR #6 §17: enough to avoid chicken-and-egg deadlock.
			stockyard.Deposit( ItemType.Brick, 200 );
			stockyard.Deposit( ItemType.Plank, 100 );
			stockyard.Deposit( ItemType.Timber, 50 );

			Log.Info( $"Lute: ResourceBootstrap initialized — {discovered} discovered + {synthetic} synthetic sources, {ResourceRegistry.AllStockpiles().Count} stockpiles." );

			// Discover all CraftingBench components in the scene as workstations.
			WorkstationRegistry.DiscoverAll();
		}

		/// <summary>
		/// Map a <see cref="NodeType"/> to the capability required to
		/// gather from it. Keeps the capability gate on the registry
		/// side in sync with the physical node's tool requirement.
		/// </summary>
		static NpcCapability CapabilityForNode( NodeType node ) => node switch
		{
			NodeType.ClayDeposit  => NpcCapability.GatherClay,
			NodeType.StrawField   => NpcCapability.GatherClay,
			NodeType.StoneQuarry  => NpcCapability.GatherStone,
			NodeType.OreVein      => NpcCapability.GatherOre,
			NodeType.Tree         => NpcCapability.GatherWood,
			NodeType.WaterSource  => NpcCapability.None,
			_ => NpcCapability.None,
		};

		/// <summary>
		/// Create a stockpile backed by a real <see cref="LuteInventory"/>
		/// on a spawned GameObject. The inventory component is what holds
		/// the material — the Stockpile is the registry-facing wrapper.
		/// Uses a crate model instead of a tinted primitive box.
		/// </summary>
		static Stockpile CreateStockpile( string id, Vector3 pos, Vector3 halfExtents,
			int capacity )
		{
			var go = new GameObject();
			go.Name = $"Stockpile_{id}";
			go.WorldPosition = pos;

			var mr = go.Components.Create<ModelRenderer>();
			mr.Model = Model.Load( "models/citizen_props/crate01.vmdl" );

			var inv = go.Components.Create<LuteInventory>();

			var pile = new Stockpile( id, pos, halfExtents, capacity, inv )
			{
				VisualGo = go,
			};
			return pile;
		}

		/// <summary>
		/// Spawn a real model as the visual marker for a resource source
		/// (tree, rock pile, dirt pile, grass clump, etc.) instead of a
		/// tinted primitive box.
		/// </summary>
		static void SpawnSourcePlaceholder( ResourceSource source, string modelPath, Vector3 scale )
		{
			var go = new GameObject();
			go.Name = $"ResourceSource_{source.Id}";
			go.WorldPosition = source.Position;
			go.WorldScale = scale;

			var mr = go.Components.Create<ModelRenderer>();
			mr.Model = Model.Load( modelPath );

			source.VisualGo = go;
		}
	}
}
