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
using OpenRA.Mods.Common.Activities;
using OpenRA.Primitives;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Traits
{
	[Desc("Reserve landing places for aircraft.")]
	class ReservableInfo : TraitInfo
	{
		public override object Create(ActorInitializer init) { return new Reservable(init.Self); }
	}

	public class Reservable : ITick, INotifyOwnerChanged, INotifySold, INotifyActorDisposing, INotifyCreated
	{
		public readonly Actor Actor;
		Actor reservedFor;
		Aircraft reservedForAircraft;
		RallyPoint rallyPoint;

		public Reservable(Actor self)
		{
			Actor = self;
		}

		void INotifyCreated.Created()
		{
			rallyPoint = Actor.TraitOrDefault<RallyPoint>();
		}

		void ITick.Tick()
		{
			// Nothing to do.
			if (reservedFor == null)
				return;

			if (!Target.FromActor(reservedFor).IsValidFor(Actor))
			{
				// Not likely to arrive now.
				reservedForAircraft.UnReserve();
				reservedFor = null;
				reservedForAircraft = null;
			}
		}

		public IDisposable Reserve(Actor forActor, Aircraft forAircraft)
		{
			if (reservedForAircraft != null && reservedForAircraft.MayYieldReservation)
				UnReserve();

			reservedFor = forActor;
			reservedForAircraft = forAircraft;

			// NOTE: we really don't care about the GC eating DisposableActions that apply to a world *other* than
			// the one we're playing in.
			return new DisposableAction(
				() => { reservedFor = null; reservedForAircraft = null; },
				() => Game.RunAfterTick(() =>
				{
					if (Game.IsCurrentWorld(Actor.World))
						throw new InvalidOperationException(
							$"Attempted to finalize an undisposed DisposableAction. {forActor.Info.Name} ({forActor.ActorID}) reserved {Actor.Info.Name} ({Actor.ActorID})");
				}));
		}

		public static bool IsReserved(Actor a)
		{
			var res = a.TraitOrDefault<Reservable>();
			return res != null && res.reservedForAircraft != null && !res.reservedForAircraft.MayYieldReservation;
		}

		public static bool IsAvailableFor(Actor reservable, Actor forActor)
		{
			var res = reservable.TraitOrDefault<Reservable>();
			return res == null || res.reservedForAircraft == null || res.reservedForAircraft.MayYieldReservation || res.reservedFor == forActor;
		}

		void UnReserve()
		{
			if (reservedForAircraft != null)
			{
				if (reservedForAircraft.GetActorBelow() == Actor)
				{
					// HACK: Cache this in a local var, such that the inner activity of AttackMoveActivity can access the trait easily after reservedForAircraft was nulled
					var aircraft = reservedForAircraft;
					if (rallyPoint != null && rallyPoint.Path.Count > 0)
						foreach (var cell in rallyPoint.Path)
							reservedFor.QueueActivity(new AttackMoveActivity(reservedFor, () => aircraft.MoveTo(cell, 1, targetLineColor: Color.OrangeRed)));
					else
						reservedFor.QueueActivity(new TakeOff(reservedFor));
				}

				reservedForAircraft.UnReserve();
			}
		}

		void INotifyActorDisposing.Disposing() { UnReserve(); }

		void INotifyOwnerChanged.OnOwnerChanged(Player oldOwner, Player newOwner) { UnReserve(); }

		void INotifySold.Selling() { UnReserve(); }
		void INotifySold.Sold() { UnReserve(); }
	}
}
