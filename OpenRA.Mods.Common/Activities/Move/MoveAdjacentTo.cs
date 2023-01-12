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
	public class MoveAdjacentTo : Activity
	{
		protected readonly Mobile Mobile;
		readonly Color? targetLineColor;

		protected Target Target => useLastVisibleTarget ? lastVisibleTarget : target;

		Target target;
		Target lastVisibleTarget;
		protected CPos lastVisibleTargetLocation;
		bool useLastVisibleTarget;

		public MoveAdjacentTo(Actor self, in Target target, WPos? initialTargetPosition = null, Color? targetLineColor = null)
			: base(self)
		{
			this.target = target;
			this.targetLineColor = targetLineColor;
			Mobile = self.Trait<Mobile>();
			ChildHasPriority = false;

			// The target may become hidden between the initial order request and the first tick (e.g. if queued)
			// Moving to any position (even if quite stale) is still better than immediately giving up
			if ((target.Type == TargetType.Actor && target.Actor.CanBeViewedByPlayer(self.Owner))
			    || target.Type == TargetType.FrozenActor || target.Type == TargetType.Terrain)
			{
				lastVisibleTarget = Target.FromPos(target.CenterPosition);
				lastVisibleTargetLocation = self.World.Map.CellContaining(target.CenterPosition);
			}
			else if (initialTargetPosition.HasValue)
			{
				lastVisibleTarget = Target.FromPos(initialTargetPosition.Value);
				lastVisibleTargetLocation = self.World.Map.CellContaining(initialTargetPosition.Value);
			}
		}

		protected virtual bool ShouldStop()
		{
			return false;
		}

		protected virtual bool ShouldRepath(CPos targetLocation)
		{
			return lastVisibleTargetLocation != targetLocation;
		}

		protected virtual IEnumerable<CPos> CandidateMovementCells()
		{
			return Util.AdjacentCells(Actor.World, Target)
				.Where(c => Mobile.CanStayInCell(c));
		}

		protected override void OnFirstRun()
		{
			QueueChild(Mobile.MoveTo(check => CalculatePathToTarget(check)));
		}

		public override bool Tick()
		{
			var oldTargetLocation = lastVisibleTargetLocation;
			target = target.Recalculate(Actor.Owner, out var targetIsHiddenActor);
			if (!targetIsHiddenActor && target.Type == TargetType.Actor)
			{
				lastVisibleTarget = Target.FromTargetPositions(target);
				lastVisibleTargetLocation = Actor.World.Map.CellContaining(target.CenterPosition);
			}

			// Target is equivalent to checkTarget variable in other activities
			// value is either lastVisibleTarget or target based on visibility and validity
			var targetIsValid = Target.IsValidFor(Actor);
			useLastVisibleTarget = targetIsHiddenActor || !targetIsValid;

			// Target is hidden or dead, and we don't have a fallback position to move towards
			var noTarget = useLastVisibleTarget && !lastVisibleTarget.IsValidFor(Actor);

			// Cancel the current path if the activity asks to stop, or asks to repath
			// The repath happens once the move activity stops in the next cell
			var shouldRepath = targetIsValid && ShouldRepath(oldTargetLocation);
			if (ChildActivity != null && (ShouldStop() || shouldRepath || noTarget))
				ChildActivity.Cancel();

			// Target has moved, and MoveAdjacentTo is still valid.
			if (!IsCanceling && shouldRepath)
				QueueChild(Mobile.MoveTo(check => CalculatePathToTarget(check)));

			// The last queued childactivity is guaranteed to be the inner move, so if the childactivity
			// queue is empty it means we have reached our destination.
			return TickChild();
		}

		readonly List<CPos> searchCells = new List<CPos>();
		int searchCellsTick = -1;

		List<CPos> CalculatePathToTarget(BlockedByActor check)
		{
			var loc = Actor.Location;

			// PERF: Assume that CandidateMovementCells doesn't change within a tick to avoid repeated queries
			// when Move enumerates different BlockedByActor values
			if (searchCellsTick != Actor.World.WorldTick)
			{
				searchCells.Clear();
				searchCellsTick = Actor.World.WorldTick;
				foreach (var cell in CandidateMovementCells())
					if (Mobile.CanEnterCell(cell))
						searchCells.Add(cell);
			}

			if (searchCells.Count == 0)
				return PathFinder.NoPath;

			var path = Mobile.PathFinder.FindPathToTargetCell(searchCells, loc, check);
			path.Reverse();
			return path;
		}

		public override IEnumerable<Target> GetTargets()
		{
			if (ChildActivity != null)
				return ChildActivity.GetTargets();

			return Target.None;
		}

		public override IEnumerable<TargetLineNode> TargetLineNodes()
		{
			if (targetLineColor.HasValue)
				yield return new TargetLineNode(useLastVisibleTarget ? lastVisibleTarget : target, targetLineColor.Value);
		}
	}
}
