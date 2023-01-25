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
using System.Linq;
using OpenRA.Mods.Common.Activities;
using OpenRA.Mods.Common.Orders;
using OpenRA.Primitives;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Traits
{
	[Desc("Manages DockClients on the actor.")]
	public class DockClientManagerInfo : ConditionalTraitInfo
	{
		[Desc("Go to DockTypes on deploy.")]
		public readonly BitSet<DockType> ReturnToBaseDockType;

		[Desc("How long (in ticks) to wait until (re-)checking for a nearby available DockHost.")]
		public readonly int SearchForDockDelay = 125;

		[Desc("The pathfinding cost penalty applied for each dock client waiting to unload at a DockHost.")]
		public readonly int OccupancyCostModifier = 12;

		[CursorReference]
		[Desc("Cursor to display when able to dock at target actor.")]
		public readonly string EnterCursor = "enter";

		[CursorReference]
		[Desc("Cursor to display when unable to dock at target actor.")]
		public readonly string EnterBlockedCursor = "enter-blocked";

		[VoiceReference]
		[Desc("Voice.")]
		public readonly string Voice = "Action";

		[Desc("Color to use for the target line of docking orders.")]
		public readonly Color DockLineColor = Color.Green;

		public override object Create(ActorInitializer init) { return new DockClientManager(init.Self, this); }
	}

	public class DockClientManager : ConditionalTrait<DockClientManagerInfo>, IResolveOrder, IOrderVoice, IIssueOrder, INotifyActorDisposing
	{
		readonly Actor self;
		protected IDockClient[] dockClients;
		public bool IsAliveAndInWorld => !self.IsDead && self.IsInWorld;
		public Color DockLineColor => Info.DockLineColor;
		public int OccupancyCostModifier => Info.OccupancyCostModifier;

		public DockClientManager(Actor self, DockClientManagerInfo info)
			: base(info)
		{
			this.self = self;
		}

		protected override void Created(Actor self)
		{
			base.Created(self);
			dockClients = self.TraitsImplementing<IDockClient>().ToArray();
		}

		public Actor ReservedHostActor { get; protected set; }
		public DockHost ReservedHost { get; protected set; }

		DockHost lastReservedDockHost = null;
		public DockHost LastReservedHost
		{
			get
			{
				if (lastReservedDockHost != null)
				{
					if (!lastReservedDockHost.IsEnabledAndInWorld)
						lastReservedDockHost = null;
					else
						return lastReservedDockHost;
				}

				return ReservedHost;
			}
		}

		public void UnreserveHost()
		{
			if (ReservedHost != null)
			{
				lastReservedDockHost = ReservedHost;
				ReservedHost = null;
				ReservedHostActor = null;
				lastReservedDockHost.Unreserve(this);
			}
		}

		public bool ReserveHost(Actor hostActor, DockHost host)
		{
			if (host == null)
				return false;

			if (ReservedHost == host)
				return true;

			UnreserveHost();
			if (host.Reserve(hostActor, this))
			{
				ReservedHost = host;
				ReservedHostActor = hostActor;
				return true;
			}

			return false;
		}

		public void DockStarted(Actor self, Actor hostActor, DockHost host)
		{
			foreach (var client in dockClients)
				client.DockStarted(self, hostActor, host);
		}

		public bool DockTick(Actor self, Actor hostActor, DockHost host)
		{
			var cancel = true;
			foreach (var client in dockClients)
				if (!client.DockTick(self, hostActor, host))
					cancel = false;

			return cancel;
		}

		public void DockCompleted(Actor self, Actor hostActor, DockHost host)
		{
			foreach (var client in dockClients)
				client.DockCompleted(self, hostActor, host);

			UnreserveHost();
		}

		IEnumerable<IOrderTargeter> IIssueOrder.Orders
		{
			get
			{
				yield return new EnterAlliedActorTargeter<DockHostInfo>(
					"Dock",
					5,
					Info.EnterCursor,
					Info.EnterBlockedCursor,
					DockingPossible,
					target => CanDockAt(target));
			}
		}

		void IResolveOrder.ResolveOrder(Actor self, Order order)
		{
			if (order.OrderString == "Dock")
			{
				var target = order.Target;

				// Deliver orders are only valid for own/allied actors,
				// which are guaranteed to never be frozen.
				if (target.Type != TargetType.Actor)
					return;

				if (IsTraitDisabled)
					return;

				var dock = AvailableDocks(target.Actor).ClosestDock(self, this);
				if (!dock.HasValue)
					return;

				self.QueueActivity(order.Queued, new MoveToDock(self, dock.Value.Actor, dock.Value.Trait));
				self.ShowTargetLines();
			}
		}

		string IOrderVoice.VoicePhraseForOrder(Actor self, Order order)
		{
			if (order.OrderString == "Dock" && CanDockAt(order.Target.Actor))
				return Info.Voice;

			return null;
		}

		Order IIssueOrder.IssueOrder(Actor self, IOrderTargeter order, in Target target, bool queued)
		{
			if (order.OrderID == "Dock")
				return new Order(order.OrderID, self, target, queued);

			return null;
		}

		public bool DockingPossible(Actor target, TargetModifiers modifiers)
		{
			return !IsTraitDisabled && target.TraitsImplementing<DockHost>().Any(host => dockClients.Any(client => client.DockingPossible(host.GetDockType)));
		}

		public bool DockingPossible(BitSet<DockType> type)
		{
			return !IsTraitDisabled && dockClients.Any(client => client.DockingPossible(type));
		}

		public bool CanStillDockAt(DockHost host)
		{
			return !IsTraitDisabled && dockClients.Any(client => host.CanStillDock(self, client));
		}

		public bool CanDockAt(Actor hostActor, DockHost host, bool allowedToForceEnter = false)
		{
			return !IsTraitDisabled && dockClients.Any(client => client.CanDockAt(hostActor, host, allowedToForceEnter));
		}

		public bool CanDockAt(Actor target, bool allowedToForceEnter = false)
		{
			return !IsTraitDisabled && target.TraitsImplementing<DockHost>().Any(host => dockClients.Any(client => client.CanDockAt(target, host, allowedToForceEnter)));
		}

		public TraitPair<DockHost>? ChooseNewDock(DockHost ignore, BitSet<DockType> type = default, bool allowedToForceEnter = false)
		{
			var dockHost = type.IsEmpty ? ClosestDock(ignore, allowedToForceEnter) : ClosestDock(ignore, type, allowedToForceEnter);
			return dockHost;
		}

		public TraitPair<DockHost>? ClosestDock(DockHost ignore, bool allowedToForceEnter = false)
		{
			return self.World.ActorsWithTrait<DockHost>()
				.Where(host => host.Trait != ignore && CanDockAt(host.Actor, host.Trait, allowedToForceEnter))
				.ClosestDock(self, this);
		}

		public TraitPair<DockHost>? ClosestDock(DockHost ignore, BitSet<DockType> type, bool allowedToForceEnter = false)
		{
			var clients = AvailableDockClients(type);

			// Find all docks and their occupancy count:
			return self.World.ActorsWithTrait<DockHost>()
				.Where(host => host.Trait != ignore && clients.Any(d => d.CanDockAt(host.Actor, host.Trait, allowedToForceEnter)))
				.ClosestDock(self, this);
		}

		public IEnumerable<TraitPair<DockHost>> AvailableDocks(Actor target, bool allowedToForceEnter = false)
		{
			return target.TraitsImplementing<DockHost>()
				.Where(host => dockClients.Any(client => client.CanDockAt(target, host, allowedToForceEnter)))
				.Select(host => new TraitPair<DockHost>(target, host));
		}

		public IEnumerable<IDockClient> AvailableDockClients(BitSet<DockType> type)
		{
			return dockClients.Where(client => client.DockingPossible(type));
		}

		void INotifyActorDisposing.Disposing(Actor self) { UnreserveHost(); }
	}

	public static class DockExts
	{
		public static TraitPair<DockHost>? ClosestDock(this IEnumerable<TraitPair<DockHost>> docks, Actor self, DockClientManager client)
		{
			var mobile = self.TraitOrDefault<Mobile>();
			if (mobile != null)
			{
				// Overlapping docks can become hidden
				var lookup = docks.ToDictionary(dock => self.World.Map.CellContaining(dock.Trait.DockPosition));

				// Start a search from each docks position:
				var path = mobile.PathFinder.FindPathToTargetCell(
					self, lookup.Keys, self.Location, BlockedByActor.None,
					location =>
					{
						if (!lookup.ContainsKey(location))
							return 0;

						var dock = lookup[location];

						// Prefer docks with less occupancy (multiplier is to offset distance cost):
						return dock.Trait.ReservationCount * client.OccupancyCostModifier;
					});

				if (path.Count > 0)
					return lookup[path.Last()];
			}
			else
			{
				return docks
					.OrderBy(dock => (self.Location - self.World.Map.CellContaining(dock.Trait.DockPosition)).LengthSquared + dock.Trait.ReservationCount * client.OccupancyCostModifier)
					.FirstOrDefault();
			}

			return null;
		}
	}
}
