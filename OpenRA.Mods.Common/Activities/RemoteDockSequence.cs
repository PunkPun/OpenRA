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
using OpenRA.Activities;
using OpenRA.Mods.Common.Traits;

namespace OpenRA.Mods.Common.Activities
{
	public class RemoteDockSequence : Activity
	{
		protected readonly DockClientManager DockClient;
		protected readonly Actor DockHostActor;
		protected readonly IDockHost DockHost;
		protected bool dockStarted = false;
		protected readonly int CloseEnough;

		public RemoteDockSequence(DockClientManager client, Actor hostActor, IDockHost host, int closeEnough)
		{
			DockClient = client;
			DockHost = host;
			DockHostActor = hostActor;
			CloseEnough = closeEnough;
			QueueChild(new Wait(host.DockWait));
		}

		bool HostAliveAndInRange(Actor self) =>
			!IsCanceling && !DockHostActor.IsDead && DockHostActor.IsInWorld
			&& (self.CenterPosition - DockHost.DockPosition).Length <= CloseEnough;

		protected override void OnFirstRun(Actor self)
		{
			if (HostAliveAndInRange(self) && DockClient.CanDockAt(DockHostActor, DockHost, false, true))
			{
				DockHost.OnDockStarted(DockHostActor, self, DockClient);
				dockStarted = true;
			}
			else
				DockClient.UnreserveHost();
		}

		public override bool Tick(Actor self)
		{
			if (!dockStarted)
				return true;

			if (HostAliveAndInRange(self) && !DockClient.OnDockTick(self, DockHostActor, DockHost))
				return false;

			DockHost.OnDockCompleted(DockHostActor, self, DockClient);
			return true;
		}
	}
}
