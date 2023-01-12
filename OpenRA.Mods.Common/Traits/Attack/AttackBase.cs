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
	public enum AttackSource { Default, AutoTarget, AttackMove }

	public abstract class AttackBaseInfo : PausableConditionalTraitInfo
	{
		[Desc("Armament names")]
		public readonly string[] Armaments = { "primary", "secondary" };

		[CursorReference]
		[Desc("Cursor to display when hovering over a valid target.")]
		public readonly string Cursor = null;

		[CursorReference]
		[Desc("Cursor to display when hovering over a valid target that is outside of range.")]
		public readonly string OutsideRangeCursor = null;

		[Desc("Color to use for the target line.")]
		public readonly Color TargetLineColor = Color.Crimson;

		[Desc("Does the attack type require the attacker to enter the target's cell?")]
		public readonly bool AttackRequiresEnteringCell = false;

		[Desc("Allow firing into the fog to target frozen actors without requiring force-fire.")]
		public readonly bool TargetFrozenActors = false;

		[Desc("Force-fire mode ignores actors and targets the ground instead.")]
		public readonly bool ForceFireIgnoresActors = false;

		[Desc("Force-fire mode is required to enable targeting against targets outside of range.")]
		public readonly bool OutsideRangeRequiresForceFire = false;

		[VoiceReference]
		public readonly string Voice = "Action";

		[Desc("Tolerance for attack angle. Range [0, 512], 512 covers 360 degrees.")]
		public readonly WAngle FacingTolerance = new WAngle(512);

		public override void RulesetLoaded(Ruleset rules, ActorInfo ai)
		{
			base.RulesetLoaded(rules, ai);

			if (FacingTolerance.Angle > 512)
				throw new YamlException("Facing tolerance must be in range of [0, 512], 512 covers 360 degrees.");
		}

		public abstract override object Create(ActorInitializer init);
	}

	public abstract class AttackBase : PausableConditionalTrait<AttackBaseInfo>, ITick, IIssueOrder, IResolveOrder, IOrderVoice, ISync
	{
		readonly string attackOrderName = "Attack";
		readonly string forceAttackOrderName = "ForceAttack";

		[Sync]
		public bool IsAiming { get; set; }

		public IEnumerable<Armament> Armaments => getArmaments();

		protected IFacing facing;
		protected IPositionable positionable;
		protected INotifyAiming[] notifyAiming;
		protected Func<IEnumerable<Armament>> getArmaments;

		bool wasAiming;

		public AttackBase(AttackBaseInfo info, Actor self)
			: base(info, self) { }

		protected override void Created()
		{
			facing = Actor.TraitOrDefault<IFacing>();
			positionable = Actor.TraitOrDefault<IPositionable>();
			notifyAiming = Actor.TraitsImplementing<INotifyAiming>().ToArray();

			getArmaments = InitializeGetArmaments();

			base.Created();
		}

		void ITick.Tick()
		{
			Tick();
		}

		protected virtual void Tick()
		{
			if (!wasAiming && IsAiming)
				foreach (var n in notifyAiming)
					n.StartedAiming(this);
			else if (wasAiming && !IsAiming)
				foreach (var n in notifyAiming)
					n.StoppedAiming(this);

			wasAiming = IsAiming;
		}

		protected virtual Func<IEnumerable<Armament>> InitializeGetArmaments()
		{
			var armaments = Actor.TraitsImplementing<Armament>()
				.Where(a => Info.Armaments.Contains(a.Info.Name)).ToArray();

			return () => armaments;
		}

		public bool TargetInFiringArc(in Target target, WAngle facingTolerance)
		{
			if (facing == null)
				return true;

			var pos = Actor.CenterPosition;
			var targetedPosition = GetTargetPosition(pos, target);
			var delta = targetedPosition - pos;

			if (delta.HorizontalLengthSquared == 0)
				return true;

			return Util.FacingWithinTolerance(facing.Facing, delta.Yaw, facingTolerance);
		}

		protected virtual bool CanAttack(in Target target)
		{
			if (!Actor.IsInWorld || IsTraitDisabled || IsTraitPaused)
				return false;

			if (!target.IsValidFor(Actor))
				return false;

			if (!HasAnyValidWeapons(target, reloadingIsInvalid: true))
				return false;

			// PERF: Mobile implements IPositionable, so we can use 'as' to save a trait look-up here.
			if (positionable is Mobile mobile && !mobile.CanInteractWithGroundLayer())
				return false;

			return true;
		}

		public virtual void DoAttack(in Target target)
		{
			if (!CanAttack(target))
				return;

			foreach (var a in Armaments)
				a.CheckFire(facing, target);
		}

		IEnumerable<IOrderTargeter> IIssueOrder.Orders
		{
			get
			{
				if (IsTraitDisabled)
					yield break;

				if (!Armaments.Any())
					yield break;

				yield return new AttackOrderTargeter(Actor, this, 6);
			}
		}

		Order IIssueOrder.IssueOrder(IOrderTargeter order, in Target target, bool queued)
		{
			if (order is AttackOrderTargeter)
				return new Order(order.OrderID, Actor, target, queued);

			return null;
		}

		void IResolveOrder.ResolveOrder(Order order)
		{
			var forceAttack = order.OrderString == forceAttackOrderName;
			if (forceAttack || order.OrderString == attackOrderName)
			{
				if (!order.Target.IsValidFor(Actor))
					return;

				AttackTarget(order.Target, AttackSource.Default, order.Queued, true, forceAttack, Info.TargetLineColor);
				Actor.ShowTargetLines();
			}
			else if (order.OrderString == "Stop")
				OnStopOrder();
		}

		// Some 3rd-party mods rely on this being public
		public virtual void OnStopOrder()
		{
			// We don't want Stop orders from traits other than Mobile or Aircraft to cancel Resupply activity.
			// Resupply is always either the main activity or a child of ReturnToBase.
			// TODO: This should generally only cancel activities queued by this trait.
			if (Actor.CurrentActivity == null || Actor.CurrentActivity is Resupply || Actor.CurrentActivity is ReturnToBase)
				return;

			Actor.CancelActivity();
		}

		string IOrderVoice.VoicePhraseForOrder(Order order)
		{
			return order.OrderString == attackOrderName || order.OrderString == forceAttackOrderName ? Info.Voice : null;
		}

		public abstract Activity GetAttackActivity(AttackSource source, in Target newTarget, bool allowMove, bool forceAttack, Color? targetLineColor = null);

		public bool HasAnyValidWeapons(in Target t, bool checkForCenterTargetingWeapons = false, bool reloadingIsInvalid = false)
		{
			if (IsTraitDisabled)
				return false;

			if (Info.AttackRequiresEnteringCell && (positionable == null || !positionable.CanEnterCell(t.Actor.Location, null, BlockedByActor.None)))
				return false;

			// PERF: Avoid LINQ.
			foreach (var armament in Armaments)
			{
				var checkIsValid = checkForCenterTargetingWeapons ? armament.Weapon.TargetActorCenter : !armament.IsTraitPaused;
				var reloadingStateIsValid = !reloadingIsInvalid || !armament.IsReloading;
				if (checkIsValid && reloadingStateIsValid && !armament.IsTraitDisabled && armament.Weapon.IsValidAgainst(t, Actor.World, Actor))
					return true;
			}

			return false;
		}

		public virtual WPos GetTargetPosition(WPos pos, in Target target)
		{
			return HasAnyValidWeapons(target, true) ? target.CenterPosition : target.Positions.PositionClosestTo(pos);
		}

		public WDist GetMinimumRange()
		{
			if (IsTraitDisabled)
				return WDist.Zero;

			// PERF: Avoid LINQ.
			var min = WDist.MaxValue;
			foreach (var armament in Armaments)
			{
				if (armament.IsTraitDisabled)
					continue;

				if (armament.IsTraitPaused)
					continue;

				var range = armament.Weapon.MinRange;
				if (min > range)
					min = range;
			}

			return min != WDist.MaxValue ? min : WDist.Zero;
		}

		public WDist GetMaximumRange()
		{
			if (IsTraitDisabled)
				return WDist.Zero;

			// PERF: Avoid LINQ.
			var max = WDist.Zero;
			foreach (var armament in Armaments)
			{
				if (armament.IsTraitDisabled)
					continue;

				if (armament.IsTraitPaused)
					continue;

				var range = armament.MaxRange();
				if (max < range)
					max = range;
			}

			return max;
		}

		public WDist GetMinimumRangeVersusTarget(in Target target)
		{
			if (IsTraitDisabled)
				return WDist.Zero;

			// PERF: Avoid LINQ.
			var min = WDist.MaxValue;
			foreach (var armament in Armaments)
			{
				if (armament.IsTraitDisabled)
					continue;

				if (armament.IsTraitPaused)
					continue;

				if (!armament.Weapon.IsValidAgainst(target, Actor.World, Actor))
					continue;

				var range = armament.Weapon.MinRange;
				if (min > range)
					min = range;
			}

			return min != WDist.MaxValue ? min : WDist.Zero;
		}

		public WDist GetMaximumRangeVersusTarget(in Target target)
		{
			if (IsTraitDisabled)
				return WDist.Zero;

			var max = WDist.Zero;

			// We want actors to use only weapons with ammo for this, except when ALL weapons are out of ammo,
			// then we use the paused, valid weapon with highest range.
			var maxFallback = WDist.Zero;

			// PERF: Avoid LINQ.
			foreach (var armament in Armaments)
			{
				if (armament.IsTraitDisabled)
					continue;

				if (!armament.Weapon.IsValidAgainst(target, Actor.World, Actor))
					continue;

				var range = armament.MaxRange();
				if (maxFallback < range)
					maxFallback = range;

				if (armament.IsTraitPaused)
					continue;

				if (max < range)
					max = range;
			}

			return max != WDist.Zero ? max : maxFallback;
		}

		// Enumerates all armaments, that this actor possesses, that can be used against Target t
		public IEnumerable<Armament> ChooseArmamentsForTarget(Target t, bool forceAttack)
		{
			// If force-fire is not used, and the target requires force-firing or the target is
			// terrain or invalid, no armaments can be used
			if (!forceAttack && (t.Type == TargetType.Terrain || t.Type == TargetType.Invalid || t.RequiresForceFire))
				return Enumerable.Empty<Armament>();

			// Get target's owner; in case of terrain or invalid target there will be no problems
			// with owner == null since forceFire will have to be true in this part of the method
			// (short-circuiting in the logical expression below)
			Player owner = null;
			if (t.Type == TargetType.FrozenActor)
				owner = t.FrozenActor.Owner;
			else if (t.Type == TargetType.Actor)
				owner = t.Actor.Owner;

			return Armaments.Where(a =>
				!a.IsTraitDisabled
				&& (owner == null || (forceAttack ? a.Info.ForceTargetRelationships : a.Info.TargetRelationships).HasRelationship(Actor.Owner.RelationshipWith(owner)))
				&& a.Weapon.IsValidAgainst(t, Actor.World, Actor));
		}

		public void AttackTarget(in Target target, AttackSource source, bool queued, bool allowMove, bool forceAttack = false, Color? targetLineColor = null)
		{
			if (IsTraitDisabled)
				return;

			if (!target.IsValidFor(Actor))
				return;

			var activity = GetAttackActivity(source, target, allowMove, forceAttack, targetLineColor);
			Actor.QueueActivity(queued, activity);
			OnResolveAttackOrder(Actor, activity, target, queued, forceAttack);
		}

		public virtual void OnResolveAttackOrder(Actor self, Activity activity, in Target target, bool queued, bool forceAttack) { }

		public bool IsReachableTarget(in Target target, bool allowMove)
		{
			return HasAnyValidWeapons(target)
				&& (target.IsInRange(Actor.CenterPosition, GetMaximumRangeVersusTarget(target)) || (allowMove && Actor.Info.HasTraitInfo<IMoveInfo>()));
		}

		public PlayerRelationship UnforcedAttackTargetStances()
		{
			// PERF: Avoid LINQ.
			var stances = PlayerRelationship.None;
			foreach (var armament in Armaments)
				if (!armament.IsTraitDisabled)
					stances |= armament.Info.TargetRelationships;

			return stances;
		}

		class AttackOrderTargeter : IOrderTargeter
		{
			readonly AttackBase ab;
			public readonly Actor Actor;

			public AttackOrderTargeter(Actor self, AttackBase ab, int priority)
			{
				Actor = self;
				this.ab = ab;
				OrderID = ab.attackOrderName;
				OrderPriority = priority;
			}

			public string OrderID { get; private set; }
			public int OrderPriority { get; }
			public bool TargetOverridesSelection(in Target target, List<Actor> actorsAt, CPos xy, TargetModifiers modifiers) { return true; }

			bool CanTargetActor(in Target target, ref TargetModifiers modifiers, ref string cursor)
			{
				IsQueued = modifiers.HasModifier(TargetModifiers.ForceQueue);

				if (modifiers.HasModifier(TargetModifiers.ForceMove))
					return false;

				if (ab.Info.ForceFireIgnoresActors && modifiers.HasModifier(TargetModifiers.ForceAttack))
					return false;

				// Disguised actors are revealed by the attack cursor
				// HACK: works around limitations in the targeting code that force the
				// targeting and attacking logic (which should be logically separate)
				// to use the same code
				if (target.Type == TargetType.Actor && target.Actor.EffectiveOwner != null &&
						target.Actor.EffectiveOwner.Disguised && Actor.Owner.RelationshipWith(target.Actor.Owner) == PlayerRelationship.Enemy)
					modifiers |= TargetModifiers.ForceAttack;

				var forceAttack = modifiers.HasModifier(TargetModifiers.ForceAttack);
				var armaments = ab.ChooseArmamentsForTarget(target, forceAttack);
				if (!armaments.Any())
					return false;

				// Use valid armament with highest range out of those that have ammo
				// If all are out of ammo, just use valid armament with highest range
				armaments = armaments.OrderByDescending(x => x.MaxRange());
				var a = armaments.FirstOrDefault(x => !x.IsTraitPaused);
				if (a == null)
					a = armaments.First();

				var outOfRange = !target.IsInRange(Actor.CenterPosition, a.MaxRange()) ||
					(!forceAttack && target.Type == TargetType.FrozenActor && !ab.Info.TargetFrozenActors);

				if (outOfRange && ab.Info.OutsideRangeRequiresForceFire && !modifiers.HasModifier(TargetModifiers.ForceAttack))
					return false;

				cursor = outOfRange ? ab.Info.OutsideRangeCursor ?? a.Info.OutsideRangeCursor : ab.Info.Cursor ?? a.Info.Cursor;

				if (!forceAttack)
					return true;

				OrderID = ab.forceAttackOrderName;
				return true;
			}

			bool CanTargetLocation(CPos location, TargetModifiers modifiers, ref string cursor)
			{
				if (!Actor.World.Map.Contains(location))
					return false;

				IsQueued = modifiers.HasModifier(TargetModifiers.ForceQueue);

				// Targeting the terrain is only possible with force-attack modifier
				if (modifiers.HasModifier(TargetModifiers.ForceMove) || !modifiers.HasModifier(TargetModifiers.ForceAttack))
					return false;

				var target = Target.FromCell(Actor.World, location);
				var armaments = ab.ChooseArmamentsForTarget(target, true);
				if (!armaments.Any())
					return false;

				// Use valid armament with highest range out of those that have ammo
				// If all are out of ammo, just use valid armament with highest range
				armaments = armaments.OrderByDescending(x => x.MaxRange());
				var a = armaments.FirstOrDefault(x => !x.IsTraitPaused);
				if (a == null)
					a = armaments.First();

				cursor = !target.IsInRange(Actor.CenterPosition, a.MaxRange())
					? ab.Info.OutsideRangeCursor ?? a.Info.OutsideRangeCursor
					: ab.Info.Cursor ?? a.Info.Cursor;

				OrderID = ab.forceAttackOrderName;
				return true;
			}

			public bool CanTarget(in Target target, ref TargetModifiers modifiers, ref string cursor)
			{
				switch (target.Type)
				{
					case TargetType.Actor:
					case TargetType.FrozenActor:
						return CanTargetActor(target, ref modifiers, ref cursor);
					case TargetType.Terrain:
						return CanTargetLocation(Actor.World.Map.CellContaining(target.CenterPosition), modifiers, ref cursor);
					default:
						return false;
				}
			}

			public bool IsQueued { get; protected set; }
		}
	}
}
