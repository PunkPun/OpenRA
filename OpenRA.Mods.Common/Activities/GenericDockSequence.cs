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
using OpenRA.Mods.Common.Traits.Render;
using OpenRA.Primitives;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Activities
{
	public class GenericDockSequence : Activity
	{
		protected enum DockingState { Wait, Drag, Dock, Loop, Undock, Complete }

		protected readonly Actor DockHostActor;
		protected readonly IDockHost DockHost;
		protected readonly WithDockingOverlay DockHostSpriteOverlay;
		protected readonly DockClientManager DockClient;
		protected readonly IDockClientBody DockClientBody;
		protected readonly bool IsDragRequired;
		protected readonly int DragLength;
		protected readonly WPos StartDrag;
		protected readonly WPos EndDrag;

		protected DockingState dockingState;

		readonly INotifyActiveDock[] notifyActiveDockClients;
		readonly INotifyActiveDock[] notifyActiveDockHosts;
		readonly INotifyDock[] notifyDockClients;
		readonly INotifyDock[] notifyDockHosts;

		readonly BitSet<DockType> sharedDockTypes;
		BitSet<DockType> activeDockTypes;
		bool dockInitiated = false;

		public GenericDockSequence(Actor self, DockClientManager client, Actor hostActor, IDockHost host)
		{
			dockingState = DockingState.Drag;
			sharedDockTypes = client.GetSharedTypes(host);

			DockClient = client;
			DockClientBody = self.TraitsImplementing<IDockClientBody>().FirstOrDefault(b => b.DockType.Overlaps(sharedDockTypes));
			notifyActiveDockClients = self.TraitsImplementing<INotifyActiveDock>().ToArray();
			notifyDockClients = self.TraitsImplementing<INotifyDock>().ToArray();

			DockHost = host;
			DockHostActor = hostActor;
			DockHostSpriteOverlay = hostActor.TraitOrDefault<WithDockingOverlay>();
			notifyActiveDockHosts = hostActor.TraitsImplementing<INotifyActiveDock>().ToArray();
			notifyDockHosts = hostActor.TraitsImplementing<INotifyDock>().ToArray();

			if (host is IDockHostDrag sequence)
			{
				IsDragRequired = sequence.IsDragRequired;
				DragLength = sequence.DragLength;
				StartDrag = self.CenterPosition;
				EndDrag = hostActor.CenterPosition + sequence.DragOffset;
			}
			else
				IsDragRequired = false;

			QueueChild(new Wait(host.DockWait));
		}

		public override bool Tick(Actor self)
		{
			switch (dockingState)
			{
				case DockingState.Wait:
					return false;

				case DockingState.Drag:
					if (IsCanceling || DockHostActor.IsDead || !DockHostActor.IsInWorld || !DockClient.CanDockAt(DockHostActor, DockHost, false, true))
					{
						DockClient.UnreserveHost();
						return true;
					}

					dockingState = DockingState.Dock;
					if (IsDragRequired)
						QueueChild(new Drag(self, StartDrag, EndDrag, DragLength));

					return false;

				case DockingState.Dock:
					if (!IsCanceling && !DockHostActor.IsDead && DockHostActor.IsInWorld && DockClient.CanDockAt(DockHostActor, DockHost, false, true))
					{
						dockInitiated = true;
						PlayDockAnimations(self);
						DockHost.OnDockStarted(DockHostActor, self, DockClient);
						DockClient.OnDockStarted(self, DockHostActor, DockHost);
						NotifyProcedureStarted(self);
					}
					else
						dockingState = DockingState.Undock;

					return false;

				case DockingState.Loop:
					if (IsCanceling || DockHostActor.IsDead || !DockHostActor.IsInWorld || DockClient.OnDockTick(self, DockHostActor, DockHost, () => UpdateActiveDockTypes(self)))
					{
						NotifyUndocked(self);
						dockingState = DockingState.Undock;
					}

					return false;

				case DockingState.Undock:
					if (dockInitiated)
						PlayUndockAnimations(self);
					else
						dockingState = DockingState.Complete;

					return false;

				case DockingState.Complete:
					DockHost.OnDockCompleted(DockHostActor, self, DockClient);
					DockClient.OnDockCompleted(self, DockHostActor, DockHost);
					NotifyProcedureEnded(self);
					if (IsDragRequired)
						QueueChild(new Drag(self, EndDrag, StartDrag, DragLength));

					return true;
			}

			throw new InvalidOperationException("Invalid harvester dock state");
		}

		protected virtual void PlayDockAnimations(Actor self)
		{
			PlayDockCientAnimation(self, () =>
			{
				if (DockHostSpriteOverlay != null)
				{
					dockingState = DockingState.Wait;
					DockHostSpriteOverlay.Start(self, () => dockingState = DockingState.Loop);
				}
				else
					dockingState = DockingState.Loop;
			});
		}

		protected virtual void PlayDockCientAnimation(Actor self, Action after)
		{
			if (DockClientBody != null)
			{
				dockingState = DockingState.Wait;
				DockClientBody.PlayDockAnimation(self, () => after());
			}
			else
				after();
		}

		protected virtual void PlayUndockAnimations(Actor self)
		{
			if (DockHostActor.IsInWorld && !DockHostActor.IsDead && DockHostSpriteOverlay != null)
			{
				dockingState = DockingState.Wait;
				DockHostSpriteOverlay.End(self, () => PlayUndockClientAnimation(self, () => dockingState = DockingState.Complete));
			}
			else
				PlayUndockClientAnimation(self, () => dockingState = DockingState.Complete);
		}

		protected virtual void PlayUndockClientAnimation(Actor self, Action after)
		{
			if (DockClientBody != null)
			{
				dockingState = DockingState.Wait;
				DockClientBody.PlayReverseDockAnimation(self, () => after());
			}
			else
				after();
		}

		void NotifyProcedureStarted(Actor self)
		{
			foreach (var nd in notifyDockClients)
				nd.DockProcedureStarted(self, DockHostActor, sharedDockTypes);

			foreach (var nd in notifyDockHosts)
				nd.DockProcedureStarted(DockHostActor, self, sharedDockTypes);
		}

		void NotifyProcedureEnded(Actor self)
		{
			foreach (var nd in notifyDockClients)
				nd.DockProcedureEnded(self, DockHostActor, sharedDockTypes);

			if (DockHostActor.IsInWorld && !DockHostActor.IsDead)
				foreach (var nd in notifyDockHosts)
					nd.DockProcedureEnded(DockHostActor, self, sharedDockTypes);
		}

		void NotifyActiveDocksChanged(Actor self)
		{
			foreach (var nd in notifyActiveDockClients)
				nd.ActiveDocksChanged(self, DockHostActor, activeDockTypes);

			foreach (var nd in notifyActiveDockHosts)
				nd.ActiveDocksChanged(DockHostActor, self, activeDockTypes);
		}

		void UpdateActiveDockTypes(Actor self)
		{
			var active = DockClient.GetActiveTypes(DockHost);
			if (activeDockTypes == active)
				return;

			NotifyActiveDocksChanged(self);

			activeDockTypes = active;
		}

		void NotifyUndocked(Actor self)
		{
			if (activeDockTypes == default)
				return;

			NotifyActiveDocksChanged(self);
			activeDockTypes = default;
		}

		public override IEnumerable<Target> GetTargets(Actor self)
		{
			yield return Target.FromActor(DockHostActor);
		}

		public override IEnumerable<TargetLineNode> TargetLineNodes(Actor self)
		{
			yield return new TargetLineNode(Target.FromActor(DockHostActor), Color.Green);
		}
	}
}
