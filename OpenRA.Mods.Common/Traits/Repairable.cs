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
using System.Linq;
using OpenRA.Primitives;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Traits
{
	[Desc("This actor can be sent for repairs.")]
	public class RepairableInfo : DockClientBaseInfo, Requires<IHealthInfo>, Requires<IMoveInfo>, IObservesVariablesInfo
	{
		[Desc("Docking type")]
		public readonly BitSet<DockType> Type = new("Repair");

		[Desc("The amount the unit will be repaired at each step. Use -1 for fallback behavior where HpPerStep from RepairsUnits trait will be used.")]
		public readonly int HpPerStep = -1;

		public override object Create(ActorInitializer init) { return new Repairable(init.Self, this); }
	}

	public class Repairable : DockClientBase<RepairableInfo>
	{
		readonly Actor self;
		readonly IHealth health;
		readonly int unitCost;

		public override BitSet<DockType> GetDockType => Info.Type;

		public Repairable(Actor self, RepairableInfo info)
			: base(self, info)
		{
			this.self = self;
			health = self.Trait<IHealth>();

			var valued = self.Info.TraitInfoOrDefault<ValuedInfo>();
			unitCost = valued != null ? valued.Cost : 0;
		}

		protected override bool CanDock()
		{
			return self.GetDamageState() > DamageState.Undamaged;
		}

		RepairsUnits[] allRepairsUnits;
		PlayerResources playerResources;

		bool soundPlayed;
		int remainingTicks;

		public override void OnDockStarted(Actor self, Actor hostActor, IDockHost host)
		{
			playerResources = self.Owner.PlayerActor.Trait<PlayerResources>();
			allRepairsUnits = hostActor.TraitsImplementing<RepairsUnits>().ToArray();
		}

		public override bool OnDockTick(Actor self, Actor hostActor, IDockHost host, out bool paused)
		{
			paused = false;
			var repairsUnits = allRepairsUnits.FirstOrDefault(r => !r.IsTraitDisabled && !r.IsTraitPaused);
			if (repairsUnits == null)
			{
				if (!allRepairsUnits.Any(r => r.IsTraitPaused))
					return true;

				paused = true;
				return false;
			}

			if (health.DamageState == DamageState.Undamaged)
			{
				// Give experience to your ally.
				if (hostActor.Owner != self.Owner)
					hostActor.Owner.PlayerActor.TraitOrDefault<PlayerExperience>()?.GiveExperience(repairsUnits.Info.PlayerExperience);

				Game.Sound.PlayNotification(self.World.Map.Rules, self.Owner, "Speech", repairsUnits.Info.FinishRepairingNotification, self.Owner.Faction.InternalName);
				TextNotificationsManager.AddTransientLine(self.Owner, repairsUnits.Info.FinishRepairingTextNotification);

				return true;
			}

			if (remainingTicks == 0)
			{
				var hpToRepair = Info.HpPerStep > 0 ? Info.HpPerStep : repairsUnits.Info.HpPerStep;

				// Cast to long to avoid overflow when multiplying by the health.
				var value = (long)unitCost * repairsUnits.Info.ValuePercentage;
				var cost = value == 0 ? 0 : Math.Max(1, (int)(hpToRepair * value / (health.MaxHP * 100L)));

				if (!soundPlayed)
				{
					soundPlayed = true;
					Game.Sound.PlayNotification(self.World.Map.Rules, self.Owner, "Speech", repairsUnits.Info.StartRepairingNotification, self.Owner.Faction.InternalName);
					TextNotificationsManager.AddTransientLine(self.Owner, repairsUnits.Info.StartRepairingTextNotification);
				}

				if (!playerResources.TakeCash(cost, true))
				{
					remainingTicks = 1;
					paused = true;
					return false;
				}

				self.InflictDamage(hostActor, new Damage(-hpToRepair, repairsUnits.Info.RepairDamageTypes));
				remainingTicks = repairsUnits.Info.Interval;
			}
			else
				--remainingTicks;

			paused = true;
			return false;
		}

		public override void OnDockCompleted(Actor self, Actor hostActor, IDockHost host)
		{
			playerResources = null;
			allRepairsUnits = null;
			remainingTicks = 0;
			soundPlayed = false;
		}
	}
}
