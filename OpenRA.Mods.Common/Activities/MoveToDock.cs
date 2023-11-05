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
using OpenRA.Primitives;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Activities
{
	public class MoveToDock : Activity
	{
		readonly DockClientManager dockClient;
		(Actor Actor, IDockHost Trait) dockHost;
		readonly BitSet<DockType> type;
		readonly INotifyDockClientMoving[] notifyDockClientMoving;
		readonly Color? dockLineColor = null;

		public MoveToDock(Actor self, DockClientManager dockClient, Actor dockHostActor, IDockHost dockHost, Color? dockLineColor = null)
		{
			this.dockClient = dockClient;
			this.dockHost = (dockHostActor, dockHost);
			this.dockLineColor = dockLineColor;
			notifyDockClientMoving = self.TraitsImplementing<INotifyDockClientMoving>().ToArray();
		}

		public MoveToDock(Actor self, DockClientManager dockClient, BitSet<DockType> type = default, Color? dockLineColor = null)
		{
			this.dockClient = dockClient;
			this.type = type;
			this.dockLineColor = dockLineColor;
			notifyDockClientMoving = self.TraitsImplementing<INotifyDockClientMoving>().ToArray();
		}

		public override bool Tick(Actor self)
		{
			if (IsCanceling)
				return true;

			if (dockClient.IsTraitDisabled)
			{
				Cancel(self, true);
				return true;
			}

			// Find the nearest DockHost if not explicitly ordered to a specific dock.
			if (dockHost.Actor == null || !dockHost.Trait.IsEnabledAndInWorld)
			{
				var host = dockClient.ClosestDock(null, type);
				if (host.HasValue)
					dockHost = (host.Value.Actor, host.Value.Trait);
				else
				{
					// No docks exist; check again after delay defined in dockClient.
					QueueChild(new Wait(dockClient.Info.SearchForDockDelay));
					return false;
				}
			}

			if (dockClient.ReserveHost(dockHost.Actor, dockHost.Trait))
			{
				if (dockHost.Trait.QueueMoveActivity(this, dockHost.Actor, self, dockClient))
				{
					foreach (var ndcm in notifyDockClientMoving)
						ndcm.MovingToDock(self, dockHost.Actor, dockHost.Trait);

					return false;
				}

				dockHost.Trait.QueueDockActivity(this, dockHost.Actor, self, dockClient);
				return true;
			}
			else
			{
				// The dock explicitely chosen by the user is currently occupied. Wait and check again.
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
			if (!dockLineColor.HasValue)
				yield break;

			if (dockHost.Actor != null)
				yield return new TargetLineNode(Target.FromActor(dockHost.Actor), dockLineColor.Value);
			else
			{
				if (dockClient.ReservedHostActor != null)
					yield return new TargetLineNode(Target.FromActor(dockClient.ReservedHostActor), dockLineColor.Value);
			}
		}
	}
}
