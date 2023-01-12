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
using OpenRA.Mods.Common.Pathfinder;
using OpenRA.Mods.Common.Traits;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Activities
{
	public class FindAndDeliverResources : Activity
	{
		readonly Harvester harv;
		readonly HarvesterInfo harvInfo;
		readonly Mobile mobile;
		readonly ResourceClaimLayer claimLayer;

		Actor deliverActor;
		CPos? orderLocation;
		CPos? lastHarvestedCell;
		bool hasDeliveredLoad;
		bool hasHarvestedCell;
		bool hasWaited;

		public bool LastSearchFailed { get; private set; }

		public FindAndDeliverResources(Actor self, Actor deliverActor = null)
			: base(self)
		{
			harv = self.Trait<Harvester>();
			harvInfo = self.Info.TraitInfo<HarvesterInfo>();
			mobile = self.Trait<Mobile>();
			claimLayer = self.World.WorldActor.Trait<ResourceClaimLayer>();
			this.deliverActor = deliverActor;
		}

		public FindAndDeliverResources(Actor self, CPos orderLocation)
			: this(self, null)
		{
			this.orderLocation = orderLocation;
		}

		protected override void OnFirstRun()
		{
			// If an explicit "harvest" order is given, direct the harvester to the ordered location instead of
			// the previous harvested cell for the initial search.
			if (orderLocation != null)
			{
				lastHarvestedCell = orderLocation;

				// If two "harvest" orders are issued consecutively, we deliver the load first if needed.
				// We have to make sure the actual "harvest" order is not skipped if a third order is queued,
				// so we keep deliveredLoad false.
				if (harv.IsFull)
					QueueChild(new DeliverResources(Actor));
			}

			// If an explicit "deliver" order is given, the harvester goes immediately to the refinery.
			if (deliverActor != null)
			{
				QueueChild(new DeliverResources(Actor, deliverActor));
				hasDeliveredLoad = true;
				deliverActor = null;
			}
		}

		public override bool Tick()
		{
			if (IsCanceling || harv.IsTraitDisabled)
				return true;

			if (NextActivity != null)
			{
				// Interrupt automated harvesting after clearing the first cell.
				if (!harvInfo.QueueFullLoad && (hasHarvestedCell || LastSearchFailed))
					return true;

				// Interrupt automated harvesting after first complete harvest cycle.
				if (hasDeliveredLoad || harv.IsFull)
					return true;
			}

			// Are we full or have nothing more to gather? Deliver resources.
			if (harv.IsFull || (!harv.IsEmpty && LastSearchFailed))
			{
				QueueChild(new DeliverResources(Actor));
				hasDeliveredLoad = true;
				return false;
			}

			// After a failed search, wait and sit still for a bit before searching again.
			if (LastSearchFailed && !hasWaited)
			{
				QueueChild(new Wait(Actor, harv.Info.WaitDuration));
				hasWaited = true;
				return false;
			}

			hasWaited = false;

			// Scan for resources. If no resources are found near the current field, search near the refinery
			// instead. If that doesn't help, give up for now.
			var closestHarvestableCell = ClosestHarvestablePos();
			if (!closestHarvestableCell.HasValue)
			{
				if (lastHarvestedCell != null)
				{
					lastHarvestedCell = null; // Forces search from backup position.
					closestHarvestableCell = ClosestHarvestablePos();
					LastSearchFailed = !closestHarvestableCell.HasValue;
				}
				else
					LastSearchFailed = true;
			}
			else
				LastSearchFailed = false;

			// If no harvestable position could be found and we are at the refinery, get out of the way
			// of the refinery entrance.
			if (LastSearchFailed)
			{
				var lastproc = harv.LastLinkedProc ?? harv.LinkedProc;
				if (lastproc != null && !lastproc.Disposed)
				{
					var deliveryLoc = lastproc.Location + lastproc.Trait<IAcceptResources>().DeliveryOffset;
					if (Actor.Location == deliveryLoc && harv.IsEmpty)
					{
						var unblockCell = deliveryLoc + harv.Info.UnblockCell;
						var moveTo = mobile.NearestMoveableCell(unblockCell, 1, 5);
						QueueChild(mobile.MoveTo(moveTo, 1));
					}
				}

				return false;
			}

			// If we get here, our search for resources was successful. Commence harvesting.
			QueueChild(new HarvestResource(Actor, closestHarvestableCell.Value));
			lastHarvestedCell = closestHarvestableCell.Value;
			hasHarvestedCell = true;
			return false;
		}

		/// <summary>
		/// Finds the closest harvestable pos between the current position of the harvester
		/// and the last order location
		/// </summary>
		CPos? ClosestHarvestablePos()
		{
			// Harvesters should respect an explicit harvest order instead of harvesting the current cell.
			if (orderLocation == null)
			{
				if (harv.CanHarvestCell(Actor.Location) && claimLayer.CanClaimCell(Actor, Actor.Location))
					return Actor.Location;
			}
			else
			{
				if (harv.CanHarvestCell(orderLocation.Value) && claimLayer.CanClaimCell(Actor, orderLocation.Value))
					return orderLocation;

				orderLocation = null;
			}

			// Determine where to search from and how far to search:
			var procLoc = GetSearchFromProcLocation();
			var searchFromLoc = lastHarvestedCell ?? procLoc ?? Actor.Location;
			var searchRadius = lastHarvestedCell.HasValue ? harvInfo.SearchFromHarvesterRadius : harvInfo.SearchFromProcRadius;

			var searchRadiusSquared = searchRadius * searchRadius;

			var map = Actor.World.Map;
			var procPos = procLoc.HasValue ? (WPos?)map.CenterOfCell(procLoc.Value) : null;
			var harvPos = Actor.CenterPosition;

			// Find any harvestable resources:
			var path = mobile.PathFinder.FindPathToTargetCellByPredicate(
				new[] { searchFromLoc, Actor.Location },
				loc =>
					harv.CanHarvestCell(loc) &&
					claimLayer.CanClaimCell(Actor, loc),
				BlockedByActor.Stationary,
				loc =>
				{
					if ((loc - searchFromLoc).LengthSquared > searchRadiusSquared)
						return PathGraph.PathCostForInvalidPath;

					// Add a cost modifier to harvestable cells to prefer resources that are closer to the refinery.
					// This reduces the tendency for harvesters to move in straight lines
					if (procPos.HasValue && harvInfo.ResourceRefineryDirectionPenalty > 0 && harv.CanHarvestCell(loc))
					{
						var pos = map.CenterOfCell(loc);

						// Calculate harv-cell-refinery angle (cosine rule)
						var b = pos - procPos.Value;

						if (b != WVec.Zero)
						{
							var c = pos - harvPos;
							if (c != WVec.Zero)
							{
								var a = harvPos - procPos.Value;
								var cosA = (int)(512 * (b.LengthSquared + c.LengthSquared - a.LengthSquared) / b.Length / c.Length);

								// Cost modifier varies between 0 and ResourceRefineryDirectionPenalty
								return Math.Abs(harvInfo.ResourceRefineryDirectionPenalty / 2) + harvInfo.ResourceRefineryDirectionPenalty * cosA / 2048;
							}
						}
					}

					return 0;
				});

			if (path.Count > 0)
				return path[0];

			return null;
		}

		public override IEnumerable<Target> GetTargets()
		{
			yield return Target.FromCell(Actor.World, Actor.Location);
		}

		public override IEnumerable<TargetLineNode> TargetLineNodes()
		{
			if (ChildActivity != null)
				foreach (var n in ChildActivity.TargetLineNodes())
					yield return n;

			if (orderLocation != null)
				yield return new TargetLineNode(Target.FromCell(Actor.World, orderLocation.Value), harvInfo.HarvestLineColor);
			else if (deliverActor != null)
				yield return new TargetLineNode(Target.FromActor(deliverActor), harvInfo.DeliverLineColor);
		}

		CPos? GetSearchFromProcLocation()
		{
			if (harv.LastLinkedProc != null && !harv.LastLinkedProc.IsDead && harv.LastLinkedProc.IsInWorld)
				return harv.LastLinkedProc.Location + harv.LastLinkedProc.Trait<IAcceptResources>().DeliveryOffset;

			if (harv.LinkedProc != null && !harv.LinkedProc.IsDead && harv.LinkedProc.IsInWorld)
				return harv.LinkedProc.Location + harv.LinkedProc.Trait<IAcceptResources>().DeliveryOffset;

			return null;
		}
	}
}
