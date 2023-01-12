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
using OpenRA.Mods.Common.Activities;
using OpenRA.Primitives;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Traits
{
	[Desc("Actor will follow units until in range to attack them.")]
	public class AttackFollowInfo : AttackBaseInfo
	{
		[Desc("Automatically acquire and fire on targets of opportunity when not actively attacking.")]
		public readonly bool OpportunityFire = true;

		[Desc("Keep firing on targets even after attack order is cancelled")]
		public readonly bool PersistentTargeting = true;

		[Desc("Range to stay away from min and max ranges to give some leeway if the target starts moving.")]
		public readonly WDist RangeMargin = WDist.FromCells(1);

		[Desc("Does this actor cancel its attack activity when it needs to resupply? Setting this to 'false' will make the actor resume attack after reloading.")]
		public readonly bool AbortOnResupply = true;

		public override object Create(ActorInitializer init) { return new AttackFollow(this, init.Self); }
	}

	public class AttackFollow : AttackBase, INotifyOwnerChanged, IOverrideAutoTarget, INotifyStanceChanged
	{
		public new readonly AttackFollowInfo Info;
		public Target RequestedTarget { get; private set; }
		public Target OpportunityTarget { get; private set; }

		Mobile mobile;
		AutoTarget autoTarget;
		bool requestedForceAttack;
		Activity requestedTargetPresetForActivity;
		bool opportunityForceAttack;
		bool opportunityTargetIsPersistentTarget;

		public void SetRequestedTarget(in Target target, bool isForceAttack = false, Activity requestedTargetPreset = null)
		{
			RequestedTarget = target;
			requestedForceAttack = isForceAttack;
			requestedTargetPresetForActivity = requestedTargetPreset;
		}

		public void ClearRequestedTarget()
		{
			if (Info.PersistentTargeting)
			{
				OpportunityTarget = RequestedTarget;
				opportunityForceAttack = requestedForceAttack;
				opportunityTargetIsPersistentTarget = true;
			}

			RequestedTarget = Target.Invalid;
			requestedTargetPresetForActivity = null;
		}

		public AttackFollow(AttackFollowInfo info, Actor self)
			: base(info, self)
		{
			Info = info;
		}

		protected override void Created()
		{
			mobile = Actor.TraitOrDefault<Mobile>();
			autoTarget = Actor.TraitOrDefault<AutoTarget>();
			base.Created();
		}

		protected bool CanAimAtTarget(in Target target, bool forceAttack)
		{
			if (target.Type == TargetType.Actor && !target.Actor.CanBeViewedByPlayer(Actor.Owner))
				return false;

			if (target.Type == TargetType.FrozenActor && !target.FrozenActor.IsValid)
				return false;

			var pos = Actor.CenterPosition;
			var armaments = ChooseArmamentsForTarget(target, forceAttack);
			foreach (var a in armaments)
				if (target.IsInRange(pos, a.MaxRange()) && (a.Weapon.MinRange == WDist.Zero || !target.IsInRange(pos, a.Weapon.MinRange)))
					if (TargetInFiringArc(target, Info.FacingTolerance))
						return true;

			return false;
		}

		protected override void Tick()
		{
			if (IsTraitDisabled)
			{
				RequestedTarget = OpportunityTarget = Target.Invalid;
				opportunityTargetIsPersistentTarget = false;
			}

			if (requestedTargetPresetForActivity != null)
			{
				// RequestedTarget was set by OnQueueAttackActivity in preparation for a queued activity
				// requestedTargetPresetForActivity will be cleared once the activity starts running and calls UpdateRequestedTarget
				if (Actor.CurrentActivity != null && Actor.CurrentActivity.NextActivity == requestedTargetPresetForActivity)
				{
					RequestedTarget = RequestedTarget.Recalculate(Actor.Owner, out _);
				}

				// Requested activity has been canceled
				else
					ClearRequestedTarget();
			}

			// Can't fire on anything
			if (mobile != null && !mobile.CanInteractWithGroundLayer())
				return;

			if (RequestedTarget.IsValidFor(Actor))
			{
				IsAiming = CanAimAtTarget(RequestedTarget, requestedForceAttack);
				if (IsAiming)
					DoAttack(RequestedTarget);
			}
			else
			{
				IsAiming = false;

				if (OpportunityTarget.IsValidFor(Actor))
					IsAiming = CanAimAtTarget(OpportunityTarget, opportunityForceAttack);

				if (!IsAiming && Info.OpportunityFire && autoTarget != null &&
				    !autoTarget.IsTraitDisabled && autoTarget.Stance >= UnitStance.Defend)
				{
					OpportunityTarget = autoTarget.ScanForTarget(false, false);
					opportunityForceAttack = false;
					opportunityTargetIsPersistentTarget = false;

					if (OpportunityTarget.IsValidFor(Actor))
						IsAiming = CanAimAtTarget(OpportunityTarget, opportunityForceAttack);
				}

				if (IsAiming)
					DoAttack(OpportunityTarget);
			}

			base.Tick();
		}

		public override Activity GetAttackActivity(AttackSource source, in Target newTarget, bool allowMove, bool forceAttack, Color? targetLineColor = null)
		{
			// HACK: Manually set force attacking if we persisted an opportunity target that required force attacking
			if (opportunityTargetIsPersistentTarget && opportunityForceAttack && newTarget == OpportunityTarget)
				forceAttack = true;

			return new AttackActivity(Actor, source, newTarget, allowMove, forceAttack, targetLineColor);
		}

		public override void OnResolveAttackOrder(Actor self, Activity activity, in Target target, bool queued, bool forceAttack)
		{
			// We can improve responsiveness for turreted actors by preempting
			// the last order (usually a move) and setting the target immediately
			if (!queued)
				SetRequestedTarget(target, forceAttack, activity);
		}

		public override void OnStopOrder()
		{
			RequestedTarget = OpportunityTarget = Target.Invalid;
			opportunityTargetIsPersistentTarget = false;
			base.OnStopOrder();
		}

		void INotifyOwnerChanged.OnOwnerChanged(Player oldOwner, Player newOwner)
		{
			RequestedTarget = OpportunityTarget = Target.Invalid;
			opportunityTargetIsPersistentTarget = false;
		}

		bool IOverrideAutoTarget.TryGetAutoTargetOverride(out Target target)
		{
			if (RequestedTarget.Type != TargetType.Invalid)
			{
				target = RequestedTarget;
				return true;
			}

			if (opportunityTargetIsPersistentTarget && OpportunityTarget.Type != TargetType.Invalid)
			{
				target = OpportunityTarget;
				return true;
			}

			target = Target.Invalid;
			return false;
		}

		void INotifyStanceChanged.StanceChanged(AutoTarget autoTarget, UnitStance oldStance, UnitStance newStance)
		{
			// Cancel opportunity targets when switching to a more restrictive stance if they are no longer valid for auto-targeting
			if (newStance > oldStance || opportunityForceAttack)
				return;

			if (OpportunityTarget.Type == TargetType.Actor)
			{
				var a = OpportunityTarget.Actor;
				if (!autoTarget.HasValidTargetPriority(a.Owner, a.GetEnabledTargetTypes()))
					OpportunityTarget = Target.Invalid;
			}
			else if (OpportunityTarget.Type == TargetType.FrozenActor)
			{
				var fa = OpportunityTarget.FrozenActor;
				if (!autoTarget.HasValidTargetPriority(fa.Owner, fa.TargetTypes))
					OpportunityTarget = Target.Invalid;
			}
		}

		class AttackActivity : Activity, IActivityNotifyStanceChanged
		{
			readonly AttackFollow attack;
			readonly RevealsShroud[] revealsShroud;
			readonly IMove move;
			readonly bool forceAttack;
			readonly Color? targetLineColor;
			readonly Rearmable rearmable;
			readonly AttackSource source;
			readonly bool isAircraft;

			Target target;
			Target lastVisibleTarget;
			bool useLastVisibleTarget;
			WDist lastVisibleMaximumRange;
			WDist lastVisibleMinimumRange;
			BitSet<TargetableType> lastVisibleTargetTypes;
			Player lastVisibleOwner;
			bool wasMovingWithinRange;
			bool hasTicked;
			bool returnToBase = false;

			public AttackActivity(Actor self, AttackSource source, in Target target, bool allowMove, bool forceAttack, Color? targetLineColor = null)
				: base(self)
			{
				attack = self.Trait<AttackFollow>();
				move = allowMove ? self.TraitOrDefault<IMove>() : null;
				revealsShroud = self.TraitsImplementing<RevealsShroud>().ToArray();
				rearmable = self.TraitOrDefault<Rearmable>();

				this.target = target;
				this.forceAttack = forceAttack;
				this.targetLineColor = targetLineColor;
				this.source = source;
				isAircraft = self.Info.HasTraitInfo<AircraftInfo>();

				// The target may become hidden between the initial order request and the first tick (e.g. if queued)
				// Moving to any position (even if quite stale) is still better than immediately giving up
				if ((target.Type == TargetType.Actor && target.Actor.CanBeViewedByPlayer(self.Owner))
				    || target.Type == TargetType.FrozenActor || target.Type == TargetType.Terrain)
				{
					lastVisibleTarget = Target.FromPos(target.CenterPosition);
					lastVisibleMaximumRange = attack.GetMaximumRangeVersusTarget(target);
					lastVisibleMinimumRange = attack.GetMinimumRangeVersusTarget(target);

					if (target.Type == TargetType.Actor)
					{
						lastVisibleOwner = target.Actor.Owner;
						lastVisibleTargetTypes = target.Actor.GetEnabledTargetTypes();
					}
					else if (target.Type == TargetType.FrozenActor)
					{
						lastVisibleOwner = target.FrozenActor.Owner;
						lastVisibleTargetTypes = target.FrozenActor.TargetTypes;
					}
				}
			}

			public override bool Tick()
			{
				returnToBase = false;

				if (IsCanceling)
					return true;

				// Check that AttackFollow hasn't cancelled the target by modifying attack.Target
				// Having both this and AttackFollow modify that field is a horrible hack.
				if (hasTicked && attack.RequestedTarget.Type == TargetType.Invalid)
					return true;

				if (attack.IsTraitPaused)
					return false;

				target = target.Recalculate(Actor.Owner, out var targetIsHiddenActor);
				attack.SetRequestedTarget(target, forceAttack);
				hasTicked = true;

				if (!targetIsHiddenActor && target.Type == TargetType.Actor)
				{
					lastVisibleTarget = Target.FromTargetPositions(target);
					lastVisibleMaximumRange = attack.GetMaximumRangeVersusTarget(target);
					lastVisibleMinimumRange = attack.GetMinimumRange();
					lastVisibleOwner = target.Actor.Owner;
					lastVisibleTargetTypes = target.Actor.GetEnabledTargetTypes();

					var leeway = attack.Info.RangeMargin.Length;
					if (leeway != 0 && move != null && target.Actor.Info.HasTraitInfo<IMoveInfo>())
					{
						var preferMinRange = Math.Min(lastVisibleMinimumRange.Length + leeway, lastVisibleMaximumRange.Length);
						var preferMaxRange = Math.Max(lastVisibleMaximumRange.Length - leeway, lastVisibleMinimumRange.Length);
						lastVisibleMaximumRange = new WDist((lastVisibleMaximumRange.Length - leeway).Clamp(preferMinRange, preferMaxRange));
					}
				}

				// The target may become hidden in the same tick the AttackActivity constructor is called,
				// causing lastVisible* to remain uninitialized.
				// Fix the fallback values based on the frozen actor properties
				else if (target.Type == TargetType.FrozenActor && !lastVisibleTarget.IsValidFor(Actor))
				{
					lastVisibleTarget = Target.FromTargetPositions(target);
					lastVisibleMaximumRange = attack.GetMaximumRangeVersusTarget(target);
					lastVisibleOwner = target.FrozenActor.Owner;
					lastVisibleTargetTypes = target.FrozenActor.TargetTypes;
				}

				var maxRange = lastVisibleMaximumRange;
				var minRange = lastVisibleMinimumRange;
				useLastVisibleTarget = targetIsHiddenActor || !target.IsValidFor(Actor);

				// Most actors want to be able to see their target before shooting
				if (target.Type == TargetType.FrozenActor && !attack.Info.TargetFrozenActors && !forceAttack)
				{
					var rs = revealsShroud
						.Where(t => !t.IsTraitDisabled)
						.MaxByOrDefault(s => s.Range);

					// Default to 2 cells if there are no active traits
					var sightRange = rs != null ? rs.Range : WDist.FromCells(2);
					if (sightRange < maxRange)
						maxRange = sightRange;
				}

				// If we are ticking again after previously sequencing a MoveWithRange then that move must have completed
				// Either we are in range and can see the target, or we've lost track of it and should give up
				if (wasMovingWithinRange && targetIsHiddenActor)
					return true;

				// Target is hidden or dead, and we don't have a fallback position to move towards
				if (useLastVisibleTarget && !lastVisibleTarget.IsValidFor(Actor))
					return true;

				// If all valid weapons have depleted their ammo and Rearmable trait exists, return to RearmActor to reload
				// and resume the activity after reloading if AbortOnResupply is set to 'false'
				if (rearmable != null && !useLastVisibleTarget && attack.Armaments.All(x => x.IsTraitPaused || !x.Weapon.IsValidAgainst(target, Actor.World, Actor)))
				{
					// Attack moves never resupply
					if (source == AttackSource.AttackMove)
						return true;

					// AbortOnResupply cancels the current activity (after resupplying) plus any queued activities
					if (attack.Info.AbortOnResupply)
						NextActivity?.Cancel();

					if (isAircraft)
						QueueChild(new ReturnToBase(Actor));
					else
					{
						var target = Actor.World.ActorsHavingTrait<Reservable>()
							.Where(a => !a.IsDead && a.IsInWorld
								&& a.Owner.IsAlliedWith(Actor.Owner) &&
								rearmable.Info.RearmActors.Contains(a.Info.Name))
							.OrderBy(a => a.Owner == Actor.Owner ? 0 : 1)
							.ThenBy(p => (Actor.Location - p.Location).LengthSquared)
							.FirstOrDefault();

						if (target != null)
							QueueChild(new Resupply(Actor, target, new WDist(512)));
					}

					returnToBase = true;
					return attack.Info.AbortOnResupply;
				}

				var pos = Actor.CenterPosition;
				var checkTarget = useLastVisibleTarget ? lastVisibleTarget : target;

				// We've reached the required range - if the target is visible and valid then we wait
				// otherwise if it is hidden or dead we give up
				if (checkTarget.IsInRange(pos, maxRange) && !checkTarget.IsInRange(pos, minRange))
				{
					if (useLastVisibleTarget)
						return true;

					return false;
				}

				// We can't move into range, so give up
				if (move == null || maxRange == WDist.Zero || maxRange < minRange)
					return true;

				wasMovingWithinRange = true;
				QueueChild(move.MoveWithinRange(target, minRange, maxRange, checkTarget.CenterPosition));
				return false;
			}

			protected override void OnLastRun()
			{
				// Cancel the requested target, but keep firing on it while in range
				attack.ClearRequestedTarget();
			}

			void IActivityNotifyStanceChanged.StanceChanged(AutoTarget autoTarget, UnitStance oldStance, UnitStance newStance)
			{
				// Cancel non-forced targets when switching to a more restrictive stance if they are no longer valid for auto-targeting
				if (newStance > oldStance || forceAttack)
					return;

				// If lastVisibleTarget is invalid we could never view the target in the first place, so we just drop it here too
				if (!lastVisibleTarget.IsValidFor(Actor) || !autoTarget.HasValidTargetPriority(lastVisibleOwner, lastVisibleTargetTypes))
					attack.ClearRequestedTarget();
			}

			public override IEnumerable<TargetLineNode> TargetLineNodes()
			{
				if (targetLineColor != null)
				{
					if (returnToBase)
						foreach (var n in ChildActivity.TargetLineNodes())
							yield return n;
					if (!returnToBase || !attack.Info.AbortOnResupply)
						yield return new TargetLineNode(useLastVisibleTarget ? lastVisibleTarget : target, targetLineColor.Value);
				}
			}
		}
	}
}
