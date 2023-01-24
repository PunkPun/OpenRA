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
using System.Collections.Generic;
using OpenRA.Activities;
using OpenRA.Primitives;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Traits
{
	public sealed class DockType { DockType() { } }

	[Desc("A generic dock that services DockClients.")]
	public class DockHostInfo : ConditionalTraitInfo, Requires<RefineryInfo>
	{
		[Desc("Docking type.")]
		public readonly BitSet<DockType> Type;

		[Desc("How many clients can this dock be reserved for?")]
		public readonly int MaxQueueLength = 3;

		[Desc("How long should the harvester wait before starting the docking sequence.")]
		public readonly int DockWait = 10;

		[Desc("Actual client facing when docking.")]
		public readonly WAngle DockAngle = WAngle.Zero;

		[Desc("Docking cell relative to the centre of the actor.")]
		public readonly WVec DockOffset = WVec.Zero;

		[Desc("Does client need to be dragged in?")]
		public readonly bool IsDragRequired = false;

		[Desc("Vector by which the client will be dragged when docking.")]
		public readonly WVec DragOffset = WVec.Zero;

		[Desc("In how many steps to perform the dragging?")]
		public readonly int DragLength = 0;

		public override object Create(ActorInitializer init) { return new DockHost(init.Self, this); }
	}

	public class DockHost : ConditionalTrait<DockHostInfo>, ITick, INotifySold, INotifyCapture, INotifyOwnerChanged, ISync, INotifyActorDisposing
	{
		readonly Actor self;

		public BitSet<DockType> GetDockType => Info.Type;
		public bool IsEnabledAndInWorld => !preventDock && !IsTraitDisabled && !self.IsDead && self.IsInWorld;
		public int ReservationCount => reservedDockClients.Count;
		public bool CanReserve => ReservationCount < Info.MaxQueueLength;
		readonly List<DockClientManager> reservedDockClients = new List<DockClientManager>();

		public WPos DockPosition => self.CenterPosition + Info.DockOffset;
		public int DockWait => Info.DockWait;
		public WAngle DockAngle => Info.DockAngle;
		public bool IsDragRequired => Info.IsDragRequired;
		public WVec DragOffset => Info.DragOffset;
		public int DragLength => Info.DragLength;

		[Sync]
		bool preventDock = false;

		[Sync]
		Actor dockedClientActor = null;
		DockClientManager dockedClient = null;
		readonly Func<Actor, DockClientManager, Actor, DockHost, Activity> dockSequence;

		public DockHost(Actor self, DockHostInfo info)
			: base(info)
		{
			this.self = self;

			// HACK: Remove once TiberianSunRefinery is disposed of
			dockSequence = self.Trait<Refinery>().DockSequence;
		}

		public bool Reserve(Actor self, DockClientManager client)
		{
			if (CanReserve && !reservedDockClients.Contains(client))
			{
				reservedDockClients.Add(client);
				client.ReserveHost(self, this);
				return true;
			}

			return false;
		}

		public void Unreserve()
		{
			while (reservedDockClients.Count > 0)
				Unreserve(reservedDockClients[0]);
		}

		public void Unreserve(DockClientManager client)
		{
			if (reservedDockClients.Contains(client))
			{
				reservedDockClients.Remove(client);
				client.UnreserveHost();
			}
		}

		public bool CanDock(Actor clientActor, IDockClient client, bool allowedToForceEnter)
		{
			return CanStillDock(clientActor, client) && (allowedToForceEnter || CanReserve || reservedDockClients.Contains(client.DockClientManager));
		}

		public bool CanStillDock(Actor clientActor, IDockClient client)
		{
			return IsEnabledAndInWorld && !IsTraitDisabled && client.IsEnabledAndInWorld && clientActor.Owner.IsAlliedWith(self.Owner);
		}

		public void DockStarted(Actor clientActor, DockClientManager client)
		{
			dockedClientActor = clientActor;
			dockedClient = client;
		}

		public void DockCompleted()
		{
			dockedClientActor = null;
			dockedClient = null;
		}

		public virtual void QueueDockSequence(Activity dockOrder, Actor self, DockClientManager client, Actor hostActor, DockHost host)
		{
			dockOrder.QueueChild(dockSequence(self, client, hostActor, host));
		}

		void ITick.Tick(Actor self)
		{
			// Client was killed during docking
			if (dockedClient != null && !dockedClient.IsAliveAndInWorld)
			{
				dockedClient = null;
				dockedClientActor = null;
			}
		}

		protected override void TraitDisabled(Actor self) { Unreserve(); }

		void INotifyOwnerChanged.OnOwnerChanged(Actor self, Player oldOwner, Player newOwner) { Unreserve(); }

		void INotifyCapture.OnCapture(Actor self, Actor captor, Player oldOwner, Player newOwner, BitSet<CaptureType> captureTypes)
		{
			// Steal any docked unit too
			if (dockedClient != null && dockedClient.IsAliveAndInWorld)
			{
				dockedClientActor.ChangeOwner(newOwner);

				if (!dockedClient.IsTraitDisabled)
					dockedClient.ReserveHost(self, this);
			}
		}

		void INotifySold.Selling(Actor self) { preventDock = true; }

		void INotifySold.Sold(Actor self) { Unreserve(); }

		void INotifyActorDisposing.Disposing(Actor self) { preventDock = true; Unreserve(); }
	}
}
