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
	[Desc("Manages AI repairing base buildings.")]
	public class BuildingRepairBotModuleInfo : ConditionalTraitInfo
	{
		public override object Create(ActorInitializer init) { return new BuildingRepairBotModule(init.Self, this); }
	}

	public class BuildingRepairBotModule : ConditionalTrait<BuildingRepairBotModuleInfo>, IBotRespondToAttack
	{
		public BuildingRepairBotModule(Actor self, BuildingRepairBotModuleInfo info)
			: base(info, self) { }

		void IBotRespondToAttack.RespondToAttack(IBot bot, AttackInfo e)
		{
			// HACK: We don't want D2k bots to repair all their buildings on placement
			// where half their HP is removed via neutral terrain damage.
			// TODO: Implement concrete placement for D2k bots and remove this hack.
			if (Actor.Owner.RelationshipWith(e.Attacker.Owner) == PlayerRelationship.Neutral)
				return;

			var rb = Actor.TraitOrDefault<RepairableBuilding>();
			if (rb != null)
			{
				if (e.DamageState > DamageState.Light && e.PreviousDamageState <= DamageState.Light && !rb.RepairActive)
				{
					AIUtils.BotDebug("{0} noticed damage {1} {2}->{3}, repairing.",
						Actor.Owner, Actor, e.PreviousDamageState, e.DamageState);
					bot.QueueOrder(new Order("RepairBuilding", Actor.Owner.PlayerActor, Target.FromActor(Actor), false));
				}
			}
		}
	}
}
