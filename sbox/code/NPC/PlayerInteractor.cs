using System.Collections.Generic;

namespace Lute.Npc
{
	/// <summary>
	/// Player-side interaction controller. Attach to the player GameObject
	/// (alongside <c>PlayerController</c>). Each tick, scans the scene for
	/// <see cref="Interactable"/> components within range, picks the closest
	/// one as the current target, and invokes it when the player presses the
	/// "use" key (E).
	///
	/// This is NOT LLM-driven — it's a simple proximity + key-press system.
	/// The current target's prompt text is exposed via
	/// <see cref="CurrentPromptText"/> so a UI panel (e.g.
	/// <c>LuteInteractionPrompt.razor</c>) can render it.
	/// </summary>
	public sealed class PlayerInteractor : Component
	{
		/// <summary>
		/// Maximum scan range. Interactables beyond this distance are
		/// ignored entirely (cheaper than checking each one's own range
		/// first, then filtering). The effective range is the minimum of
		/// this and the interactable's own <see cref="Interactable.Range"/>.
		/// </summary>
		[Property] public float MaxScanRange { get; set; } = 300f;

		/// <summary>
		/// The closest in-range <see cref="Interactable"/> this tick, or
		/// null if none is in range. Updated every <see cref="OnUpdate"/>.
		/// </summary>
		public Interactable CurrentTarget { get; private set; }

		/// <summary>
		/// The prompt text for <see cref="CurrentTarget"/>, or empty if no
		/// target. UI panels bind to this.
		/// </summary>
		public string CurrentPromptText { get; private set; } = "";

		private float _logTimer;
		private bool _startupLogged;

		protected override void OnUpdate()
		{
			if ( !_startupLogged )
			{
				_startupLogged = true;
				var all = Scene.Components.GetAll<Interactable>();
				int count = 0;
				foreach ( var i in all ) count++;
				Log.Info( $"Lute: PlayerInteractor startup — found {count} Interactable components in scene." );
			}

			ScanForTargets();

			// Fire interaction on E press.
			if ( Input.Pressed( "use" ) && CurrentTarget.IsValid() )
			{
				CurrentTarget.Interact( GameObject );
				Log.Info( $"Lute: PlayerInteractor interacted with '{CurrentTarget.GameObject.Name}'." );
			}

			// Debug: press Flashlight (F) to auto-interact with the nearest
			// interactable regardless of range. Lets the agent verify the
			// interaction pipeline without teleporting the player. Only
			// fires when no target is in range (so it doesn't double-fire
			// when the player is actually next to an NPC).
			if ( Input.Pressed( "flashlight" ) && !CurrentTarget.IsValid() )
			{
				var nearest = FindNearestAnyRange();
				if ( nearest.IsValid() )
				{
					Log.Info( $"Lute: PlayerInteractor DEBUG — auto-interact with nearest '{nearest.GameObject.Name}' (bypassing range)." );
					nearest.Interact( GameObject );
				}
			}

			// Periodic log so the agent can verify the interactor is alive.
			_logTimer += Time.Delta;
			if ( _logTimer > 5f && CurrentTarget.IsValid() )
			{
				_logTimer = 0f;
				var dist = Vector3.DistanceBetween( WorldPosition, CurrentTarget.WorldPosition );
				Log.Info( $"Lute: PlayerInteractor target='{CurrentTarget.GameObject.Name}' dist={dist:F0} ({dist / 39.37f:F1} m) prompt='{CurrentPromptText}'." );
			}
		}

		void ScanForTargets()
		{
			CurrentTarget = null;
			CurrentPromptText = "";

			var bestDist = float.MaxValue;
			var playerPos = WorldPosition;

			foreach ( var interactable in Scene.Components.GetAll<Interactable>() )
			{
				if ( !interactable.Enabled || !interactable.IsAvailable )
					continue;

				var dist = Vector3.DistanceBetween( playerPos, interactable.WorldPosition );
				var effectiveRange = System.Math.Min( MaxScanRange, interactable.Range );

				if ( dist > effectiveRange )
					continue;

				if ( dist < bestDist )
				{
					bestDist = dist;
					CurrentTarget = interactable;
				}
			}

			if ( CurrentTarget.IsValid() )
				CurrentPromptText = CurrentTarget.GetPromptText();
		}

		/// <summary>
		/// Find the nearest available Interactable regardless of range.
		/// Used by the debug auto-interact key (noclip/V when no target is
		/// in range). Returns null if none exist.
		/// </summary>
		Interactable FindNearestAnyRange()
		{
			Interactable nearest = null;
			var bestDist = float.MaxValue;
			var playerPos = WorldPosition;

			foreach ( var interactable in Scene.Components.GetAll<Interactable>() )
			{
				if ( !interactable.Enabled || !interactable.IsAvailable )
					continue;

				var dist = Vector3.DistanceBetween( playerPos, interactable.WorldPosition );
				if ( dist < bestDist )
				{
					bestDist = dist;
					nearest = interactable;
				}
			}

			return nearest;
		}
	}
}
