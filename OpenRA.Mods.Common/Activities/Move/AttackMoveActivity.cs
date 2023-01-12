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
	public class AttackMoveActivity : Activity
	{
		readonly Func<Activity> getMove;
		readonly bool isAssaultMove;
		readonly AutoTarget autoTarget;
		readonly AttackMove attackMove;

		bool runningMoveActivity = false;
		int token = Actor.InvalidConditionToken;
		Target target = Target.Invalid;

		public AttackMoveActivity(Actor self, Func<Activity> getMove, bool assaultMoving = false)
			: base(self)
		{
			this.getMove = getMove;
			autoTarget = self.TraitOrDefault<AutoTarget>();
			attackMove = self.TraitOrDefault<AttackMove>();
			isAssaultMove = assaultMoving;
			ChildHasPriority = false;
		}

		protected override void OnFirstRun()
		{
			if (attackMove == null || autoTarget == null)
			{
				QueueChild(getMove());
				return;
			}

			if (isAssaultMove)
				token = Actor.GrantCondition(attackMove.Info.AssaultMoveCondition);
			else
				token = Actor.GrantCondition(attackMove.Info.AttackMoveCondition);
		}

		public override bool Tick()
		{
			if (IsCanceling || attackMove == null || autoTarget == null)
				return TickChild();

			// We are currently not attacking, so scan for new targets.
			if (ChildActivity == null || runningMoveActivity)
			{
				// Use the standard ScanForTarget rate limit while we are running the move activity to save performance.
				// Override the rate limit if our attack activity has completed so we can immediately acquire a new target instead of moving.
				target = autoTarget.ScanForTarget(false, true, !runningMoveActivity);

				// Cancel the current move activity and queue attack activities if we find a new target.
				if (target.Type != TargetType.Invalid)
				{
					runningMoveActivity = false;
					ChildActivity?.Cancel();

					foreach (var ab in autoTarget.ActiveAttackBases)
						QueueChild(ab.GetAttackActivity(AttackSource.AttackMove, target, false, false));
				}

				// Continue with the move activity (or queue a new one) when there are no targets.
				if (ChildActivity == null)
				{
					runningMoveActivity = true;
					QueueChild(getMove());
				}
			}

			// If the move activity finished, we have reached our destination and there are no more enemies on our path.
			return TickChild() && runningMoveActivity;
		}

		protected override void OnLastRun()
		{
			if (token != Actor.InvalidConditionToken)
				token = Actor.RevokeCondition(token);
		}

		public override IEnumerable<Target> GetTargets()
		{
			if (ChildActivity != null)
				return ChildActivity.GetTargets();

			return Target.None;
		}

		public override IEnumerable<TargetLineNode> TargetLineNodes()
		{
			foreach (var n in getMove().TargetLineNodes())
				yield return n;

			yield break;
		}
	}
}
