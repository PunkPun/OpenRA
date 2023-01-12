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

using OpenRA.Traits;

namespace OpenRA.Mods.Common.Traits
{
	[Desc("Grants a condition while the trait is active.")]
	class GrantConditionInfo : ConditionalTraitInfo
	{
		[FieldLoader.Require]
		[GrantedConditionReference]
		[Desc("Condition to grant.")]
		public readonly string Condition = null;

		[Desc("Is the condition irrevocable once it has been activated?")]
		public readonly bool GrantPermanently = false;

		public override object Create(ActorInitializer init) { return new GrantCondition(init.Self, this); }
	}

	class GrantCondition : ConditionalTrait<GrantConditionInfo>
	{
		int conditionToken = Actor.InvalidConditionToken;

		public GrantCondition(Actor self, GrantConditionInfo info)
			: base(info, self) { }

		protected override void TraitEnabled()
		{
			if (conditionToken == Actor.InvalidConditionToken)
				conditionToken = Actor.GrantCondition(Info.Condition);
		}

		protected override void TraitDisabled()
		{
			if (Info.GrantPermanently || conditionToken == Actor.InvalidConditionToken)
				return;

			conditionToken = Actor.RevokeCondition(conditionToken);
		}
	}
}
