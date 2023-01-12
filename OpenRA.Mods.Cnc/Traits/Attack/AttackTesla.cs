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
using OpenRA.Mods.Common.Activities;
using OpenRA.Mods.Common.Traits;
using OpenRA.Primitives;
using OpenRA.Traits;

namespace OpenRA.Mods.Cnc.Traits
{
	[Desc("Implements the charge-then-burst attack logic specific to the RA tesla coil.")]
	class AttackTeslaInfo : AttackBaseInfo
	{
		[Desc("How many charges this actor has to attack with, once charged.")]
		public readonly int MaxCharges = 1;

		[Desc("Reload time for all charges (in ticks).")]
		public readonly int ReloadDelay = 120;

		[Desc("Delay for initial charge attack (in ticks).")]
		public readonly int InitialChargeDelay = 22;

		[Desc("Delay between charge attacks (in ticks).")]
		public readonly int ChargeDelay = 3;

		[Desc("Sound to play when actor charges.")]
		public readonly string ChargeAudio = null;

		public override object Create(ActorInitializer init) { return new AttackTesla(this, init.Self); }
	}

	class AttackTesla : AttackBase, ITick, INotifyAttack
	{
		readonly AttackTeslaInfo info;

		[Sync]
		int charges;

		[Sync]
		int timeToRecharge;

		public AttackTesla(AttackTeslaInfo info, Actor self)
			: base(info, self)
		{
			this.info = info;
			charges = info.MaxCharges;
		}

		void ITick.Tick()
		{
			if (--timeToRecharge <= 0)
				charges = info.MaxCharges;
		}

		protected override bool CanAttack(in Target target)
		{
			if (!IsReachableTarget(target, true))
				return false;

			return base.CanAttack(target);
		}

		void INotifyAttack.Attacking(in Target target, Armament a, Barrel barrel)
		{
			--charges;
			timeToRecharge = info.ReloadDelay;
		}

		void INotifyAttack.PreparingAttack(in Target target, Armament a, Barrel barrel) { }

		public override Activity GetAttackActivity(AttackSource source, in Target newTarget, bool allowMove, bool forceAttack, Color? targetLineColor = null)
		{
			return new ChargeAttack(Actor, this, newTarget, forceAttack, targetLineColor);
		}

		class ChargeAttack : Activity, IActivityNotifyStanceChanged
		{
			readonly AttackTesla attack;
			readonly Target target;
			readonly bool forceAttack;
			readonly Color? targetLineColor;

			public ChargeAttack(Actor self, AttackTesla attack, in Target target, bool forceAttack, Color? targetLineColor = null)
				: base(self)
			{
				this.attack = attack;
				this.target = target;
				this.forceAttack = forceAttack;
				this.targetLineColor = targetLineColor;
			}

			public override bool Tick()
			{
				if (IsCanceling || !attack.CanAttack(target))
					return true;

				if (attack.charges == 0)
					return false;

				foreach (var notify in Actor.TraitsImplementing<INotifyTeslaCharging>())
					notify.Charging(target);

				if (!string.IsNullOrEmpty(attack.info.ChargeAudio))
					Game.Sound.Play(SoundType.World, attack.info.ChargeAudio, Actor.CenterPosition);

				QueueChild(new Wait(Actor, attack.info.InitialChargeDelay));
				QueueChild(new ChargeFire(Actor, attack, target));
				return false;
			}

			void IActivityNotifyStanceChanged.StanceChanged(AutoTarget autoTarget, UnitStance oldStance, UnitStance newStance)
			{
				// Cancel non-forced targets when switching to a more restrictive stance if they are no longer valid for auto-targeting
				if (newStance > oldStance || forceAttack)
					return;

				if (target.Type == TargetType.Actor)
				{
					var a = target.Actor;
					if (!autoTarget.HasValidTargetPriority(a.Owner, a.GetEnabledTargetTypes()))
						Cancel(true);
				}
				else if (target.Type == TargetType.FrozenActor)
				{
					var fa = target.FrozenActor;
					if (!autoTarget.HasValidTargetPriority(fa.Owner, fa.TargetTypes))
						Cancel(true);
				}
			}

			public override IEnumerable<TargetLineNode> TargetLineNodes()
			{
				if (targetLineColor != null)
					yield return new TargetLineNode(target, targetLineColor.Value);
			}
		}

		class ChargeFire : Activity
		{
			readonly AttackTesla attack;
			readonly Target target;

			public ChargeFire(Actor self, AttackTesla attack, in Target target)
				: base(self)
			{
				this.attack = attack;
				this.target = target;
			}

			public override bool Tick()
			{
				if (IsCanceling || !attack.CanAttack(target))
					return true;

				if (attack.charges == 0)
					return true;

				attack.DoAttack(target);

				QueueChild(new Wait(Actor, attack.info.ChargeDelay));
				return false;
			}
		}
	}
}
