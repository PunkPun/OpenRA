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

using OpenRA.Mods.Common.Traits;
using OpenRA.Primitives;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Activities
{
	public class MoveOnto : MoveAdjacentTo
	{
		readonly WVec offset = WVec.Zero;

		public MoveOnto(Actor self, in Target target, WVec? offset = null, WPos? initialTargetPosition = null, Color? targetLineColor = null)
			: base(self, target, initialTargetPosition, targetLineColor)
		{
			if (offset.HasValue)
				this.offset = offset.Value;
		}

		protected override void SetVisibleTargetLocation(Actor self, Target target)
		{
			lastVisibleTargetLocation = self.World.Map.CellContaining(Target.CenterPosition + offset);
		}

		protected override bool ShouldStop(Actor self)
		{
			// If we are right next to the Actor and target type is still not Actor, we assume the target is dead
			return Target.Type != TargetType.Actor && Util.AreAdjacentCells(lastVisibleTargetLocation, self.Location);
		}

		protected override bool UpdateSearchCells(Actor self)
		{
			// If we are close to the target but can't enter, we wait
			if (!Mobile.CanEnterCell(lastVisibleTargetLocation) && Util.AreAdjacentCells(lastVisibleTargetLocation, self.Location))
				return true;

			if (!SearchCells.Contains(lastVisibleTargetLocation))
			{
				SearchCells.Clear();
				SearchCells.Add(lastVisibleTargetLocation);
			}

			return false;
		}
	}
}
