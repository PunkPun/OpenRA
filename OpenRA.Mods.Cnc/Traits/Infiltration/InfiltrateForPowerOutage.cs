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

using OpenRA.Mods.Common.Traits;
using OpenRA.Primitives;
using OpenRA.Traits;

namespace OpenRA.Mods.Cnc.Traits
{
	class InfiltrateForPowerOutageInfo : TraitInfo
	{
		[Desc("The `TargetTypes` from `Targetable` that are allowed to enter.")]
		public readonly BitSet<TargetableType> Types = default;

		[Desc("Measured in ticks.")]
		public readonly int Duration = 500;

		[NotificationReference("Speech")]
		[Desc("Sound the victim will hear when they get sabotaged.")]
		public readonly string InfiltratedNotification = null;

		[Desc("Text notification the victim will see when they get sabotaged.")]
		public readonly string InfiltratedTextNotification = null;

		[NotificationReference("Speech")]
		[Desc("Sound the perpetrator will hear after successful infiltration.")]
		public readonly string InfiltrationNotification = null;

		[Desc("Text notification the perpetrator will see after successful infiltration.")]
		public readonly string InfiltrationTextNotification = null;

		public override object Create(ActorInitializer init) { return new InfiltrateForPowerOutage(this, init.Self); }
	}

	class InfiltrateForPowerOutage : INotifyOwnerChanged, INotifyInfiltrated
	{
		readonly InfiltrateForPowerOutageInfo info;
		readonly Actor actor;
		PowerManager playerPower;

		public InfiltrateForPowerOutage(InfiltrateForPowerOutageInfo info, Actor self)
		{
			this.info = info;
			actor = self;
			playerPower = self.Owner.PlayerActor.Trait<PowerManager>();
		}

		void INotifyInfiltrated.Infiltrated(Actor infiltrator, BitSet<TargetableType> types)
		{
			if (!info.Types.Overlaps(types))
				return;

			if (info.InfiltratedNotification != null)
				Game.Sound.PlayNotification(actor.World.Map.Rules, actor.Owner, "Speech", info.InfiltratedNotification, actor.Owner.Faction.InternalName);

			if (info.InfiltrationNotification != null)
				Game.Sound.PlayNotification(actor.World.Map.Rules, infiltrator.Owner, "Speech", info.InfiltrationNotification, infiltrator.Owner.Faction.InternalName);

			TextNotificationsManager.AddTransientLine(info.InfiltratedTextNotification, actor.Owner);
			TextNotificationsManager.AddTransientLine(info.InfiltrationTextNotification, infiltrator.Owner);

			playerPower.TriggerPowerOutage(info.Duration);
		}

		void INotifyOwnerChanged.OnOwnerChanged(Player oldOwner, Player newOwner)
		{
			playerPower = actor.Owner.PlayerActor.Trait<PowerManager>();
		}
	}
}
