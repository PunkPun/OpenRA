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
using OpenRA.Mods.Common.Traits;
using OpenRA.Primitives;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Activities
{
	public enum EnterBehaviour { Exit, Suicide, Dispose }

	public abstract class Enter : Activity
	{
		enum EnterState { Approaching, Entering, Exiting, Finished }

		readonly IMove move;
		readonly Color? targetLineColor;

		Target target;
		Target lastVisibleTarget;
		bool useLastVisibleTarget;
		EnterState lastState = EnterState.Approaching;

		protected Enter(Actor self, in Target target, Color? targetLineColor = null)
			: base(self)
		{
			move = self.Trait<IMove>();
			this.target = target;
			this.targetLineColor = targetLineColor;
			ChildHasPriority = false;
		}

		/// <summary>
		/// Called early in the activity tick to allow subclasses to update state.
		/// Call Cancel(self, true) if it is no longer valid to enter
		/// </summary>
		protected virtual void TickInner(in Target target, bool targetIsDeadOrHiddenActor) { }

		/// <summary>
		/// Called when the actor is ready to transition from approaching to entering the target actor.
		/// Return true to start entering, or false to wait in the WaitingToEnter state.
		/// Call Cancel(self, true) before returning false if it is no longer valid to enter
		/// </summary>
		protected virtual bool TryStartEnter(Actor targetActor) { return true; }

		/// <summary>
		/// Called when the actor has entered the target actor.
		/// Actor will be Killed/Disposed or they will enter/exit unharmed.
		/// Depends on either the EnterBehaviour of the actor or the requirements of an overriding function.
		/// </summary>
		protected virtual void OnEnterComplete(Actor targetActor) { }

		public override bool Tick()
		{
			// Update our view of the target
			target = target.Recalculate(Actor.Owner, out var targetIsHiddenActor);
			if (!targetIsHiddenActor && target.Type == TargetType.Actor)
				lastVisibleTarget = Target.FromTargetPositions(target);

			useLastVisibleTarget = targetIsHiddenActor || !target.IsValidFor(Actor);

			// Cancel immediately if the target died while we were entering it
			if (!IsCanceling && useLastVisibleTarget && lastState == EnterState.Entering)
				Cancel(true);

			TickInner(target, useLastVisibleTarget);

			// We need to wait for movement to finish before transitioning to
			// the next state or next activity
			if (!TickChild())
				return false;

			// Note that lastState refers to what we have just *finished* doing
			switch (lastState)
			{
				case EnterState.Approaching:
				{
					// NOTE: We can safely cancel in this case because we know the
					// actor has finished any in-progress move activities
					if (IsCanceling)
						return true;

					// Lost track of the target
					if (useLastVisibleTarget && lastVisibleTarget.Type == TargetType.Invalid)
						return true;

					// We are not next to the target - lets fix that
					if (target.Type != TargetType.Invalid && !move.CanEnterTargetNow(target))
					{
						// Target lines are managed by this trait, so we do not pass targetLineColor
						var initialTargetPosition = (useLastVisibleTarget ? lastVisibleTarget : target).CenterPosition;
						QueueChild(move.MoveToTarget(target, initialTargetPosition));
						return false;
					}

					// We are next to where we thought the target should be, but it isn't here
					// There's not much more we can do here
					if (useLastVisibleTarget || target.Type != TargetType.Actor)
						return true;

					// Are we ready to move into the target?
					if (TryStartEnter(target.Actor))
					{
						lastState = EnterState.Entering;
						QueueChild(move.MoveIntoTarget(target));
						return false;
					}

					// Subclasses can cancel the activity during TryStartEnter
					// Return immediately to avoid an extra tick's delay
					if (IsCanceling)
						return true;

					return false;
				}

				case EnterState.Entering:
				{
					// Check that we reached the requested position
					var targetPos = target.Positions.PositionClosestTo(Actor.CenterPosition);
					if (!IsCanceling && Actor.CenterPosition == targetPos && target.Type == TargetType.Actor)
						OnEnterComplete(target.Actor);

					lastState = EnterState.Exiting;
					return false;
				}

				case EnterState.Exiting:
				{
					QueueChild(move.ReturnToCell());
					lastState = EnterState.Finished;
					return false;
				}
			}

			return true;
		}

		public override IEnumerable<TargetLineNode> TargetLineNodes()
		{
			if (targetLineColor != null)
				yield return new TargetLineNode(useLastVisibleTarget ? lastVisibleTarget : target, targetLineColor.Value);
		}
	}
}
