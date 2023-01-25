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
using OpenRA.Mods.Common.Traits;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Activities
{
	public class MoveToDock : Activity
	{
		readonly IMove movement;
		readonly DockClientManager dockClient;
		Actor dockHostActor;
		DockHost dockHost;
		readonly INotifyDockClientMoving[] notifyDockClientMoving;

		public MoveToDock(Actor self, Actor dockHostActor = null, DockHost dockHost = null)
		{
			movement = self.Trait<IMove>();
			dockClient = self.Trait<DockClientManager>();
			this.dockHostActor = dockHostActor;
			this.dockHost = dockHost;
			notifyDockClientMoving = self.TraitsImplementing<INotifyDockClientMoving>().ToArray();
		}

		public override bool Tick(Actor self)
		{
			if (dockClient.IsTraitDisabled)
				Cancel(self, true);

			if (IsCanceling)
				return true;

			// Find the nearest DockHost if not explicitly ordered to a specific docl:
			if (dockHost == null || !dockHost.IsEnabledAndInWorld)
			{
				var host = dockClient.ChooseNewDock(null);
				if (host.HasValue)
				{
					dockHost = host.Value.Trait;
					dockHostActor = host.Value.Actor;
				}
			}

			// No docks exist; check again after delay defined in dockClient.
			if (dockHost == null)
			{
				QueueChild(new Wait(dockClient.Info.SearchForDockDelay));
				return false;
			}

			if (dockClient.ReserveHost(dockHostActor, dockHost))
			{
				// Mobile cannot freely move in WPos so when we calculate close enough, we convert to CPos.
				if (movement is Mobile ? self.Location != self.World.Map.CellContaining(dockHost.DockPosition) : self.CenterPosition != dockHost.DockPosition)
				{
					foreach (var ndcm in notifyDockClientMoving)
						ndcm.MovingToDock(self, dockHostActor, dockHost);

					QueueChild(movement.MoveOntoTarget(self, Target.FromActor(dockHostActor), dockHost.DockPosition - dockHostActor.CenterPosition, dockHost.DockAngle));
					return false;
				}

				dockHost.QueueDockSequence(this, self, dockClient, dockHostActor, dockHost);
				return true;
			}
			else
			{
				QueueChild(new Wait(dockClient.Info.SearchForDockDelay));
				return false;
			}
		}

		public override void Cancel(Actor self, bool keepQueue = false)
		{
			dockClient.UnreserveHost();
			foreach (var ndcm in notifyDockClientMoving)
				ndcm.MovementCancelled(self);

			base.Cancel(self, keepQueue);
		}

		public override IEnumerable<TargetLineNode> TargetLineNodes(Actor self)
		{
			if (dockHostActor != null)
				yield return new TargetLineNode(Target.FromActor(dockHostActor), dockClient.DockLineColor);
			else
			{
				if (dockClient.ReservedHostActor != null)
					yield return new TargetLineNode(Target.FromActor(dockClient.ReservedHostActor), dockClient.DockLineColor);
			}
		}
	}
}
