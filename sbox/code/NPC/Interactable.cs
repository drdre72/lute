using System;

namespace Lute.Npc
{
	/// <summary>
	/// Component for anything the player can interact with by pressing
	/// the "use" key (E) while in range. Attach this to an NPC or object
	/// to make it interactable.
	///
	/// The <see cref="PlayerInteractor"/> on the player scans for nearby
	/// <see cref="Interactable"/> components each tick, tracks the closest
	/// one as the current target, and calls <see cref="Interact"/> when
	/// the player presses E while in range.
	///
	/// S&Box components are sealed by the engine's codegen, so this is a
	/// single component (not a base class). NPC-specific behavior is
	/// configured via <see cref="NpcMode"/> and the prompt/line properties
	/// rather than via subclassing.
	/// </summary>
	public sealed class Interactable : Component
	{
		/// <summary>
		/// Maximum distance (in world units) at which the player can
		/// interact with this object. The <see cref="PlayerInteractor"/>
		/// uses this to pick the closest in-range interactable.
		/// </summary>
		[Property] public float Range { get; set; } = 150f;

		/// <summary>
		/// The prompt text shown to the player when in range (e.g.
		/// "Press E to talk to Builder").
		/// </summary>
		[Property] public string PromptText { get; set; } = "Press E to interact";

		/// <summary>
		/// If true, the interactable is currently available. Set to false
		/// to disable interaction (e.g. an NPC that's dead or busy). The
		/// <see cref="PlayerInteractor"/> skips disabled interactables.
		/// </summary>
		[Property] public bool IsAvailable { get; set; } = true;

		/// <summary>
		/// If true, this interactable is an NPC that cycles through
		/// Talk / Trade / Quest modes on each interaction. When false,
		/// it's a simple one-shot interactable that just logs.
		/// </summary>
		[Property] public bool NpcMode { get; set; } = false;

		/// <summary>
		/// NPC display name shown in prompts and dialogue. Only used when
		/// <see cref="NpcMode"/> is true.
		/// </summary>
		[Property, Group( "NPC" )] public string DisplayName { get; set; } = "Builder";

		/// <summary>
		/// Which interaction mode the next E press will trigger. Cycles
		/// Talk → Trade → Quest → Talk on each interaction. Only used when
		/// <see cref="NpcMode"/> is true.
		/// </summary>
		public enum InteractionMode
		{
			Talk,
			Trade,
			Quest
		}

		[Property, Group( "NPC" )] public InteractionMode NextMode { get; set; } = InteractionMode.Talk;

		/// <summary>
		/// Babblespeak greeting line for Talk mode. Per PRD §1.4, NPC
		/// dialogue is overly complex English that the player progressively
		/// translates via dictionary pages. This is a static placeholder.
		/// </summary>
		[Property, Group( "NPC" )] public string GreetingLine { get; set; } =
			"Verily, the edifice upon which I labor doth require yet further material " +
			"acquisition ere its completion may be actualized.";

		/// <summary>
		/// Trade offer line for Trade mode. Placeholder for the full
		/// economy/trade UI (PRD §5.2).
		/// </summary>
		[Property, Group( "NPC" )] public string TradeLine { get; set; } =
			"I possess sundry materials of construction — prithee, what dost thou offer in exchange?";

		/// <summary>
		/// Quest offer line for Quest mode. Placeholder for the dynamic
		/// quest generation engine (PRD §5.2).
		/// </summary>
		[Property, Group( "NPC" )] public string QuestLine { get; set; } =
			"The tower's unending construction doth necessitate thy assistance — " +
			"fetch thou the requisite materials and thy labor shall be rewarded.";

		/// <summary>
		/// Invoked when the player presses E while this interactable is the
		/// current target. The <see cref="PlayerInteractor"/> passes the
		/// player's GameObject as the argument.
		/// </summary>
		public Action<GameObject> OnInteract { get; set; }

		/// <summary>
		/// Returns the prompt text to display.
		/// </summary>
		public string GetPromptText()
		{
			if ( !IsAvailable )
				return "";

			if ( !NpcMode )
				return PromptText;

			var action = NextMode switch
			{
				InteractionMode.Talk => "Talk to",
				InteractionMode.Trade => "Trade with",
				InteractionMode.Quest => "Ask quest from",
				_ => "Interact with",
			};
			return $"Press E to {action} {DisplayName}";
		}

		/// <summary>
		/// Called by <see cref="PlayerInteractor"/> when the player presses
		/// E while in range. Logs the interaction and fires
		/// <see cref="OnInteract"/>. In NPC mode, logs the appropriate
		/// babblespeak line and advances to the next mode.
		/// </summary>
		public void Interact( GameObject player )
		{
			if ( NpcMode )
			{
				var mode = NextMode;
				switch ( mode )
				{
					case InteractionMode.Talk:
						Log.Info( $"Lute: Interactable '{DisplayName}' TALK — {GreetingLine}" );
						break;
					case InteractionMode.Trade:
						Log.Info( $"Lute: Interactable '{DisplayName}' TRADE — {TradeLine}" );
						break;
					case InteractionMode.Quest:
						Log.Info( $"Lute: Interactable '{DisplayName}' QUEST — {QuestLine}" );
						break;
				}

				NextMode = mode switch
				{
					InteractionMode.Talk => InteractionMode.Trade,
					InteractionMode.Trade => InteractionMode.Quest,
					InteractionMode.Quest => InteractionMode.Talk,
					_ => InteractionMode.Talk,
				};
			}
			else
			{
				Log.Info( $"Lute: Interactable '{GameObject.Name}' interacted with by '{player?.Name ?? "null"}'." );
			}

			OnInteract?.Invoke( player );
		}
	}
}
