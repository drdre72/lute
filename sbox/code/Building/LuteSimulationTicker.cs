using Sandbox;

namespace Lute.Building
{
	/// <summary>
	/// Single authoritative simulation clock. Advances all global static
	/// systems (SpatialBlackboard, ConstructionEventBus, BuilderLivenessRegistry)
	/// exactly once per frame so simulation time is independent of the
	/// number of builder NPCs in the scene.
	///
	/// Without this, every VillageBuilderController.OnFixedUpdate() calls
	/// Update() on these shared static systems, so 4 builders advance the
	/// global clock ~4× per frame. This component replaces that pattern:
	/// it ticks once; NPCs publish their own positions but do NOT advance
	/// global time.
	///
	/// Add this component to the scene root (or any always-active GameObject).
	/// It auto-deduplicates: only one ticker may be active at a time.
	/// </summary>
	public sealed class LuteSimulationTicker : Component
	{
		static LuteSimulationTicker _active;

		protected override void OnStart()
		{
			// Only one ticker may drive global time. If another is already
			// active, this duplicate disables itself.
			if ( _active != null && _active.IsValid && _active != this )
			{
				Log.Warning( "Lute: LuteSimulationTicker — duplicate detected, disabling this instance." );
				Enabled = false;
				return;
			}
			_active = this;
			Log.Info( "Lute: LuteSimulationTicker started — single global simulation clock active." );
		}

		protected override void OnFixedUpdate()
		{
			// Advance all global static systems exactly once per frame.
			// NPCs call UpdatePosition() individually but must NOT call
			// Update() on these shared clocks.
			SpatialBlackboard.Update( Time.Delta );
			ConstructionEventBus.Update( Time.Delta );
			BuilderLivenessRegistry.TickAll( Time.Delta );
		}

		protected override void OnDestroy()
		{
			if ( _active == this )
				_active = null;
		}
	}
}
