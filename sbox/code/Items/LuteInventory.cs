namespace Lute.Items;

/// <summary>
/// A single inventory slot. Holds an item type and count.
/// Tools have durability (e.g., spade breaks after 300 uses).
/// </summary>
public struct InventorySlot
{
	public ItemType Type;
	public int Count;
	public float Durability;  // 1.0 = new, 0.0 = broken
	public int MaxDurability;  // max uses before breaking (e.g., 300 for spade)
	public int UsesRemaining;  // current uses left

	public bool IsEmpty => Count <= 0;
	public bool IsBroken => ItemDefs.IsTool( Type ) && UsesRemaining <= 0;

	public void Clear()
	{
		Type = ItemType.Clay; // default
		Count = 0;
		Durability = 0;
		MaxDurability = 0;
		UsesRemaining = 0;
	}

	public void Set( ItemType type, int count, int maxDurability = 0 )
	{
		Type = type;
		Count = count;
		MaxDurability = maxDurability;
		UsesRemaining = maxDurability;
		Durability = maxDurability > 0 ? 1.0f : 0;
	}

	/// <summary>
	/// Use a tool. Returns false if broken.
	/// </summary>
	public bool UseTool()
	{
		if ( !ItemDefs.IsTool( Type ) || UsesRemaining <= 0 )
			return false;
		UsesRemaining--;
		Durability = (float)UsesRemaining / MaxDurability;
		if ( UsesRemaining <= 0 )
		{
			Count = 0; // tool is gone
			return false;
		}
		return true;
	}
}

/// <summary>
/// Component-based inventory. 30 slots, 500 stack per slot.
/// Attach to any GameObject (player or NPC).
/// </summary>
public sealed class LuteInventory : Component
{
	[Property] public int SlotCount { get; set; } = ItemDefs.MaxSlots;

	public InventorySlot[] Slots { get; private set; }

	/// <summary>
	/// The 6-slot hotbar (toolbar). Indices into Slots[0..5].
	/// </summary>
	public int[] Hotbar { get; private set; } = { 0, 1, 2, 3, 4, 5 };

	/// <summary> Currently selected hotbar slot (0-5). </summary>
	[Property] public int SelectedHotbarSlot { get; set; } = 0;

	protected override void OnStart()
	{
		Slots = new InventorySlot[SlotCount];
	}

	/// <summary>
	/// Add items to the inventory. Returns the number that couldn't fit.
	/// </summary>
	public int AddItem( ItemType type, int count, int maxDurability = 0 )
	{
		int maxStack = ItemDefs.GetMaxStack( type );

		// First, try to stack into existing slots
		if ( maxStack > 1 )
		{
			for ( int i = 0; i < SlotCount && count > 0; i++ )
			{
				if ( Slots[i].Type == type && Slots[i].Count > 0 && Slots[i].Count < maxStack )
				{
					int space = maxStack - Slots[i].Count;
					int add = Math.Min( space, count );
					Slots[i].Count += add;
					count -= add;
				}
			}
		}

		// Then, fill empty slots
		for ( int i = 0; i < SlotCount && count > 0; i++ )
		{
			if ( Slots[i].IsEmpty )
			{
				int add = Math.Min( maxStack, count );
				Slots[i].Set( type, add, maxDurability );
				count -= add;
			}
		}

		return count; // remaining items that didn't fit
	}

	/// <summary>
	/// Remove items from the inventory. Returns true if successful.
	/// </summary>
	public bool RemoveItem( ItemType type, int count )
	{
		// Check if we have enough
		if ( CountItem( type ) < count )
			return false;

		for ( int i = 0; i < SlotCount && count > 0; i++ )
		{
			if ( Slots[i].Type == type && Slots[i].Count > 0 )
			{
				int remove = Math.Min( Slots[i].Count, count );
				Slots[i].Count -= remove;
				count -= remove;
				if ( Slots[i].Count <= 0 )
					Slots[i].Clear();
			}
		}
		return true;
	}

	/// <summary>
	/// Count how many of an item type are in the inventory.
	/// </summary>
	public int CountItem( ItemType type )
	{
		int total = 0;
		for ( int i = 0; i < SlotCount; i++ )
		{
			if ( Slots[i].Type == type && Slots[i].Count > 0 )
				total += Slots[i].Count;
		}
		return total;
	}

	/// <summary>
	/// Get the currently selected hotbar item.
	/// </summary>
	public InventorySlot GetSelectedHotbarItem()
	{
		if ( SelectedHotbarSlot < 0 || SelectedHotbarSlot >= Hotbar.Length )
			return default;
		int slotIdx = Hotbar[SelectedHotbarSlot];
		if ( slotIdx < 0 || slotIdx >= SlotCount )
			return default;
		return Slots[slotIdx];
	}

	/// <summary>
	/// Use the currently selected tool. Returns false if no tool or broken.
	/// </summary>
	public bool UseSelectedTool()
	{
		var item = GetSelectedHotbarItem();
		if ( !ItemDefs.IsTool( item.Type ) || item.IsBroken )
			return false;

		int slotIdx = Hotbar[SelectedHotbarSlot];
		return Slots[slotIdx].UseTool();
	}

	/// <summary>
	/// Check if the inventory has a specific tool type (not broken).
	/// </summary>
	public bool HasTool( ItemType toolType )
	{
		for ( int i = 0; i < SlotCount; i++ )
		{
			if ( Slots[i].Type == toolType && Slots[i].Count > 0 && !Slots[i].IsBroken )
				return true;
		}
		return false;
	}

	/// <summary>
	/// Find and return the slot index of a working tool.
	/// </summary>
	public int FindTool( ItemType toolType )
	{
		for ( int i = 0; i < SlotCount; i++ )
		{
			if ( Slots[i].Type == toolType && Slots[i].Count > 0 && !Slots[i].IsBroken )
				return i;
		}
		return -1;
	}

	/// <summary>
	/// Transfer all contents from another inventory. Returns items that didn't fit.
	/// </summary>
	public int TransferFrom( LuteInventory source )
	{
		int leftover = 0;
		for ( int i = 0; i < source.SlotCount; i++ )
		{
			if ( source.Slots[i].Count > 0 )
			{
				leftover += AddItem( source.Slots[i].Type, source.Slots[i].Count, source.Slots[i].MaxDurability );
				if ( leftover == 0 )
					source.Slots[i].Clear();
			}
		}
		return leftover;
	}
}
