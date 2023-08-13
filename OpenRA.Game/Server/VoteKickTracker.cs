#region Copyright & License Information
/*
 * Copyright (c) The OpenRA Developers and Contributors
 * This file is part of OpenRA, which is free software. It is made
 * available to you under the terms of the GNU General Public License
 * as published by the Free Software Foundation, either version 3 of
 * the License, or (at your option) any later version. For more
 * information, see COPYING.
 */
#endregion

using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using OpenRA.Network;

namespace OpenRA.Server
{
	public sealed class VoteKickTracker
	{
		[TranslationReference("kickee")]
		const string InsufficientVotes = "notification-insufficient-votes-to-kick";

		[TranslationReference]
		const string AlreadyVoted = "notification-kick-already-voted";

		[TranslationReference("kicker", "kickee")]
		const string VoteKickStarted = "notification-vote-kick-started";

		[TranslationReference]
		const string UnableToStartAVote = "notification-unable-to-start-a-vote";

		[TranslationReference("kickee", "percentage")]
		const string VoteKickProgress = "notification-vote-kick-in-progress";

		[TranslationReference("kickee")]
		const string VoteKickEnded = "notification-vote-kick-ended";

		readonly Dictionary<int, bool> voteTracker = new();
		readonly Dictionary<Session.Client, long> failedVoteKickers = new();
		readonly Server server;

		Stopwatch voteKickTimer;
		(Session.Client Client, Connection Conn) kickee;
		(Session.Client Client, Connection Conn) voteKickerStarter;

		public VoteKickTracker(Server server)
		{
			this.server = server;
		}

		// Only admins and alive players can participate in a vote kick.
		bool ClientHasPower(Session.Client client) => client.IsAdmin || !server.CanKickClient(client);

		public void Tick()
		{
			if (voteKickTimer == null)
				return;

			if (!server.Conns.Contains(kickee.Conn))
			{
				server.SendLocalizedMessage(VoteKickEnded, Translation.Arguments("kickee", kickee.Client.Name));
				EndKickVote();
				return;
			}

			if (voteKickTimer.ElapsedMilliseconds > server.Settings.VoteKickTimer)
			{
				server.SendLocalizedMessage(VoteKickEnded, Translation.Arguments("kickee", kickee.Client.Name));
				EndKickVoteAndBlockKicker();
			}
		}

		public bool VoteKick(Connection conn, Session.Client kicker, Connection kickeeConn, Session.Client kickee, int kickeeID, bool vote)
		{
			if (server.State != ServerState.GameStarted
				|| (kickee.IsAdmin && server.Type != ServerType.Dedicated)
				|| (!vote && this.kickee.Client == null) // Disallow starting a vote with a downvote
				|| (this.kickee.Client != null && this.kickee.Client != kickee) // Disallow starting new votes when one is already ongoing.
				|| !ClientHasPower(kicker))
			{
				server.SendLocalizedMessageTo(conn, UnableToStartAVote);
				return false;
			}

			// Prevent vote kick spam abuse.
			if (voteKickTimer == null && failedVoteKickers.TryGetValue(kicker, out var time))
			{
				if (time + server.Settings.VoteKickerCooldown > kickeeConn.ConnectionTimer.ElapsedMilliseconds)
				{
					server.SendLocalizedMessageTo(conn, UnableToStartAVote);
					return false;
				}
				else
					failedVoteKickers.Remove(kicker);
			}

			var eligeablePlayers = server.Conns.Count(conn => ClientHasPower(server.GetClient(conn)));

			// If only 2 players are playing, they should not be able to kick eachother.
			if (!kickee.IsObserver && eligeablePlayers < 3)
			{
				server.SendLocalizedMessageTo(conn, InsufficientVotes, Translation.Arguments("kickee", kickee.Name));
				return false;
			}

			if (voteKickTimer == null)
			{
				Log.Write("server", $"Vote kick started on {kickeeID}.");
				voteKickTimer = Stopwatch.StartNew();
				server.SendLocalizedMessage(VoteKickStarted, Translation.Arguments("kicker", kicker.Name, "kickee", kickee.Name));
				server.DispatchServerOrdersToClients(new Order("StartKickVote", null, false) { ExtraData = (uint)kickeeID }.Serialize());
				this.kickee = (kickee, kickeeConn);
				voteKickerStarter = (kicker, conn);
			}

			if (!voteTracker.ContainsKey(conn.PlayerIndex))
				voteTracker[conn.PlayerIndex] = vote;
			else
			{
				server.SendLocalizedMessageTo(conn, AlreadyVoted, null);
				return false;
			}

			var votesFor = 0;
			var votesAgainst = 0;
			foreach (var c in voteTracker)
			{
				if (c.Value)
					votesFor++;
				else
					votesAgainst++;
			}

			var percentage = votesFor * 100 / eligeablePlayers;
			server.SendLocalizedMessage(VoteKickProgress, Translation.Arguments("kickee", kickee.Name, "percentage", percentage));

			// If only a single player is playing, allow him to kick anyone.
			if (percentage > 50 || (kickee.IsObserver && kickee.IsAdmin && eligeablePlayers < 3))
			{
				EndKickVote();
				return true;
			}

			// As we include the kickee in eligeablePlayers, we need to subtract him from the votes against.
			if (ClientHasPower(kickee))
				eligeablePlayers -= 1;

			if (votesAgainst * 100 / eligeablePlayers >= 50)
			{
				server.SendLocalizedMessage(VoteKickEnded, Translation.Arguments("kickee", kickee.Name));
				EndKickVoteAndBlockKicker();
				return false;
			}

			voteKickTimer.Restart();
			return false;
		}

		void EndKickVoteAndBlockKicker()
		{
			if (server.Conns.Contains(voteKickerStarter.Conn))
				failedVoteKickers[voteKickerStarter.Client] = voteKickerStarter.Conn.ConnectionTimer.ElapsedMilliseconds;

			EndKickVote();
		}

		void EndKickVote()
		{
			server.DispatchServerOrdersToClients(new Order("EndKickVote", null, false) { ExtraData = (uint)kickee.Client.Index }.Serialize());

			voteKickTimer = null;
			voteKickerStarter = (null, null);
			kickee = (null, null);
			voteTracker.Clear();
		}
	}
}
