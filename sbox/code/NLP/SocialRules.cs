using System;
using System.Collections.Generic;
using Sandbox;
using Lute.Building;

namespace Lute.NLP
{
	/// <summary>
	/// Deterministic social rules engine. Given an incoming
	/// <see cref="Intent"/> and the receiver's <see cref="BeliefModel"/>,
	/// decides what <see cref="Intent"/> to respond with (if any).
	///
	/// Rules are simple and deterministic — no fuzzy logic, no
	/// probability. The same (intent, beliefs) pair always produces
	/// the same response. Rules are organized by intent type:
	///
	/// - Greet → Greet (reciprocate)
	/// - Farewell → Farewell (reciprocate)
	/// - Request → Accept if trust >= threshold, else Reject
	/// - Offer → Accept if it helps current goal, else Acknowledge
	/// - Claim → Acknowledge + update beliefs
	/// - Release → Acknowledge + update beliefs
	/// - Warn → Acknowledge + adjust trust up (they warned us)
	/// - Inform → Acknowledge + update beliefs
	/// - Ask → Inform if we know, else Acknowledge
	/// - Volunteer → Assign if we have work, else Acknowledge
	/// - Assign → Accept if trust high, else Decline
	/// - Report → Acknowledge + adjust trust based on report
	/// - Thank → Acknowledge (modest)
	/// - Apologize → Acknowledge + adjust trust up (they apologized)
	/// - Propose → Counter if trust high, else Reject
	/// - Counter → Accept if reasonable, else Counter or Reject
	///
	/// Trust thresholds:
	/// - Accept request: trust >= 10
	/// - Accept assignment: trust >= 20
	/// - Accept proposal: trust >= 30
	/// - Accept counter: trust >= 15
	/// </summary>
	public static class SocialRules
	{
		// ── Trust thresholds ──
		const int TrustAcceptRequest = 10;
		const int TrustAcceptAssign = 20;
		const int TrustAcceptPropose = 30;
		const int TrustAcceptCounter = 15;

		/// <summary>
		/// Evaluate an incoming intent against the receiver's beliefs and
		/// produce a response intent (or null to stay silent).
		/// </summary>
		public static Intent Evaluate( Intent incoming, BeliefModel beliefs )
		{
			if ( incoming == null || beliefs == null )
				return null;

			// Record the interaction in memory
			beliefs.RecordMemory( incoming.Sender, incoming, wasInitiator: false );

			// Update beliefs based on what we heard
			UpdateBeliefsFromIncoming( incoming, beliefs );

			// Decide response based on intent type
			Intent response = incoming.Type switch
			{
				IntentType.Greet => HandleGreet( incoming, beliefs ),
				IntentType.Farewell => HandleFarewell( incoming, beliefs ),
				IntentType.Request => HandleRequest( incoming, beliefs ),
				IntentType.Offer => HandleOffer( incoming, beliefs ),
				IntentType.Accept => HandleAccept( incoming, beliefs ),
				IntentType.Reject => HandleReject( incoming, beliefs ),
				IntentType.Propose => HandlePropose( incoming, beliefs ),
				IntentType.Counter => HandleCounter( incoming, beliefs ),
				IntentType.Inform => HandleInform( incoming, beliefs ),
				IntentType.Ask => HandleAsk( incoming, beliefs ),
				IntentType.Warn => HandleWarn( incoming, beliefs ),
				IntentType.Thank => HandleThank( incoming, beliefs ),
				IntentType.Apologize => HandleApologize( incoming, beliefs ),
				IntentType.Claim => HandleClaim( incoming, beliefs ),
				IntentType.Release => HandleRelease( incoming, beliefs ),
				IntentType.Volunteer => HandleVolunteer( incoming, beliefs ),
				IntentType.Assign => HandleAssign( incoming, beliefs ),
				IntentType.Decline => HandleDecline( incoming, beliefs ),
				IntentType.Report => HandleReport( incoming, beliefs ),
				IntentType.Acknowledge => null, // no response needed
				_ => null,
			};

			// If we have a response, record it as outgoing
			if ( response != null )
			{
				response.Sender = beliefs.SelfName;
				response.Target = incoming.Sender;
				response.Timestamp = SpatialBlackboard.CurrentTime;
				beliefs.RecordMemory( incoming.Sender, response, wasInitiator: true );
			}

			return response;
		}

		/// <summary>
		/// Update beliefs based on the incoming intent, before deciding
		/// a response. This is how NPCs learn about the world from what
		/// others say.
		/// </summary>
		static void UpdateBeliefsFromIncoming( Intent incoming, BeliefModel beliefs )
		{
			switch ( incoming.Type )
			{
				case IntentType.Claim:
					// Someone is claiming a site/resource — record it
					if ( !string.IsNullOrEmpty( incoming.Subject ) )
						beliefs.SetSiteClaim( incoming.Subject, incoming.Sender );
					break;

				case IntentType.Release:
					// Someone is releasing a site/resource — mark available
					if ( !string.IsNullOrEmpty( incoming.Subject ) )
						beliefs.SetSiteClaim( incoming.Subject, "" );
					break;

				case IntentType.Inform:
					// Information about a site being claimed/released
					if ( incoming.Topic == IntentTopic.Site && incoming.Parameters.TryGetValue( "claimed_by", out var claimer ) )
						beliefs.SetSiteClaim( incoming.Subject, claimer );
					break;

				case IntentType.Warn:
					// Someone warned us — they're looking out for us, trust up
					beliefs.AdjustTrust( incoming.Sender, +2 );
					break;

				case IntentType.Apologize:
					// Apology — small trust boost
					beliefs.AdjustTrust( incoming.Sender, +1 );
					break;

				case IntentType.Thank:
					// They're thanking us — we did something good
					beliefs.AdjustTrust( incoming.Sender, +1 );
					break;

				case IntentType.Report:
					// They reported completing something — trust up
					beliefs.AdjustTrust( incoming.Sender, +1 );
					break;
			}
		}

		// ── Individual intent handlers ──

		static Intent HandleGreet( Intent incoming, BeliefModel beliefs )
		{
			return Intent.Simple( IntentType.Greet, IntentTopic.None, "", beliefs.SelfName, incoming.Sender );
		}

		static Intent HandleFarewell( Intent incoming, BeliefModel beliefs )
		{
			return Intent.Simple( IntentType.Farewell, IntentTopic.None, "", beliefs.SelfName, incoming.Sender );
		}

		static Intent HandleRequest( Intent incoming, BeliefModel beliefs )
		{
			int trust = beliefs.GetTrust( incoming.Sender );
			if ( trust >= TrustAcceptRequest )
			{
				// We trust them enough — accept
				beliefs.AdjustTrust( incoming.Sender, +1 );
				return Intent.Simple( IntentType.Accept, incoming.Topic, incoming.Subject, beliefs.SelfName, incoming.Sender );
			}

			// Not enough trust — reject politely
			return Intent.Simple( IntentType.Reject, incoming.Topic, incoming.Subject, beliefs.SelfName, incoming.Sender );
		}

		static Intent HandleOffer( Intent incoming, BeliefModel beliefs )
		{
			// Accept offers if they relate to our current goal
			bool helpsGoal = incoming.Topic switch
			{
				IntentTopic.Material => beliefs.CurrentGoal == "build",
				IntentTopic.Task => beliefs.CurrentGoal == "build",
				IntentTopic.Site => beliefs.CurrentGoal == "build",
				_ => false,
			};

			if ( helpsGoal )
			{
				beliefs.AdjustTrust( incoming.Sender, +1 );
				return Intent.Simple( IntentType.Accept, incoming.Topic, incoming.Subject, beliefs.SelfName, incoming.Sender );
			}

			return Intent.Simple( IntentType.Acknowledge, incoming.Topic, incoming.Subject, beliefs.SelfName, incoming.Sender );
		}

		static Intent HandleAccept( Intent incoming, BeliefModel beliefs )
		{
			// They accepted our proposal — trust up, acknowledge
			beliefs.AdjustTrust( incoming.Sender, +1 );
			return null; // no response needed — they accepted, we're done
		}

		static Intent HandleReject( Intent incoming, BeliefModel beliefs )
		{
			// They rejected — trust down slightly
			beliefs.AdjustTrust( incoming.Sender, -1 );
			return null; // no response needed
		}

		static Intent HandlePropose( Intent incoming, BeliefModel beliefs )
		{
			int trust = beliefs.GetTrust( incoming.Sender );
			if ( trust >= TrustAcceptPropose )
			{
				// High trust — accept the proposal
				beliefs.AdjustTrust( incoming.Sender, +1 );
				return Intent.Simple( IntentType.Accept, incoming.Topic, incoming.Subject, beliefs.SelfName, incoming.Sender );
			}

			if ( trust >= TrustAcceptCounter )
			{
				// Medium trust — counter-propose
				return Intent.Simple( IntentType.Counter, incoming.Topic, incoming.Subject, beliefs.SelfName, incoming.Sender );
			}

			// Low trust — reject
			return Intent.Simple( IntentType.Reject, incoming.Topic, incoming.Subject, beliefs.SelfName, incoming.Sender );
		}

		static Intent HandleCounter( Intent incoming, BeliefModel beliefs )
		{
			int trust = beliefs.GetTrust( incoming.Sender );
			if ( trust >= TrustAcceptCounter )
			{
				beliefs.AdjustTrust( incoming.Sender, +1 );
				return Intent.Simple( IntentType.Accept, incoming.Topic, incoming.Subject, beliefs.SelfName, incoming.Sender );
			}

			return Intent.Simple( IntentType.Reject, incoming.Topic, incoming.Subject, beliefs.SelfName, incoming.Sender );
		}

		static Intent HandleInform( Intent incoming, BeliefModel beliefs )
		{
			// Acknowledge the information
			return Intent.Simple( IntentType.Acknowledge, incoming.Topic, incoming.Subject, beliefs.SelfName, incoming.Sender );
		}

		static Intent HandleAsk( Intent incoming, BeliefModel beliefs )
		{
			// If we know about the subject, inform. Otherwise acknowledge.
			if ( !string.IsNullOrEmpty( incoming.Subject ) && beliefs.Resources.ContainsKey( incoming.Subject ) )
			{
				var r = beliefs.Resources[incoming.Subject];
				return new Intent
				{
					Type = IntentType.Inform,
					Topic = incoming.Topic,
					Subject = incoming.Subject,
					Sender = beliefs.SelfName,
					Target = incoming.Sender,
					Parameters = new Dictionary<string, string>
					{
						["available"] = r.IsAvailable.ToString(),
						["claimed_by"] = r.ClaimedBy,
					},
				};
			}

			return Intent.Simple( IntentType.Acknowledge, incoming.Topic, incoming.Subject, beliefs.SelfName, incoming.Sender );
		}

		static Intent HandleWarn( Intent incoming, BeliefModel beliefs )
		{
			// Acknowledge the warning
			return Intent.Simple( IntentType.Acknowledge, incoming.Topic, incoming.Subject, beliefs.SelfName, incoming.Sender );
		}

		static Intent HandleThank( Intent incoming, BeliefModel beliefs )
		{
			// Modest acknowledgment
			return Intent.Simple( IntentType.Acknowledge, IntentTopic.None, "", beliefs.SelfName, incoming.Sender );
		}

		static Intent HandleApologize( Intent incoming, BeliefModel beliefs )
		{
			// Accept the apology
			return Intent.Simple( IntentType.Acknowledge, IntentTopic.None, "", beliefs.SelfName, incoming.Sender );
		}

		static Intent HandleClaim( Intent incoming, BeliefModel beliefs )
		{
			// Acknowledge their claim — we've already updated beliefs
			return Intent.Simple( IntentType.Acknowledge, incoming.Topic, incoming.Subject, beliefs.SelfName, incoming.Sender );
		}

		static Intent HandleRelease( Intent incoming, BeliefModel beliefs )
		{
			// Acknowledge the release
			return Intent.Simple( IntentType.Acknowledge, incoming.Topic, incoming.Subject, beliefs.SelfName, incoming.Sender );
		}

		static Intent HandleVolunteer( Intent incoming, BeliefModel beliefs )
		{
			// If we have work to assign, assign it
			if ( beliefs.CurrentGoal == "build" || beliefs.CurrentGoal == "coordinate" )
			{
				beliefs.AdjustTrust( incoming.Sender, +2 );
				return Intent.Simple( IntentType.Assign, incoming.Topic, incoming.Subject, beliefs.SelfName, incoming.Sender );
			}

			return Intent.Simple( IntentType.Acknowledge, incoming.Topic, incoming.Subject, beliefs.SelfName, incoming.Sender );
		}

		static Intent HandleAssign( Intent incoming, BeliefModel beliefs )
		{
			int trust = beliefs.GetTrust( incoming.Sender );
			if ( trust >= TrustAcceptAssign )
			{
				beliefs.AdjustTrust( incoming.Sender, +1 );
				return Intent.Simple( IntentType.Accept, incoming.Topic, incoming.Subject, beliefs.SelfName, incoming.Sender );
			}

			return Intent.Simple( IntentType.Decline, incoming.Topic, incoming.Subject, beliefs.SelfName, incoming.Sender );
		}

		static Intent HandleDecline( Intent incoming, BeliefModel beliefs )
		{
			// They declined — slight trust down
			beliefs.AdjustTrust( incoming.Sender, -1 );
			return null;
		}

		static Intent HandleReport( Intent incoming, BeliefModel beliefs )
		{
			// Acknowledge the report
			return Intent.Simple( IntentType.Acknowledge, incoming.Topic, incoming.Subject, beliefs.SelfName, incoming.Sender );
		}
	}
}
