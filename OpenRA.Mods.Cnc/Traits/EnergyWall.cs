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
using OpenRA.GameRules;
using OpenRA.Mods.Common.Traits;
using OpenRA.Support;
using OpenRA.Traits;

namespace OpenRA.Mods.Cnc.Traits
{
	[Desc("Will open and be passable for actors that appear friendly when there are no enemies in range.")]
	public class EnergyWallInfo : BuildingInfo, ITemporaryBlockerInfo, IObservesVariablesInfo, IRulesetLoaded
	{
		[FieldLoader.Require]
		[WeaponReference]
		[Desc("The weapon to attack units on top of the wall with when activated.")]
		public readonly string Weapon = null;

		[ConsumedConditionReference]
		[Desc("Boolean expression defining the condition to activate this trait.")]
		public readonly BooleanExpression ActiveCondition = null;

		public override object Create(ActorInitializer init) { return new EnergyWall(init, this); }

		public WeaponInfo WeaponInfo { get; private set; }

		public void RulesetLoaded(Ruleset rules, ActorInfo ai)
		{
			var weaponToLower = Weapon.ToLowerInvariant();
			if (!rules.Weapons.TryGetValue(weaponToLower, out var weaponInfo))
				throw new YamlException($"Weapons Ruleset does not contain an entry '{weaponToLower}'");

			WeaponInfo = weaponInfo;
		}
	}

	public class EnergyWall : Building, IObservesVariables, ITick, ITemporaryBlocker
	{
		readonly EnergyWallInfo info;
		IEnumerable<CPos> blockedPositions;

		// Initial state is active to match Building adding the influence to the ActorMap
		// This will be updated by ConditionsChanged at actor creation.
		bool active = true;

		public EnergyWall(ActorInitializer init, EnergyWallInfo info)
			: base(init, info)
		{
			this.info = info;
		}

		public virtual IEnumerable<VariableObserver> GetVariableObservers()
		{
			if (info.ActiveCondition != null)
				yield return new VariableObserver(ActiveConditionChanged, info.ActiveCondition.Variables);
		}

		void ActiveConditionChanged(IReadOnlyDictionary<string, int> conditions)
		{
			if (info.ActiveCondition == null)
				return;

			var wasActive = active;
			active = info.ActiveCondition.Evaluate(conditions);

			if (!wasActive && active)
				Actor.World.ActorMap.AddInfluence(this);
			else if (wasActive && !active)
				Actor.World.ActorMap.RemoveInfluence(this);
		}

		void ITick.Tick()
		{
			if (!active)
				return;

			foreach (var loc in blockedPositions)
			{
				var blockers = Actor.World.ActorMap.GetActorsAt(loc).Where(a => !a.IsDead && a != Actor);
				foreach (var blocker in blockers)
					info.WeaponInfo.Impact(Target.FromActor(blocker), Actor);
			}
		}

		bool ITemporaryBlocker.IsBlocking(CPos cell)
		{
			return active && blockedPositions.Contains(cell);
		}

		bool ITemporaryBlocker.CanRemoveBlockage(Actor blocking)
		{
			return !active;
		}

		protected override void AddedToWorld()
		{
			base.AddedToWorld();
			blockedPositions = info.Tiles(Actor.Location);
		}
	}
}
