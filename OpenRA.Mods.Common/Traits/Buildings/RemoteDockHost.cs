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
using OpenRA.Activities;
using OpenRA.Mods.Common.Activities;
using OpenRA.Primitives;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Traits
{
	[Desc("A generic dock that services DockClients at a distance.")]
	public class RemoteDockHostInfo : ConditionalTraitInfo, IDockHostInfo
	{
		[Desc("Docking type.")]
		public readonly BitSet<DockType> Type;

		[Desc("How many clients can this dock be reserved for? If set to -1, there is no limit.")]
		public readonly int MaxQueueLength = -1;

		[Desc("How long should the client wait before starting the docking sequence.")]
		public readonly int DockWait = 10;

		[Desc("Docking cell relative to the centre of the actor.")]
		public readonly WVec DockOffset = WVec.Zero;

		[Desc("From how far away can clients be serviced?")]
		public readonly WDist Range = WDist.FromCells(4);

		[Desc("Does the client need to be preoccupied with docking?")]
		public readonly bool OccupyClient = false;

		public override object Create(ActorInitializer init) { return new RemoteDockHost(init.Self, this); }
	}

	public class RemoteDockHost : ConditionalTrait<RemoteDockHostInfo>, IDockHost, ITick, INotifySold, INotifyOwnerChanged, ISync, INotifyKilled, INotifyActorDisposing
	{
		protected readonly Actor Self;

		[Sync]
		protected bool preventDock = false;

		protected readonly List<DockClientManager> ReservedDockClients = new();
		protected readonly List<(TraitPair<DockClientManager> Client, long Time)> WaitingClients = new();
		protected readonly List<TraitPair<DockClientManager>> DockedClients = new();

		public RemoteDockHost(Actor self, RemoteDockHostInfo info)
			: base(info)
		{
			Self = self;
		}

		#region IDockHost

		public BitSet<DockType> GetDockType => Info.Type;

		public bool IsEnabledAndInWorld => !preventDock && !IsTraitDisabled && !Self.IsDead && Self.IsInWorld;
		public int ReservationCount => ReservedDockClients.Count;
		public bool CanBeReserved => Info.MaxQueueLength < 0 || ReservationCount < Info.MaxQueueLength;

		public WPos DockPosition => Self.CenterPosition + Info.DockOffset;
		public int DockWait => Info.DockWait;

		public WAngle DockAngle => WAngle.Zero;

		public virtual bool IsDockingPossible(Actor clientActor, IDockClient client, bool ignoreReservations = false)
		{
			return !IsTraitDisabled && (ignoreReservations || CanBeReserved || ReservedDockClients.Contains(client.DockClientManager));
		}

		public virtual bool Reserve(Actor self, DockClientManager client)
		{
			if (CanBeReserved && !ReservedDockClients.Contains(client))
			{
				ReservedDockClients.Add(client);
				client.ReserveHost(self, this);
				return true;
			}

			return false;
		}

		public virtual void UnreserveAll()
		{
			while (ReservedDockClients.Count > 0)
				Unreserve(ReservedDockClients[0]);

			WaitingClients.Clear();
		}

		public virtual void Unreserve(DockClientManager client)
		{
			if (ReservedDockClients.Contains(client))
			{
				ReservedDockClients.Remove(client);
				client.UnreserveHost();
			}
		}

		public virtual void OnDockStarted(Actor self, Actor clientActor, DockClientManager client)
		{
			if (Info.OccupyClient || DockWait <= 0)
				DockStarted(self, new TraitPair<DockClientManager>(clientActor, client));
			else
				WaitingClients.Add((new TraitPair<DockClientManager>(clientActor, client), DockWait + self.World.WorldTick));
		}

		public virtual void OnDockCompleted(Actor self, Actor clientActor, DockClientManager client)
		{
			if (clientActor != null && !clientActor.IsDead && clientActor.IsInWorld)
				client.OnDockCompleted(clientActor, self, this);

			DockedClients.Remove(DockedClients.First(c => c.Trait == client));
		}

		public virtual bool QueueMoveActivity(Activity moveToDockActivity, Actor self, Actor clientActor, DockClientManager client)
		{
			if ((clientActor.CenterPosition - DockPosition).HorizontalLengthSquared > Info.Range.LengthSquared)
			{
				// TODO: MoveWithinRange doesn't support offsets.
				// TODO: MoveWithinRange considers the whole footprint instead of a point on the actor.
				moveToDockActivity.QueueChild(clientActor.Trait<IMove>().MoveWithinRange(Target.FromActor(self), Info.Range));
				return true;
			}

			return false;
		}

		public virtual void QueueDockActivity(Activity moveToDockActivity, Actor self, Actor clientActor, DockClientManager client)
		{
			if (Info.OccupyClient)
			{
				if (moveToDockActivity == null)
					clientActor.QueueActivity(new RemoteDockSequence(client, self, this, Info.Range.Length));
				else
					moveToDockActivity.QueueChild(new RemoteDockSequence(client, self, this, Info.Range.Length));
			}
			else
			{
				// Make sure OnDockStarted is only called once.
				if (!WaitingClients.Any(p => p.Client.Trait == client) && !DockedClients.Any(p => p.Trait == client))
					OnDockStarted(self, clientActor, client);
			}
		}

		#endregion

		void ITick.Tick(Actor self)
		{
			Tick(self);
		}

		bool ClientAliveAndInRange(Actor clientActor) => clientActor != null && !clientActor.IsDead && clientActor.IsInWorld
			&& (clientActor.CenterPosition - DockPosition).HorizontalLengthSquared <= Info.Range.LengthSquared;

		protected virtual void Tick(Actor self)
		{
			if (Info.OccupyClient || IsTraitDisabled)
				return;

			// Track wait time manually.
			for (var i = 0; i < WaitingClients.Count; i++)
			{
				if (WaitingClients[i].Time < self.World.WorldTick)
				{
					var client = WaitingClients[i].Client.Trait;
					if (ClientAliveAndInRange(WaitingClients[i].Client.Actor) && client.CanDockAt(self, this, false, true))
						DockStarted(self, WaitingClients[i].Client);
					else
						Unreserve(client);

					WaitingClients.RemoveAt(i);
					i--;
				}
			}

			// Tick clients manually.
			for (var i = 0; i < DockedClients.Count; i++)
			{
				var clientActor = DockedClients[i].Actor;
				if (ClientAliveAndInRange(clientActor) && !DockedClients[i].Trait.OnDockTick(clientActor, self, this))
					continue;

				OnDockCompleted(self, clientActor, DockedClients[i].Trait);
				i--;
			}
		}

		protected virtual void DockStarted(Actor self, TraitPair<DockClientManager> client)
		{
			DockedClients.Add(client);
			client.Trait.OnDockStarted(client.Actor, self, this);
		}

		public virtual void CancelDocking(Actor self)
		{
			// Cancelling will be handled in the RemoteDockSequence activity.
			if (Info.OccupyClient)
				return;

			while (DockedClients.Count != 0)
			{
				var pair = DockedClients[0];
				OnDockCompleted(self, pair.Actor, pair.Trait);
			}
		}

		protected override void TraitDisabled(Actor self) { CancelDocking(self); UnreserveAll(); }

		void INotifyOwnerChanged.OnOwnerChanged(Actor self, Player oldOwner, Player newOwner) { CancelDocking(self); UnreserveAll(); }

		void INotifySold.Selling(Actor self) { preventDock = true; }

		void INotifySold.Sold(Actor self) { CancelDocking(self); UnreserveAll(); }

		void INotifyKilled.Killed(Actor self, AttackInfo e) { CancelDocking(self); UnreserveAll(); }

		void INotifyActorDisposing.Disposing(Actor self) { CancelDocking(self); UnreserveAll(); preventDock = true; }
	}
}
