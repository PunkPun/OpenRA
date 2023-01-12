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

using System;
using OpenRA.Mods.Common.Traits;
using OpenRA.Primitives;
using OpenRA.Traits;

namespace OpenRA.Mods.Cnc.Traits
{
	using FrozenActorAction = Action<FrozenUnderFogUpdatedByGps, FrozenActorLayer, GpsWatcher, FrozenActor>;

	[Desc("Updates frozen actors of actors that change owners, are sold or die whilst having an active GPS power.")]
	public class FrozenUnderFogUpdatedByGpsInfo : TraitInfo, Requires<FrozenUnderFogInfo>
	{
		public override object Create(ActorInitializer init) { return new FrozenUnderFogUpdatedByGps(init); }
	}

	public class FrozenUnderFogUpdatedByGps : INotifyOwnerChanged, INotifyActorDisposing, IOnGpsRefreshed
	{
		static readonly FrozenActorAction Refresh = (fufubg, fal, gps, fa) =>
		{
			// Refreshes the visual state of the frozen actor, so ownership changes can be seen.
			// This only makes sense if the frozen actor has already been revealed (i.e. has renderables)
			if (fa.HasRenderables)
			{
				fa.RefreshState();
				fa.NeedRenderables = true;
			}
		};
		static readonly FrozenActorAction Remove = (fufubg, fal, gps, fa) =>
		{
			// Removes the frozen actor. Once done, we no longer need to track GPS updates.
			fa.Invalidate();
			fal.Remove(fa);
			gps.UnregisterForOnGpsRefreshed(fufubg.Actor, fufubg);
		};

		class Traits
		{
			public readonly FrozenActorLayer FrozenActorLayer;
			public readonly GpsWatcher GpsWatcher;
			public Traits(Player player, FrozenUnderFogUpdatedByGps frozenUnderFogUpdatedByGps)
			{
				FrozenActorLayer = player.FrozenActorLayer;
				GpsWatcher = player.PlayerActor.TraitOrDefault<GpsWatcher>();
				GpsWatcher.RegisterForOnGpsRefreshed(frozenUnderFogUpdatedByGps.Actor, frozenUnderFogUpdatedByGps);
			}
		}

		readonly PlayerDictionary<Traits> traits;
		public readonly Actor Actor;

		public FrozenUnderFogUpdatedByGps(ActorInitializer init)
		{
			Actor = init.Self;
			traits = new PlayerDictionary<Traits>(init.World, player => new Traits(player, this));
		}

		public void OnOwnerChanged(Player oldOwner, Player newOwner)
		{
			ActOnFrozenActorsForAllPlayers(Refresh);
		}

		void INotifyActorDisposing.Disposing()
		{
			ActOnFrozenActorsForAllPlayers(Remove);
		}

		public void OnGpsRefresh(Player player)
		{
			if (Actor.IsDead)
				ActOnFrozenActorForPlayer(player, Remove);
			else
				ActOnFrozenActorForPlayer(player, Refresh);
		}

		void ActOnFrozenActorsForAllPlayers(FrozenActorAction action)
		{
			for (var playerIndex = 0; playerIndex < traits.Count; playerIndex++)
				ActOnFrozenActorForTraits(traits[playerIndex], action);
		}

		void ActOnFrozenActorForPlayer(Player player, FrozenActorAction action)
		{
			ActOnFrozenActorForTraits(traits[player], action);
		}

		void ActOnFrozenActorForTraits(Traits t, FrozenActorAction action)
		{
			if (t.FrozenActorLayer == null || t.GpsWatcher == null ||
				!t.GpsWatcher.Granted || !t.GpsWatcher.GrantedAllies)
				return;

			var fa = t.FrozenActorLayer.FromID(Actor.ActorID);
			if (fa == null)
				return;

			action(this, t.FrozenActorLayer, t.GpsWatcher, fa);
		}
	}
}
