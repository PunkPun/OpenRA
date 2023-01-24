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
using System.Linq;
using OpenRA.Activities;
using OpenRA.Mods.Common.Traits;
using OpenRA.Primitives;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Activities
{
	public abstract class HarvesterDockSequence : Activity
	{
		protected enum DockingState { Wait, Drag, Dock, Loop, Undock, Complete }

		protected readonly DockClientManager DockClient;
		protected readonly Actor DockHostActor;
		protected readonly DockHost DockHost;
		protected readonly WAngle DockAngle;
		protected readonly bool IsDragRequired;
		protected readonly WVec DragOffset;
		protected readonly int DragLength;
		protected readonly WPos StartDrag;
		protected readonly WPos EndDrag;

		protected DockingState dockingState;

		readonly INotifyDockClient[] notifyDockClients;
		readonly INotifyDockHost[] notifyDockHosts;

		public HarvesterDockSequence(Actor self, DockClientManager client, Actor hostActor, DockHost host)
		{
			dockingState = DockingState.Drag;
			DockClient = client;
			DockHost = host;
			DockHostActor = hostActor;
			DockAngle = host.DockAngle;
			IsDragRequired = host.IsDragRequired;
			DragOffset = host.DragOffset;
			DragLength = host.DragLength;
			StartDrag = self.CenterPosition;
			EndDrag = hostActor.CenterPosition + DragOffset;
			notifyDockClients = self.TraitsImplementing<INotifyDockClient>().ToArray();
			notifyDockHosts = hostActor.TraitsImplementing<INotifyDockHost>().ToArray();
			QueueChild(new Wait(host.DockWait));
		}

		public override bool Tick(Actor self)
		{
			switch (dockingState)
			{
				case DockingState.Wait:
					return false;

				case DockingState.Drag:
					if (IsCanceling || !DockClient.CanStillDockAt(DockHost))
					{
						DockClient.UnreserveHost();
						return true;
					}

					dockingState = DockingState.Dock;
					if (IsDragRequired)
						QueueChild(new Drag(self, StartDrag, EndDrag, DragLength));

					return false;

				case DockingState.Dock:
					if (!IsCanceling && DockClient.CanStillDockAt(DockHost))
					{
						OnStateDock(self);
						DockHost.DockStarted(self, DockClient);
						DockClient.DockStarted(self, DockHostActor, DockHost);
						NotifyDocked(self);
					}
					else
						dockingState = DockingState.Undock;

					return false;

				case DockingState.Loop:
					if (IsCanceling || !DockHost.IsEnabledAndInWorld || DockClient.DockTick(self, DockHostActor, DockHost))
						dockingState = DockingState.Undock;

					return false;

				case DockingState.Undock:
					OnStateUndock(self);
					return false;

				case DockingState.Complete:
					DockHost.DockCompleted();
					DockClient.DockCompleted(self, DockHostActor, DockHost);
					NotifyUndocked(self);
					if (IsDragRequired)
						QueueChild(new Drag(self, EndDrag, StartDrag, DragLength));

					return true;
			}

			throw new InvalidOperationException("Invalid harvester dock state");
		}

		public override IEnumerable<Target> GetTargets(Actor self)
		{
			yield return Target.FromActor(DockHostActor);
		}

		public override IEnumerable<TargetLineNode> TargetLineNodes(Actor self)
		{
			yield return new TargetLineNode(Target.FromActor(DockHostActor), Color.Green);
		}

		public abstract void OnStateDock(Actor self);

		public abstract void OnStateUndock(Actor self);

		void NotifyDocked(Actor self)
		{
			foreach (var nd in notifyDockClients)
				nd.Docked(self, DockHostActor);

			foreach (var nd in notifyDockHosts)
				nd.Docked(DockHostActor, self);
		}

		void NotifyUndocked(Actor self)
		{
			foreach (var nd in notifyDockClients)
				nd.Undocked(self, DockHostActor);

			if (DockHostActor.IsInWorld && !DockHostActor.IsDead)
				foreach (var nd in notifyDockHosts)
					nd.Undocked(DockHostActor, self);
		}
	}
}
