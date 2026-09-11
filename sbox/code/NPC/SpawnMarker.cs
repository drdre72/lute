using System;
using System.Collections.Generic;

namespace Lute.Npc
{
	/// <summary>
	/// A general-purpose NPC spawn marker. Place this in the scene file
	/// (editor-placed) or spawn it from code (e.g. LuteWorld.Build) to mark
	/// where an NPC should appear. The <see cref="NPCSpawner"/> scans for
	/// all SpawnMarkers in the scene at world-gen and spawns the right NPC
	/// body + controller at each one.
	///
	/// This is NOT builder-specific — <see cref="NpcType"/> selects which
	/// NPC factory runs. Today only "Builder" is implemented (spawns a
	/// citizen body + NPCBuilderController wired to a sibling NPCBuilder),
	/// but the marker system is extensible for future types (Vendor, Guard,
	/// QuestGiver, etc.) without changing the marker or spawner.
	/// </summary>
	public sealed class SpawnMarker : Component
	{
		/// <summary>
		/// Which NPC factory to run at this marker. The spawner looks this
		/// up in its factory table. Unknown types are logged and skipped.
		/// </summary>
		[Property] public string NpcType { get; set; } = "Builder";

		/// <summary>
		/// Optional name override for the spawned NPC GameObject. If empty,
		/// the spawner generates one from the marker's name + NpcType.
		/// </summary>
		[Property] public string NpcName { get; set; } = "";

		/// <summary>
		/// For builder NPCs: the wealth factor to pass to the NPCBuilder.
		/// Ignored by other NPC types. Kept here so a single marker can
		/// configure the NPC it spawns without a separate component.
		/// </summary>
		[Property, Group( "Builder" )] public float WealthFactor { get; set; } = 1.0f;

		/// <summary>
		/// For builder NPCs: the layout seed (0 = random). Ignored by other
		/// NPC types.
		/// </summary>
		[Property, Group( "Builder" )] public int LayoutSeed { get; set; } = 0;

		/// <summary>
		/// For builder NPCs: base layout dimensions in cells before wealth
		/// scaling. Ignored by other NPC types.
		/// </summary>
		[Property, Group( "Builder" )] public int BaseWidth { get; set; } = 4;
		[Property, Group( "Builder" )] public int BaseHeight { get; set; } = 4;

		/// <summary>
		/// For village builder NPCs: seed for the village layout (0 = random).
		/// Ignored by other NPC types.
		/// </summary>
		[Property, Group( "Village" )] public int VillageSeed { get; set; } = 0;

		/// <summary>
		/// For village builder NPCs: if true, clear any existing save and
		/// start a fresh village build. Ignored by other NPC types.
		/// </summary>
		[Property, Group( "Village" )] public bool FreshBuild { get; set; } = false;

		protected override void OnStart()
		{
			// Markers are invisible placeholders — disable rendering of the
			// marker itself. The spawner reads our position/config and
			// spawns the real NPC body elsewhere.
			GameObject.Enabled = false;
		}
	}
}
