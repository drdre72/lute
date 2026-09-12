namespace Lute.Items;

/// <summary>
/// All item types in Lute. Each maps to a god-given tool or material.
/// </summary>
public enum ItemType
{
	// Raw materials (gathered by farmers)
	Clay,
	Straw,
	Stone,
	Ore,
	Wood,
	Water,

	// Crafted materials (made at benches)
	Brick,
	Concrete,
	Mortar,

	// Tools (crafted by smith, used by builders/farmers)
	Spade,      // builder tool — breaks after 300 bricks placed
	Pickaxe,    // farmer tool — mines stone/ore
	Shovel,     // farmer tool — digs clay/soil
	Trowel,     // builder tool — applies mortar/concrete

	// Containers
	StorageCrate, // 30 slots, 500 stack per slot, portable even when full
}

/// <summary>
/// Static metadata for each item type.
/// </summary>
public static class ItemDefs
{
	public const int MaxSlots = 30;
	public const int MaxStack = 500;

	public static int GetMaxStack( ItemType type )
	{
		// Tools and containers don't stack
		if ( IsTool( type ) || type == ItemType.StorageCrate )
			return 1;
		return MaxStack;
	}

	public static bool IsTool( ItemType type )
	{
		return type == ItemType.Spade || type == ItemType.Pickaxe ||
			   type == ItemType.Shovel || type == ItemType.Trowel;
	}

	public static bool IsRawMaterial( ItemType type )
	{
		return type == ItemType.Clay || type == ItemType.Straw ||
			   type == ItemType.Stone || type == ItemType.Ore ||
			   type == ItemType.Wood || type == ItemType.Water;
	}

	public static bool IsCraftedMaterial( ItemType type )
	{
		return type == ItemType.Brick || type == ItemType.Concrete || type == ItemType.Mortar;
	}

	public static string GetDisplayName( ItemType type )
	{
		return type switch
		{
			ItemType.Clay => "Clay",
			ItemType.Straw => "Straw",
			ItemType.Stone => "Stone",
			ItemType.Ore => "Iron Ore",
			ItemType.Wood => "Wood",
			ItemType.Water => "Water",
			ItemType.Brick => "Brick",
			ItemType.Concrete => "Concrete",
			ItemType.Mortar => "Mortar",
			ItemType.Spade => "Spade",
			ItemType.Pickaxe => "Pickaxe",
			ItemType.Shovel => "Shovel",
			ItemType.Trowel => "Trowel",
			ItemType.StorageCrate => "Storage Crate",
			_ => type.ToString(),
		};
	}
}
