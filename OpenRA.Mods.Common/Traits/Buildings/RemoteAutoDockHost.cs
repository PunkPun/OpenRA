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
using OpenRA.Primitives;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Traits
{
	[Desc("A generic dock that services DockClients at a distance and compels nearby clients to dock automatically.")]
	public class RemoteAutoDockHostInfo : RemoteDockHostInfo, IDockHostInfo
	{
		public override object Create(ActorInitializer init) { return new RemoteAutoDockHost(init.Self, this); }
	}

	public class RemoteAutoDockHost : RemoteDockHost, IDockHost, INotifyAddedToWorld, INotifyRemovedFromWorld, INotifyOtherProduction
	{
		int proximityTrigger;
		WDist desiredRange;
		WDist cachedRange;
		WPos cachedPosition;

		protected readonly List<TraitPair<DockClientManager>> ProximityClients = new();

		public RemoteAutoDockHost(Actor self, RemoteAutoDockHostInfo info)
			: base(self, info) { }

		protected override void Tick(Actor self)
		{
			// Don't start docking if we're disabled.
			if (!IsTraitDisabled && !preventDock)
			{
				// Update ProximityTrigger incase we get disabled or moved.
				if (self.CenterPosition != DockPosition || desiredRange != cachedRange)
				{
					cachedPosition = DockPosition;
					cachedRange = desiredRange;
					self.World.ActorMap.UpdateProximityTrigger(proximityTrigger, cachedPosition, cachedRange, WDist.Zero);
				}

				// Actively search for nearby clients.
				foreach (var pair in ProximityClients)
				{
					var clientActor = pair.Actor;
					var client = pair.Trait;

					// Invalid actors will be removed in the next cycle so we can ignore them.
					if (clientActor.Disposed || clientActor.IsDead || !clientActor.IsInWorld)
						continue;

					if (Info.OccupyClient)
					{
						// Auto-dock is not allowed to overwrite activities here.
						if (clientActor.CurrentActivity == null && client.CanDockAt(self, this) && client.ReserveHost(self, this))
							QueueDockActivity(null, self, clientActor, client);
					}
					else
					{
						if (client.ReservedHost == null)
						{
							if (client.CanDockAt(self, this) && client.ReserveHost(self, this))
								QueueDockActivity(null, self, clientActor, client);
						}
						else if (client.ReservedHost == this)
						{
							// Make sure docking is queued as soon as we get into range.
							QueueDockActivity(null, self, clientActor, client);
						}
					}
				}
			}

			base.Tick(self);
		}

		void INotifyAddedToWorld.AddedToWorld(Actor self)
		{
			cachedPosition = DockPosition;
			proximityTrigger = self.World.ActorMap.AddProximityTrigger(cachedPosition, cachedRange, WDist.Zero, ActorEntered, ActorExited);
		}

		void INotifyRemovedFromWorld.RemovedFromWorld(Actor self)
		{
			self.World.ActorMap.RemoveProximityTrigger(proximityTrigger);
		}

		void ActorEntered(Actor a)
		{
			if (a.Disposed || Self.Disposed)
				return;

			if (a == Self)
				return;

			var client = a.TraitOrDefault<DockClientManager>();
			if (client == null)
				return;

			ProximityClients.Add(new TraitPair<DockClientManager>(a, client));
		}

		void ActorExited(Actor a)
		{
			var client = ProximityClients.Find(p => p.Actor == a);
			if (client != default)
				ProximityClients.Remove(client);

			var waitingClient = WaitingClients.Find(p => p.Client.Actor == a);
			if (waitingClient != default)
			{
				WaitingClients.Remove(waitingClient);
				waitingClient.Client.Trait.UnreserveHost();
			}
		}

		public void UnitProducedByOther(Actor self, Actor producer, Actor produced, string productionType, TypeDictionary init)
		{
			// We don't track clients when disabled.
			if (IsTraitDisabled || preventDock)
				return;

			// If the produced Actor doesn't occupy space, it can't be in range.
			if (produced.OccupiesSpace == null)
				return;

			// Work around for actors produced within the region not triggering until the second tick.
			if ((produced.CenterPosition - self.CenterPosition).HorizontalLengthSquared <= Info.Range.LengthSquared)
			{
				var client = produced.TraitOrDefault<DockClientManager>();
				if (client == null)
					return;

				ProximityClients.Add(new TraitPair<DockClientManager>(produced, client));
			}
		}

		protected override void TraitEnabled(Actor self) { desiredRange = Info.Range; base.TraitEnabled(self); }

		protected override void TraitDisabled(Actor self) { ProximityClients.Clear(); desiredRange = WDist.Zero; base.TraitDisabled(self); }
	}
}
