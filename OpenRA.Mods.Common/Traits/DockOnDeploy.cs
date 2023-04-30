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
using OpenRA.Activities;
using OpenRA.Mods.Common.Activities;
using OpenRA.Mods.Common.Orders;
using OpenRA.Primitives;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Traits
{
	[Desc("Finds the closest dock on deploy.")]
	public class DockOnDeployInfo : ConditionalTraitInfo, Requires<DockClientManagerInfo>
	{
		[FieldLoader.Require]
		[Desc("Docking type.")]
		public readonly BitSet<DockType> DockType;

		[CursorReference]
		[Desc("Cursor to display when able to (un)deploy the actor.")]
		public readonly string DeployCursor = "deploy";

		[CursorReference]
		[Desc("Cursor to display when unable to (un)deploy the actor.")]
		public readonly string DeployBlockedCursor = "deploy-blocked";

		public override object Create(ActorInitializer init) { return new DockOnDeploy(init.Self, this); }
	}

	public class DockOnDeploy : ConditionalTrait<DockOnDeployInfo>, IIssueDeployOrder, IResolveOrder, IOrderVoice, IIssueOrder
	{
		readonly DockClientManager manager;
		public DockOnDeploy(Actor self, DockOnDeployInfo info)
			: base(info)
		{
			manager = self.Trait<DockClientManager>();
		}

		public IEnumerable<IOrderTargeter> Orders
		{
			get
			{
				yield return new DeployOrderTargeter("FindDock", 10,
					() => !IsTraitDisabled && manager.DockingPossible(Info.DockType) ? Info.DeployCursor : Info.DeployBlockedCursor);
			}
		}

		public Order IssueOrder(Actor self, IOrderTargeter order, in Target target, bool queued)
		{
			if (order.OrderID == "FindDock")
				return new Order(order.OrderID, self, queued);

			return null;
		}

		Order IIssueDeployOrder.IssueDeployOrder(Actor self, bool queued)
		{
			return new Order("FindDock", self, queued);
		}

		bool IIssueDeployOrder.CanIssueDeployOrder(Actor self, bool queued) { return !IsTraitDisabled && manager.DockingPossible(Info.DockType, queued); }

		void IResolveOrder.ResolveOrder(Actor self, Order order)
		{
			if (order.OrderString == "FindDock")
			{
				self.QueueActivity(order.Queued, new FindDock(this, manager));
				self.ShowTargetLines();
			}
		}

		string IOrderVoice.VoicePhraseForOrder(Actor self, Order order)
		{
			if (order.OrderString == "FindDock")
				return manager.Info.Voice;

			return null;
		}
	}

	public class FindDock : Activity
	{
		readonly DockOnDeploy dockOnDeploy;
		readonly DockClientManager manager;
		public FindDock(DockOnDeploy dockOnDeploy, DockClientManager manager)
		{
			this.dockOnDeploy = dockOnDeploy;
			this.manager = manager;
		}

		protected override void OnFirstRun(Actor self)
		{
			if (dockOnDeploy.IsTraitDisabled || !manager.DockingPossible(dockOnDeploy.Info.DockType))
				return;

			var dock = manager.ClosestDock(null, dockOnDeploy.Info.DockType) ?? manager.ClosestDock(null, dockOnDeploy.Info.DockType, ignoreOccupancy: true);
			if (!dock.HasValue)
				return;

			QueueChild(new MoveToDock(self, dock.Value.Actor, dock.Value.Trait));
		}

		public override IEnumerable<Target> GetTargets(Actor self)
		{
			if (ChildActivity != null)
				foreach (var target in ChildActivity.GetTargets(self))
					yield return target;
		}

		public override IEnumerable<TargetLineNode> TargetLineNodes(Actor self)
		{
			if (ChildActivity != null)
				foreach (var node in ChildActivity.TargetLineNodes(self))
					yield return node;
		}
	}
}
