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

namespace OpenRA.Mods.Cnc.Traits
{
	[Desc("Actor has a visual turret used to attack.")]
	public class AttackTDGunboatTurretedInfo : AttackTurretedInfo, Requires<TDGunboatInfo>
	{
		public override object Create(ActorInitializer init) { return new AttackTDGunboatTurreted(this, init.Self); }
	}

	public class AttackTDGunboatTurreted : AttackTurreted
	{
		public AttackTDGunboatTurreted(AttackTDGunboatTurretedInfo info, Actor self)
			: base(info, self) { }

		public override Activity GetAttackActivity(AttackSource source, in Target newTarget, bool allowMove, bool forceAttack, Color? targetLineColor)
		{
			return new AttackTDGunboatTurretedActivity(Actor, newTarget, forceAttack, targetLineColor);
		}

		class AttackTDGunboatTurretedActivity : Activity
		{
			readonly AttackTDGunboatTurreted attack;
			readonly Target target;
			readonly bool forceAttack;
			readonly Color? targetLineColor;
			bool hasTicked;

			public AttackTDGunboatTurretedActivity(Actor self, in Target target, bool forceAttack, Color? targetLineColor = null)
				: base(self)
			{
				attack = self.Trait<AttackTDGunboatTurreted>();
				this.target = target;
				this.forceAttack = forceAttack;
				this.targetLineColor = targetLineColor;
			}

			public override bool Tick()
			{
				if (IsCanceling || !target.IsValidFor(Actor))
					return true;

				if (attack.IsTraitDisabled)
					return false;

				var weapon = attack.ChooseArmamentsForTarget(target, forceAttack).FirstOrDefault();
				if (weapon != null)
				{
					// Check that AttackTDGunboatTurreted hasn't cancelled the target by modifying attack.Target
					// Having both this and AttackTDGunboatTurreted modify that field is a horrible hack.
					if (hasTicked && attack.RequestedTarget.Type == TargetType.Invalid)
						return true;

					attack.SetRequestedTarget(target);
					hasTicked = true;
				}

				return false;
			}

			public override IEnumerable<TargetLineNode> TargetLineNodes()
			{
				if (targetLineColor != null)
					yield return new TargetLineNode(target, targetLineColor.Value);
			}
		}
	}
}
