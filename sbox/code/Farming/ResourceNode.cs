namespace Lute.Farming;

using Lute.Items;

/// <summary>
/// Type of resource node. Determines what it yields and what tool is needed.
/// </summary>
public enum NodeType
{
	ClayDeposit,   // yields Clay — requires Shovel
	StrawField,    // yields Straw — requires Shovel (or hands)
	StoneQuarry,   // yields Stone — requires Pickaxe
	OreVein,       // yields Ore — requires Pickaxe
	Tree,          // yields Wood — requires Pickaxe (chopping)
	WaterSource,   // yields Water — no tool needed
}

/// <summary>
/// A resource node that farmers can gather from.
/// Has a simple gather animation trigger and respawn timer.
/// Respawn is fast for dev purposes (configurable).
/// </summary>
public sealed class ResourceNode : Component
{
	[Property] public NodeType Node { get; set; } = NodeType.ClayDeposit;

	/// <summary> Items yielded per gather action. </summary>
	[Property] public int YieldPerGather { get; set; } = 5;

	/// <summary> Respawn time in seconds (fast for dev). </summary>
	[Property] public float RespawnTime { get; set; } = 10f;

	/// <summary> Max gathers before depleted. </summary>
	[Property] public int MaxGathers { get; set; } = 20;

	/// <summary> Current gathers remaining. </summary>
	[Property] public int GathersRemaining { get; set; } = 20;

	/// <summary> Is this node currently available for gathering? </summary>
	public bool IsAvailable => GathersRemaining > 0 && !_respawning;

	private bool _respawning;
	private float _respawnTimer;

	/// <summary>
	/// Get the item type this node yields.
	/// </summary>
	public ItemType GetYieldType()
	{
		return Node switch
		{
			NodeType.ClayDeposit => ItemType.Clay,
			NodeType.StrawField => ItemType.Straw,
			NodeType.StoneQuarry => ItemType.Stone,
			NodeType.OreVein => ItemType.Ore,
			NodeType.Tree => ItemType.Wood,
			NodeType.WaterSource => ItemType.Water,
			_ => ItemType.Clay,
		};
	}

	/// <summary>
	/// Get the tool required to gather from this node.
	/// ItemType.Clay means no tool needed.
	/// </summary>
	public ItemType GetRequiredTool()
	{
		return Node switch
		{
			NodeType.ClayDeposit => ItemType.Shovel,
			NodeType.StrawField => ItemType.Shovel,
			NodeType.StoneQuarry => ItemType.Pickaxe,
			NodeType.OreVein => ItemType.Pickaxe,
			NodeType.Tree => ItemType.Pickaxe,
			NodeType.WaterSource => ItemType.Clay, // no tool needed — use "none"
			_ => ItemType.Clay,
		};
	}

	/// <summary>
	/// Gather from this node. Returns items gathered (0 if unavailable or wrong tool).
	/// </summary>
	public int Gather( LuteInventory inventory )
	{
		if ( !IsAvailable )
			return 0;

		var requiredTool = GetRequiredTool();
		if ( requiredTool != ItemType.Clay )
		{
			int toolSlot = inventory.FindTool( requiredTool );
			if ( toolSlot < 0 )
				return 0; // doesn't have the tool

			// Use the tool
			inventory.Slots[toolSlot].UseTool();
		}

		int yield = Math.Min( YieldPerGather, GathersRemaining );
		int leftover = inventory.AddItem( GetYieldType(), yield );

		GathersRemaining -= yield;

		// Trigger gather animation event
		OnGathered?.Invoke( this, yield );

		if ( GathersRemaining <= 0 )
		{
			_respawning = true;
			_respawnTimer = RespawnTime;
		}

		return yield - leftover;
	}

	/// <summary> Event fired when the node is gathered. </summary>
	public event Action<ResourceNode, int> OnGathered;

	protected override void OnUpdate()
	{
		if ( _respawning )
		{
			_respawnTimer -= Time.Delta;
			if ( _respawnTimer <= 0 )
			{
				GathersRemaining = MaxGathers;
				_respawning = false;
			}
		}
	}
}
