using System;
using System.Collections.Generic;
using Sandbox;
using Lute.Building;

namespace Lute.NLP
{
	/// <summary>
	/// Deterministic social rules engine. Given an incoming
	/// <see cref="Intent"/> and the receiver's <see cref="BeliefModel"/>,
	/// decides what <see cref="ResponseDecision"/> to make.
	///
	/// The decision can be:
	/// - Speak: produce a verbal response (only when communication is useful)
	/// - Act: take a physical action (move, deliver, build)
	/// - Ignore: the message doesn't warrant a response
	/// - Defer: acknowledge but delay
	/// - AskForClarification: the message was ambiguous
	///
	/// Key principle (per professor feedback): NPCs need a reason to speak.
	/// The decision is NOT "did I receive a message?" → "respond". It's:
	///   Did this message change anything relevant to me?
	///     → Does responding advance one of my goals?
	///       → Can I act instead of talk?
	///         → ACT
	/// Only talk when communication is actually useful.
	///
	/// Trust thresholds:
	/// - Accept request: trust >= 10
	/// - Accept assignment: trust >= 20
	/// - Accept proposal: trust >= 30
	/// - Accept counter: trust >= 15
	/// </summary>
	public static class SocialRules
	{
		const int TrustAcceptRequest = 10;
		const int TrustAcceptAssign = 20;
		const int TrustAcceptPropose = 30;
		const int TrustAcceptCounter = 15;

		/// <summary>
		/// Evaluate an incoming intent against the receiver's beliefs and
		/// produce a <see cref="ResponseDecision"/>.
		/// </summary>
		public static ResponseDecision Evaluate( Intent incoming, BeliefModel beliefs )
		{
			if ( incoming == null || beliefs == null )
				return new ResponseDecision { Action = DecisionAction.Ignore, Reason = "null input" };

			// Record the interaction in memory
			beliefs.RecordMemory( incoming.Sender, incoming, wasInitiator: false );

			// Update beliefs based on what we heard
			UpdateBeliefsFromIncoming( incoming, beliefs );

			// Decide response based on intent type
			var decision = incoming.Type switch
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
				IntentType.Acknowledge => HandleAcknowledge( incoming, beliefs ),

				// Extended construction coordination intents
				IntentType.RequestHelp => HandleRequest( incoming, beliefs ),
				IntentType.OfferHelp => HandleOffer( incoming, beliefs ),
				IntentType.AcceptHelp => HandleAccept( incoming, beliefs ),
				IntentType.DeclineHelp => HandleReject( incoming, beliefs ),
				IntentType.ReportProblem => HandleWarn( incoming, beliefs ),
				IntentType.ReportCompletion => HandleReport( incoming, beliefs ),
				IntentType.ClaimResource => HandleClaim( incoming, beliefs ),
				IntentType.ReleaseResource => HandleRelease( incoming, beliefs ),
				IntentType.RequestResource => HandleRequest( incoming, beliefs ),
				IntentType.OfferResource => HandleOffer( incoming, beliefs ),
				IntentType.RequestTask => HandleVolunteer( incoming, beliefs ),
				IntentType.OfferTask => HandleAssign( incoming, beliefs ),
				IntentType.AssignTask => HandleAssign( incoming, beliefs ),
				IntentType.AcceptTask => HandleAccept( incoming, beliefs ),
				IntentType.ReportLocation => HandleInform( incoming, beliefs ),
				IntentType.ReportAvailability => HandleVolunteer( incoming, beliefs ),
				IntentType.Agree => HandleAccept( incoming, beliefs ),
				IntentType.Disagree => HandleReject( incoming, beliefs ),

				_ => new ResponseDecision { Action = DecisionAction.Ignore, Reason = "unknown intent type" },
			};

			// If we have a response intent, set sender/target
			if ( decision.ResponseIntent != null )
			{
				decision.ResponseIntent.Sender = beliefs.SelfName;
				decision.ResponseIntent.Target = incoming.Sender;
				decision.ResponseIntent.Timestamp = SpatialBlackboard.CurrentTime;
				beliefs.RecordMemory( incoming.Sender, decision.ResponseIntent, wasInitiator: true );
			}

			return decision;
		}

		/// <summary>
		/// Update beliefs based on the incoming intent, before deciding
		/// a response.
		/// </summary>
		static void UpdateBeliefsFromIncoming( Intent incoming, BeliefModel beliefs )
		{
			switch ( incoming.Type )
			{
				case IntentType.Claim:
					if ( !string.IsNullOrEmpty( incoming.Subject ) )
						beliefs.SetSiteClaim( incoming.Subject, incoming.Sender );
					break;

				case IntentType.Release:
					if ( !string.IsNullOrEmpty( incoming.Subject ) )
						beliefs.SetSiteClaim( incoming.Subject, "" );
					break;

				case IntentType.Inform:
					if ( incoming.Topic == IntentTopic.Site && incoming.Parameters.TryGetValue( "claimed_by", out var claimer ) )
						beliefs.SetSiteClaim( incoming.Subject, claimer );
					break;

				case IntentType.Warn:
					beliefs.AdjustTrust( incoming.Sender, +2 );
					break;

				case IntentType.Apologize:
					beliefs.AdjustTrust( incoming.Sender, +1 );
					break;

				case IntentType.Thank:
					beliefs.AdjustTrust( incoming.Sender, +1 );
					break;

				case IntentType.Report:
					beliefs.AdjustTrust( incoming.Sender, +1 );
					break;
			}
		}

		// ── Individual intent handlers ──
		// Each returns a ResponseDecision. The key change from the old
		// system: most messages are Ignored unless they're relevant to
		// the NPC's goals. NPCs don't acknowledge everything.

		static ResponseDecision HandleGreet( Intent incoming, BeliefModel beliefs )
		{
			// Greetings are social — reciprocate, but only if we haven't
			// recently greeted this NPC (avoid greeting loops).
			var recent = beliefs.GetRecentWith( incoming.Sender, 3 );
			foreach ( var m in recent )
			{
				if ( m.IntentType == IntentType.Greet )
					return new ResponseDecision { Action = DecisionAction.Ignore, Reason = "already greeted recently" };
			}

			return new ResponseDecision
			{
				Action = DecisionAction.Speak,
				ResponseIntent = Intent.Simple( IntentType.Greet, IntentTopic.None, "", beliefs.SelfName, incoming.Sender ),
				Reason = "reciprocate greeting",
			};
		}

		static ResponseDecision HandleFarewell( Intent incoming, BeliefModel beliefs )
		{
			return new ResponseDecision
			{
				Action = DecisionAction.Speak,
				ResponseIntent = Intent.Simple( IntentType.Farewell, IntentTopic.None, "", beliefs.SelfName, incoming.Sender ),
				Reason = "reciprocate farewell",
			};
		}

		static ResponseDecision HandleRequest( Intent incoming, BeliefModel beliefs )
		{
			int trust = beliefs.GetTrust( incoming.Sender );

			// Can we fulfill this request? Check if we have the resource.
			bool canFulfill = incoming.Topic switch
			{
				IntentTopic.Material => beliefs.CurrentGoal != "build" || HasSpareMaterial( beliefs, incoming.Subject ),
				IntentTopic.Site => beliefs.IsSiteAvailable( incoming.Subject ),
				IntentTopic.Task => true, // always consider tasks
				_ => false,
			};

			if ( trust >= TrustAcceptRequest && canFulfill )
			{
				beliefs.AdjustTrust( incoming.Sender, +1 );

				// If it's a material request and we can spare it, ACT (deliver)
				// rather than just speaking.
				if ( incoming.Topic == IntentTopic.Material )
				{
					return new ResponseDecision
					{
						Action = DecisionAction.SpeakAndAct,
						ResponseIntent = Intent.Simple( IntentType.Accept, incoming.Topic, incoming.Subject, beliefs.SelfName, incoming.Sender ),
						WorldAction = $"deliver:{incoming.Subject}",
						Reason = $"trusted ({trust}), can fulfill — will deliver",
					};
				}

				return new ResponseDecision
				{
					Action = DecisionAction.Speak,
					ResponseIntent = Intent.Simple( IntentType.Accept, incoming.Topic, incoming.Subject, beliefs.SelfName, incoming.Sender ),
					Reason = $"trusted ({trust} >= {TrustAcceptRequest})",
				};
			}

			if ( !canFulfill )
			{
				return new ResponseDecision
				{
					Action = DecisionAction.Speak,
					ResponseIntent = Intent.Simple( IntentType.Reject, incoming.Topic, incoming.Subject, beliefs.SelfName, incoming.Sender ),
					Reason = "cannot fulfill request",
				};
			}

			// Not enough trust — reject
			return new ResponseDecision
			{
				Action = DecisionAction.Speak,
				ResponseIntent = Intent.Simple( IntentType.Reject, incoming.Topic, incoming.Subject, beliefs.SelfName, incoming.Sender ),
				Reason = $"trust too low ({trust} < {TrustAcceptRequest})",
			};
		}

		static ResponseDecision HandleOffer( Intent incoming, BeliefModel beliefs )
		{
			// Accept offers only if they help our current goal
			bool helpsGoal = incoming.Topic switch
			{
				IntentTopic.Material => beliefs.CurrentGoal == "build",
				IntentTopic.Task => beliefs.CurrentGoal == "build",
				IntentTopic.Site => beliefs.CurrentGoal == "build",
				_ => false,
			};

			if ( !helpsGoal )
				return new ResponseDecision { Action = DecisionAction.Ignore, Reason = "offer not relevant to current goal" };

			beliefs.AdjustTrust( incoming.Sender, +1 );
			return new ResponseDecision
			{
				Action = DecisionAction.Speak,
				ResponseIntent = Intent.Simple( IntentType.Accept, incoming.Topic, incoming.Subject, beliefs.SelfName, incoming.Sender ),
				Reason = "offer helps current goal",
			};
		}

		static ResponseDecision HandleAccept( Intent incoming, BeliefModel beliefs )
		{
			// They accepted — trust up, no response needed
			beliefs.AdjustTrust( incoming.Sender, +1 );
			return new ResponseDecision { Action = DecisionAction.Ignore, Reason = "they accepted, no response needed" };
		}

		static ResponseDecision HandleReject( Intent incoming, BeliefModel beliefs )
		{
			// They rejected — trust down slightly, no response needed
			beliefs.AdjustTrust( incoming.Sender, -1 );
			return new ResponseDecision { Action = DecisionAction.Ignore, Reason = "they rejected, no response needed" };
		}

		static ResponseDecision HandlePropose( Intent incoming, BeliefModel beliefs )
		{
			int trust = beliefs.GetTrust( incoming.Sender );

			if ( trust >= TrustAcceptPropose )
			{
				beliefs.AdjustTrust( incoming.Sender, +1 );
				return new ResponseDecision
				{
					Action = DecisionAction.Speak,
					ResponseIntent = Intent.Simple( IntentType.Accept, incoming.Topic, incoming.Subject, beliefs.SelfName, incoming.Sender ),
					Reason = $"high trust ({trust} >= {TrustAcceptPropose})",
				};
			}

			if ( trust >= TrustAcceptCounter )
			{
				return new ResponseDecision
				{
					Action = DecisionAction.Speak,
					ResponseIntent = Intent.Simple( IntentType.Counter, incoming.Topic, incoming.Subject, beliefs.SelfName, incoming.Sender ),
					Reason = $"medium trust ({trust} >= {TrustAcceptCounter}) — counter",
				};
			}

			return new ResponseDecision
			{
				Action = DecisionAction.Speak,
				ResponseIntent = Intent.Simple( IntentType.Reject, incoming.Topic, incoming.Subject, beliefs.SelfName, incoming.Sender ),
				Reason = $"low trust ({trust} < {TrustAcceptCounter})",
			};
		}

		static ResponseDecision HandleCounter( Intent incoming, BeliefModel beliefs )
		{
			int trust = beliefs.GetTrust( incoming.Sender );

			if ( trust >= TrustAcceptCounter )
			{
				beliefs.AdjustTrust( incoming.Sender, +1 );
				return new ResponseDecision
				{
					Action = DecisionAction.Speak,
					ResponseIntent = Intent.Simple( IntentType.Accept, incoming.Topic, incoming.Subject, beliefs.SelfName, incoming.Sender ),
					Reason = $"trust sufficient for counter ({trust} >= {TrustAcceptCounter})",
				};
			}

			return new ResponseDecision
			{
				Action = DecisionAction.Speak,
				ResponseIntent = Intent.Simple( IntentType.Reject, incoming.Topic, incoming.Subject, beliefs.SelfName, incoming.Sender ),
				Reason = $"trust too low for counter ({trust} < {TrustAcceptCounter})",
			};
		}

		static ResponseDecision HandleInform( Intent incoming, BeliefModel beliefs )
		{
			// Information is useful — but we don't need to acknowledge
			// every piece of information. Only respond if it's relevant.
			bool relevant = incoming.Topic switch
			{
				IntentTopic.Site => beliefs.CurrentGoal == "build",
				IntentTopic.Safety => true, // safety info is always relevant
				IntentTopic.Task => beliefs.CurrentGoal == "build",
				_ => false,
			};

			if ( !relevant )
				return new ResponseDecision { Action = DecisionAction.Ignore, Reason = "information not relevant" };

			return new ResponseDecision
			{
				Action = DecisionAction.Speak,
				ResponseIntent = Intent.Simple( IntentType.Acknowledge, incoming.Topic, incoming.Subject, beliefs.SelfName, incoming.Sender ),
				Reason = "relevant information acknowledged",
			};
		}

		static ResponseDecision HandleAsk( Intent incoming, BeliefModel beliefs )
		{
			// If we know about the subject, inform. Otherwise stay silent.
			if ( !string.IsNullOrEmpty( incoming.Subject ) && beliefs.Resources.ContainsKey( incoming.Subject ) )
			{
				var r = beliefs.Resources[incoming.Subject];
				return new ResponseDecision
				{
					Action = DecisionAction.Speak,
					ResponseIntent = new Intent
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
					},
					Reason = "we know about this subject",
				};
			}

			// Don't know — stay silent rather than acknowledging uselessly
			return new ResponseDecision { Action = DecisionAction.Ignore, Reason = "we don't know about this subject" };
		}

		static ResponseDecision HandleWarn( Intent incoming, BeliefModel beliefs )
		{
			// Warnings are relevant — but we don't need to say "acknowledged".
			// Just update beliefs (already done) and stay silent, or
			// take action if it's a safety concern.
			if ( incoming.Topic == IntentTopic.Safety )
			{
				return new ResponseDecision
				{
					Action = DecisionAction.Act,
					WorldAction = $"check_safety:{incoming.Subject}",
					Reason = "safety warning — will check",
				};
			}

			return new ResponseDecision { Action = DecisionAction.Ignore, Reason = "warning noted, no action needed" };
		}

		static ResponseDecision HandleThank( Intent incoming, BeliefModel beliefs )
		{
			// Don't respond to thanks — it creates thank/acknowledge loops
			return new ResponseDecision { Action = DecisionAction.Ignore, Reason = "no response to thanks" };
		}

		static ResponseDecision HandleApologize( Intent incoming, BeliefModel beliefs )
		{
			// Don't respond to apologies — the trust adjustment is enough
			return new ResponseDecision { Action = DecisionAction.Ignore, Reason = "apology noted, no response needed" };
		}

		static ResponseDecision HandleClaim( Intent incoming, BeliefModel beliefs )
		{
			// Someone claimed a site — we've updated beliefs. No need to
			// acknowledge unless it conflicts with our plans.
			if ( beliefs.CurrentGoal == "build" && !beliefs.IsSiteAvailable( incoming.Subject ) )
			{
				// Check if we were planning to use this site
				if ( beliefs.Resources.TryGetValue( incoming.Subject, out var r ) && r.IsAvailable == false )
				{
					return new ResponseDecision
					{
						Action = DecisionAction.Speak,
						ResponseIntent = Intent.Simple( IntentType.Inform, incoming.Topic, incoming.Subject, beliefs.SelfName, incoming.Sender ),
						Reason = "site we wanted is now claimed",
					};
				}
			}

			return new ResponseDecision { Action = DecisionAction.Ignore, Reason = "claim noted, no conflict" };
		}

		static ResponseDecision HandleRelease( Intent incoming, BeliefModel beliefs )
		{
			// Someone released a site — if we need a site, this is relevant.
			if ( beliefs.CurrentGoal == "build" )
			{
				return new ResponseDecision
				{
					Action = DecisionAction.Act,
					WorldAction = $"consider_site:{incoming.Subject}",
					Reason = "site released — may want to claim it",
				};
			}

			return new ResponseDecision { Action = DecisionAction.Ignore, Reason = "release noted, not looking for sites" };
		}

		static ResponseDecision HandleVolunteer( Intent incoming, BeliefModel beliefs )
		{
			// Someone volunteered for work — if we have work to assign
			if ( beliefs.CurrentGoal == "coordinate" || beliefs.CurrentGoal == "build" )
			{
				beliefs.AdjustTrust( incoming.Sender, +2 );
				return new ResponseDecision
				{
					Action = DecisionAction.Speak,
					ResponseIntent = Intent.Simple( IntentType.Assign, incoming.Topic, incoming.Subject, beliefs.SelfName, incoming.Sender ),
					Reason = "have work to assign",
				};
			}

			return new ResponseDecision { Action = DecisionAction.Ignore, Reason = "no work to assign" };
		}

		static ResponseDecision HandleAssign( Intent incoming, BeliefModel beliefs )
		{
			int trust = beliefs.GetTrust( incoming.Sender );

			if ( trust >= TrustAcceptAssign )
			{
				beliefs.AdjustTrust( incoming.Sender, +1 );
				return new ResponseDecision
				{
					Action = DecisionAction.SpeakAndAct,
					ResponseIntent = Intent.Simple( IntentType.Accept, incoming.Topic, incoming.Subject, beliefs.SelfName, incoming.Sender ),
					WorldAction = $"accept_task:{incoming.Subject}",
					Reason = $"trusted assignment ({trust} >= {TrustAcceptAssign})",
				};
			}

			return new ResponseDecision
			{
				Action = DecisionAction.Speak,
				ResponseIntent = Intent.Simple( IntentType.Decline, incoming.Topic, incoming.Subject, beliefs.SelfName, incoming.Sender ),
				Reason = $"trust too low for assignment ({trust} < {TrustAcceptAssign})",
			};
		}

		static ResponseDecision HandleDecline( Intent incoming, BeliefModel beliefs )
		{
			beliefs.AdjustTrust( incoming.Sender, -1 );
			return new ResponseDecision { Action = DecisionAction.Ignore, Reason = "they declined, no response needed" };
		}

		static ResponseDecision HandleReport( Intent incoming, BeliefModel beliefs )
		{
			// Reports are useful — but we don't need to acknowledge every one.
			// Only respond if it affects our plans.
			if ( incoming.Topic == IntentTopic.Task && beliefs.CurrentGoal == "coordinate" )
			{
				return new ResponseDecision
				{
					Action = DecisionAction.Speak,
					ResponseIntent = Intent.Simple( IntentType.Acknowledge, incoming.Topic, incoming.Subject, beliefs.SelfName, incoming.Sender ),
					Reason = "task report relevant to coordination",
				};
			}

			return new ResponseDecision { Action = DecisionAction.Ignore, Reason = "report noted, no response needed" };
		}

		static ResponseDecision HandleAcknowledge( Intent incoming, BeliefModel beliefs )
		{
			// Never respond to acknowledgments — it creates acknowledge loops
			return new ResponseDecision { Action = DecisionAction.Ignore, Reason = "no response to acknowledgments" };
		}

		/// <summary>
		/// Check if this NPC has spare material of the given type.
		/// In the full system, this checks the NPC's inventory. For now,
		/// builders always have spare material if they're not building.
		/// </summary>
		static bool HasSpareMaterial( BeliefModel beliefs, string material )
		{
			// Simplified: if we're building, we need our materials.
			// If we're idle or coordinating, we can spare.
			return beliefs.CurrentGoal != "build" || beliefs.CurrentGoal == "coordinate";
		}
	}
}
