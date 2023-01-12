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
using OpenRA.GameRules;
using OpenRA.Primitives;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Traits
{
	[Desc("Bridge actor that can't be passed underneath.")]
	class GroundLevelBridgeInfo : TraitInfo, IRulesetLoaded, Requires<BuildingInfo>, Requires<IHealthInfo>
	{
		public readonly string TerrainType = "Bridge";

		public readonly string Type = "GroundLevelBridge";

		public readonly CVec[] NeighbourOffsets = Array.Empty<CVec>();

		[WeaponReference]
		[Desc("The name of the weapon to use when demolishing the bridge")]
		public readonly string DemolishWeapon = "Demolish";

		public WeaponInfo DemolishWeaponInfo { get; private set; }

		[Desc("Types of damage that this bridge causes to units over/in path of it while being destroyed/repaired. Leave empty for no damage types.")]
		public readonly BitSet<DamageType> DamageTypes = default;

		public void RulesetLoaded(Ruleset rules, ActorInfo ai)
		{
			var weaponToLower = (DemolishWeapon ?? string.Empty).ToLowerInvariant();
			if (!rules.Weapons.TryGetValue(weaponToLower, out var weapon))
				throw new YamlException($"Weapons Ruleset does not contain an entry '{weaponToLower}'");

			DemolishWeaponInfo = weapon;
		}

		public override object Create(ActorInitializer init) { return new GroundLevelBridge(init.Self, this); }
	}

	class GroundLevelBridge : IBridgeSegment, INotifyAddedToWorld, INotifyRemovedFromWorld
	{
		public readonly GroundLevelBridgeInfo Info;
		public readonly Actor Actor;
		readonly BridgeLayer bridgeLayer;
		readonly IEnumerable<CPos> cells;
		readonly IHealth health;

		public GroundLevelBridge(Actor self, GroundLevelBridgeInfo info)
		{
			Info = info;
			Actor = self;
			health = self.Trait<IHealth>();

			bridgeLayer = self.World.WorldActor.Trait<BridgeLayer>();
			var buildingInfo = self.Info.TraitInfo<BuildingInfo>();
			cells = buildingInfo.PathableTiles(self.Location);
		}

		void UpdateTerrain(Actor self, byte terrainIndex)
		{
			foreach (var cell in cells)
				self.World.Map.CustomTerrain[cell] = terrainIndex;
		}

		void INotifyAddedToWorld.AddedToWorld()
		{
			bridgeLayer.Add(Actor);

			var terrainIndex = Actor.World.Map.Rules.TerrainInfo.GetTerrainIndex(Info.TerrainType);
			UpdateTerrain(Actor, terrainIndex);
			KillInvalidActorsInFootprint(Actor);
		}

		void INotifyRemovedFromWorld.RemovedFromWorld()
		{
			bridgeLayer.Remove(Actor);

			UpdateTerrain(Actor, byte.MaxValue);
			KillInvalidActorsInFootprint(Actor);
		}

		void KillInvalidActorsInFootprint(Actor self)
		{
			foreach (var c in cells)
				foreach (var a in self.World.ActorMap.GetActorsAt(c))
					if (a.Info.HasTraitInfo<IPositionableInfo>() && !a.Trait<IPositionable>().CanExistInCell(c))
						a.Kill(self, Info.DamageTypes);
		}

		void IBridgeSegment.Repair(Actor repairer)
		{
			health.InflictDamage(repairer, new Damage(-health.MaxHP), true);
		}

		void IBridgeSegment.Demolish(Actor saboteur, BitSet<DamageType> damageTypes)
		{
			Actor.World.AddFrameEndTask(w =>
			{
				if (Actor.IsDead)
					return;

				// Use .FromPos since this actor is dead. Cannot use Target.FromActor
				Info.DemolishWeaponInfo.Impact(Target.FromPos(Actor.CenterPosition), saboteur);

				Actor.Kill(saboteur, damageTypes);
			});
		}

		string IBridgeSegment.Type => Info.Type;
		DamageState IBridgeSegment.DamageState => Actor.GetDamageState();
		bool IBridgeSegment.Valid => Actor.IsInWorld;
		CVec[] IBridgeSegment.NeighbourOffsets => Info.NeighbourOffsets;
		CPos IBridgeSegment.Location => Actor.Location;
	}
}
