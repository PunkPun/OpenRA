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
	public class HarvestResource : Activity
	{
		readonly Harvester harv;
		readonly HarvesterInfo harvInfo;
		readonly IFacing facing;
		readonly ResourceClaimLayer claimLayer;
		readonly IResourceLayer resourceLayer;
		readonly BodyOrientation body;
		readonly IMove move;
		readonly CPos targetCell;
		readonly INotifyHarvesterAction[] notifyHarvesterActions;

		public HarvestResource(Actor self, CPos targetCell)
			: base(self)
		{
			harv = self.Trait<Harvester>();
			harvInfo = self.Info.TraitInfo<HarvesterInfo>();
			facing = self.Trait<IFacing>();
			body = self.Trait<BodyOrientation>();
			move = self.Trait<IMove>();
			claimLayer = self.World.WorldActor.Trait<ResourceClaimLayer>();
			resourceLayer = self.World.WorldActor.Trait<IResourceLayer>();
			this.targetCell = targetCell;
			notifyHarvesterActions = self.TraitsImplementing<INotifyHarvesterAction>().ToArray();
		}

		protected override void OnFirstRun()
		{
			// We can safely assume the claim is successful, since this is only called in the
			// same actor-tick as the targetCell is selected. Therefore no other harvester
			// would have been able to claim.
			claimLayer.TryClaimCell(Actor, targetCell);
		}

		public override bool Tick()
		{
			if (harv.IsTraitDisabled)
				Cancel(true);

			if (IsCanceling || harv.IsFull)
				return true;

			// Move towards the target cell
			if (Actor.Location != targetCell)
			{
				foreach (var n in notifyHarvesterActions)
					n.MovingToResources(targetCell);

				QueueChild(move.MoveTo(targetCell, 0));
				return false;
			}

			if (!harv.CanHarvestCell(Actor.Location))
				return true;

			// Turn to one of the harvestable facings
			if (harvInfo.HarvestFacings != 0)
			{
				var current = facing.Facing;
				var desired = body.QuantizeFacing(current, harvInfo.HarvestFacings);
				if (desired != current)
				{
					QueueChild(new Turn(Actor, desired));
					return false;
				}
			}

			var resource = resourceLayer.GetResource(Actor.Location);
			if (resource.Type == null || resourceLayer.RemoveResource(resource.Type, Actor.Location) != 1)
				return true;

			harv.AcceptResource(Actor, resource.Type);

			foreach (var t in notifyHarvesterActions)
				t.Harvested(resource.Type);

			QueueChild(new Wait(Actor, harvInfo.BaleLoadDelay));
			return false;
		}

		protected override void OnLastRun()
		{
			claimLayer.RemoveClaim(Actor);
		}

		public override void Cancel(bool keepQueue = false)
		{
			foreach (var n in notifyHarvesterActions)
				n.MovementCancelled();

			base.Cancel(keepQueue);
		}

		public override IEnumerable<TargetLineNode> TargetLineNodes()
		{
			yield return new TargetLineNode(Target.FromCell(Actor.World, targetCell), harvInfo.HarvestLineColor);
		}
	}
}
