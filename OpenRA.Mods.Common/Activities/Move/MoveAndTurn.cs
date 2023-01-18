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
using OpenRA.Primitives;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Activities
{
	public class MoveAndTurn : Activity
	{
		readonly Target target;
		readonly Color? targetLineColor;
		readonly WDist closeEnough;
		readonly WAngle[] facings;

		public MoveAndTurn(int closeEnough, in Target target, WAngle[] facings, Color? targetLineColor = null)
		{
			this.target = target;
			this.facings = facings;
			this.closeEnough = WDist.FromCells(closeEnough);
			this.targetLineColor = targetLineColor;
		}

		protected override void OnFirstRun(Actor self)
		{
			QueueChild(new Move(self, self.World.Map.CellContaining(target.Actor.CenterPosition + (WVec)target.Positions.First()), closeEnough, target.Actor, false, targetLineColor));
		}

		public override bool Tick(Actor self)
		{
			var facing = self.Trait<IFacing>().Facing;
			var closestFacing = facings.MinBy((f) => Math.Abs(f.Angle - facing.Angle));
			if (facing != closestFacing)
			{
				QueueChild(new Turn(self, closestFacing));
				return false;
			}

			return true;
		}

		public override IEnumerable<Target> GetTargets(Actor self)
		{
			yield return target;
		}

		public override IEnumerable<TargetLineNode> TargetLineNodes(Actor self)
		{
			if (targetLineColor != null)
				yield return new TargetLineNode(target, targetLineColor.Value);
		}
	}
}
