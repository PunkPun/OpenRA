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
using OpenRA.Mods.Common.Traits;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Activities
{
	public class FindAndDeliverResources : Activity
	{
		readonly Harvester harv;
		readonly HarvesterInfo harvInfo;
		readonly Mobile mobile;
		readonly IMove move;
		readonly ResourceClaimLayer claimLayer;
		CPos? orderLocation;
		CPos? lastHarvestedCell;
		bool hasDeliveredLoad;
		bool hasHarvestedCell;
		bool hasWaited;

		public bool LastSearchFailed { get; private set; }

		public FindAndDeliverResources(Actor self, CPos? orderLocation = null)
		{
			harv = self.Trait<Harvester>();
			harvInfo = self.Info.TraitInfo<HarvesterInfo>();
			move = self.Trait<IMove>();
			mobile = move as Mobile;
			claimLayer = self.World.WorldActor.Trait<ResourceClaimLayer>();
			if (orderLocation.HasValue)
				this.orderLocation = orderLocation.Value;
		}

		protected override void OnFirstRun(Actor self)
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
					QueueChild(new MoveToDock(self));
			}
		}

		public override bool Tick(Actor self)
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
				QueueChild(new MoveToDock(self));
				hasDeliveredLoad = true;
				return false;
			}

			// After a failed search, wait and sit still for a bit before searching again.
			if (LastSearchFailed && !hasWaited)
			{
				QueueChild(new Wait(harv.Info.WaitDuration));
				hasWaited = true;
				return false;
			}

			hasWaited = false;

			// Scan for resources. If no resources are found near the current field, search near the refinery
			// instead. If that doesn't help, give up for now.
			var closestHarvestableCell = ClosestHarvestablePos(self);
			if (!closestHarvestableCell.HasValue)
			{
				if (lastHarvestedCell != null)
				{
					lastHarvestedCell = null; // Forces search from backup position.
					closestHarvestableCell = ClosestHarvestablePos(self);
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
				var lastproc = harv.DockClientManager.LastReservedHost;
				if (lastproc != null)
				{
					var deliveryLoc = lastproc.DockPosition;
					if (self.CenterPosition == deliveryLoc && harv.IsEmpty)
					{
						var unblockCell = self.World.Map.CellContaining(deliveryLoc) + harv.Info.UnblockCell;
						var moveTo = mobile.NearestMoveableCell(unblockCell, 1, 5);
						QueueChild(mobile.MoveTo(moveTo, 1));
					}
				}

				return false;
			}

			// If we get here, our search for resources was successful. Commence harvesting.
			QueueChild(new HarvestResource(self, closestHarvestableCell.Value));
			lastHarvestedCell = closestHarvestableCell.Value;
			hasHarvestedCell = true;
			return false;
		}

		/// <summary>
		/// Finds the closest harvestable pos between the current position of the harvester
		/// and the last order location
		/// </summary>
		CPos? ClosestHarvestablePos(Actor self)
		{
			// Harvesters should respect an explicit harvest order instead of harvesting the current cell.
			if (orderLocation == null)
			{
				if (harv.CanHarvestCell(self.Location) && claimLayer.CanClaimCell(self, self.Location))
					return self.Location;
			}
			else
			{
				if (harv.CanHarvestCell(orderLocation.Value) && claimLayer.CanClaimCell(self, orderLocation.Value))
					return orderLocation;

				orderLocation = null;
			}

			// Determine where to search from and how far to search:
			var procLoc = GetSearchFromProcLocation(self);
			var searchFromLoc = lastHarvestedCell ?? procLoc ?? self.Location;
			var searchRadius = lastHarvestedCell.HasValue ? harvInfo.SearchFromHarvesterRadius : harvInfo.SearchFromProcRadius;

			var searchRadiusSquared = searchRadius * searchRadius;

			var map = self.World.Map;
			var procPos = procLoc.HasValue ? (WPos?)map.CenterOfCell(procLoc.Value) : null;
			var harvPos = self.CenterPosition;

			// Find any harvestable resources:
			if (mobile != null)
			{
				var path = mobile.PathFinder.FindPathToTargetCellByPredicate(
					self,
					new[] { searchFromLoc, self.Location },
					loc =>
						harv.CanHarvestCell(loc) &&
						claimLayer.CanClaimCell(self, loc) &&
						(loc - searchFromLoc).LengthSquared < searchRadiusSquared,
					BlockedByActor.Stationary,
					loc => LocationWeight(loc, procPos, self));

				if (path.Count > 0)
					return path[0];
			}
			else
			{
				var pos = FindTileInCircle(self, searchFromLoc, searchRadius, procPos);
				if (pos == null && searchFromLoc != self.Location)
					pos = FindTileInCircle(self, self.Location, searchRadius, procPos);

				return pos;
			}

			return null;
		}

		CPos? FindTileInCircle(Actor self, CPos target, int searchRadius, WPos? procPos)
		{
			return self.World.Map.FindTileInCircle(target, searchRadius, (c) => harv.CanHarvestCell(c) && claimLayer.CanClaimCell(self, c), (c) => LocationWeight(c, procPos, self));
		}

		int LocationWeight(CPos loc, WPos? procPos, Actor harv)
		{
			// Add a cost modifier to harvestable cells to prefer resources that are closer to the refinery.
			// This reduces the tendency for harvesters to move in straight lines
			if (procPos.HasValue && harvInfo.ResourceRefineryDirectionPenalty > 0)
			{
				var harvPos = harv.CenterPosition;
				var pos = harv.World.Map.CenterOfCell(loc);

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
		}

		public override IEnumerable<Target> GetTargets(Actor self)
		{
			yield return Target.FromCell(self.World, self.Location);
		}

		public override IEnumerable<TargetLineNode> TargetLineNodes(Actor self)
		{
			if (ChildActivity != null)
				foreach (var n in ChildActivity.TargetLineNodes(self))
					yield return n;

			if (orderLocation != null)
				yield return new TargetLineNode(Target.FromCell(self.World, orderLocation.Value), harvInfo.HarvestLineColor);
			else
			{
				var manager = harv.DockClientManager;
				if (manager.ReservedHostActor != null)
					yield return new TargetLineNode(Target.FromActor(manager.ReservedHostActor), manager.DockLineColor);
			}
		}

		CPos? GetSearchFromProcLocation(Actor self)
		{
			var dock = harv.DockClientManager.LastReservedHost;
			if (dock != null)
				return self.World.Map.CellContaining(dock.DockPosition);

			return null;
		}
	}
}
