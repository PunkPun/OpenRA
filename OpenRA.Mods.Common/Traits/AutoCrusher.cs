#region Copyright & License Information
/*
 * Copyright 2007-2022 The OpenRA Developers (see AUTHORS)
 * This file is part of OpenRA, which is free software. It is made
 * available to you under the terms of the GNU General Public License
 * as published by the Free Software Foundation, either version 3 of
 * the License, or (at your option) any later version. For more
 * information, see COPYING.
 */
#endregion

using System.Linq;
using OpenRA.Activities;
using OpenRA.Primitives;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Traits
{
	class AutoCrusherInfo : PausableConditionalTraitInfo, Requires<IMoveInfo>, IAutoTargetInfo
	{
		[Desc("Player relationships the owner of the actor needs to get targeted")]
		public readonly PlayerRelationship TargetRelationships = PlayerRelationship.Ally | PlayerRelationship.Neutral | PlayerRelationship.Enemy;

		public override object Create(ActorInitializer init) { return new AutoCrusher(this, init.Self); }
	}

	class AutoCrusher : PausableConditionalTrait<AutoCrusherInfo>, IAutoTarget
	{
		readonly Actor self;
		readonly BitSet<CrushClass> crushes;
		readonly IMove move;

		public AutoCrusher(AutoCrusherInfo info, Actor self)
			: base(info)
		{
			this.self = self;
			move = self.Trait<IMove>();
			crushes = (move as Mobile)?.Info.LocomotorInfo.Crushes ?? (move as Aircraft)?.Info.Crushes ?? default;
		}

		bool IAutoTarget.TargetFrozenActors => false;
		PlayerRelationship IAutoTarget.UnforcedAttackTargetStances() => Info.TargetRelationships;
		WDist IAutoTarget.GetMaximumRange() => WDist.FromCells(1024);
		public bool ValidTarget(Target t)
		{
			var actor = t.Actor;
			if (actor == null)
				return false;

			return !actor.IsDead && actor.IsInWorld && actor.IsAtGroundLevel() && Info.TargetRelationships.HasRelationship(self.Owner.RelationshipWith(actor.Owner)) && actor.TraitsImplementing<ICrushable>().Any(c => c.CrushableBy(actor, self, crushes));
		}

		public bool CanAttackTarget(Target target, bool allowMove, bool allowTurn)
		{
			return allowMove && ValidTarget(target);
		}

		void IAutoTarget.AttackTarget(Target target, bool allowMove)
		{
			if (!allowMove || !ValidTarget(target))
				return;

			self.QueueActivity(GetAttackActivity(self, target));
			self.ShowTargetLines();
		}

		public Activity GetAttackActivity(Actor self, in Target newTarget)
		{
			return move.MoveIntoTarget(self, newTarget);
		}
	}
}
