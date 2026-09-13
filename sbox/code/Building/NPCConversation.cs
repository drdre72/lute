// LEGACY / RESERVED — DO NOT USE AT RUNTIME
//
// NPCConversation.cs has been removed from active runtime. It was a
// parallel implementation that competed with ConversationManager for
// the same NPC conversation state.
//
// NPC-to-NPC construction coordination now uses:
//     CommunicationBus + ConversationManager + WorldFactProvider
//
// Future player-to-NPC dialogue will use a separate PlayerDialogue
// system (not yet implemented). See:
//   - docs/proposals/AUTONOMOUS_TOWN_COOPERATION_TEST.md
//   - AGENTS.md (Three-Domain Separation rule)
//
// The original source is preserved at:
//   docs/history/NPCConversation.cs.legacy
//
// Do not resurrect this component. If you need NPC communication, use
// ConversationManager (NPC-NPC) or wait for PlayerDialogueManager
// (player-NPC, future).
#if false
namespace Lute.Building
{
	using Lute.NLP;

	// This class is intentionally not compiled. See file header.
	public sealed class NPCConversation : Component { }
}
#endif
