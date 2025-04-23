--[[
   Copyright (c) The OpenRA Developers and Contributors
   This file is part of OpenRA, which is free software. It is made
   available to you under the terms of the GNU General Public License
   as published by the Free Software Foundation, either version 3 of
   the License, or (at your option) any later version. For more
   information, see COPYING.
]]

EarlyGameStage = DateTime.Minutes(6)
InitialProductionDelay = {
	OrdosMain =
	{
		easy = DateTime.Seconds(150),
		normal = DateTime.Seconds(100),
		hard = DateTime.Seconds(50)
	},
	OrdosSmall =
	{
		easy = DateTime.Seconds(120),
		normal = DateTime.Seconds(60),
		hard = DateTime.Seconds(30)
	},
}
AttackGroupSize =
{
	easy = 6,
	normal = 8,
	hard = 10
}

EarlyAttackDelays =
{
	easy = { DateTime.Seconds(7), DateTime.Seconds(10) },
	normal = { DateTime.Seconds(4), DateTime.Seconds(6) },
	hard = { DateTime.Seconds(3), DateTime.Seconds(5) }
}
LateAttackDelays =
{
	easy = { DateTime.Seconds(4), DateTime.Seconds(7) },
	normal = { DateTime.Seconds(2), DateTime.Seconds(5) },
	hard = { DateTime.Seconds(1), DateTime.Seconds(3) }
}

OrdosInfantryTypes = { "light_inf", "light_inf", "light_inf", "trooper", "trooper" }
OrdosVehicleTypes = { "raider", "raider", "quad" }
OrdosTankType = { "combat_tank_o" }

ActivateAI = function()
	IdlingUnits[OrdosMain] = Utils.Concat(Reinforcements.Reinforce(OrdosMain, InitialOrdosReinforcements[1], InitialOrdosPaths[1]), Reinforcements.Reinforce(OrdosMain, InitialOrdosReinforcements[2], InitialOrdosPaths[2]))
	IdlingUnits[OrdosSmall] = Reinforcements.Reinforce(OrdosSmall, InitialOrdosReinforcements[1], InitialOrdosPaths[3])
	IdlingUnits[Corrino] = { CSardaukar1, CSardaukar2, CSardaukar3, CSardaukar4, CSardaukar5 }

	DefendAndRepairBase(OrdosMain, OrdosMainBase, 0.75, AttackGroupSize[Difficulty])
	DefendAndRepairBase(OrdosSmall, OrdosSmallBase, 0.75, AttackGroupSize[Difficulty])
	DefendAndRepairBase(Corrino, CorrinoBase, 0.75, AttackGroupSize[Difficulty])

	local delay = function()
		if EarlyGameStage >= DateTime.GameTime then
			return Utils.RandomInteger(EarlyAttackDelays[Difficulty][1], EarlyAttackDelays[Difficulty][2] + 1)
		else
			return Utils.RandomInteger(LateAttackDelays[Difficulty][1], LateAttackDelays[Difficulty][2] + 1)
		end
	end

	local infantryToBuild = function() return { Utils.Random(OrdosInfantryTypes) } end
	local vehilcesToBuild = function() return { Utils.Random(OrdosVehicleTypes) } end
	local tanksToBuild = function() return OrdosTankType end
	local attackThresholdSize = AttackGroupSize[Difficulty] * 2.5
	Trigger.AfterDelay(InitialProductionDelay["OrdosMain"][Difficulty], function()
		ProduceUnits(OrdosMain, OBarracks1, delay, infantryToBuild, AttackGroupSize[Difficulty], attackThresholdSize)
		ProduceUnits(OrdosMain, OLightFactory1, delay, vehilcesToBuild, AttackGroupSize[Difficulty], attackThresholdSize)
		ProduceUnits(OrdosMain, OHeavyFactory, delay, tanksToBuild, AttackGroupSize[Difficulty], attackThresholdSize)
	end)

	Trigger.AfterDelay(InitialProductionDelay["OrdosSmall"][Difficulty], function()
		ProduceUnits(OrdosSmall, OBarracks3, delay, infantryToBuild, AttackGroupSize[Difficulty], attackThresholdSize)
	ProduceUnits(OrdosSmall, OLightFactory2, delay, vehilcesToBuild, AttackGroupSize[Difficulty], attackThresholdSize)
	end)
	ActivateCrusherOnProductions({ "combat_tank_o" }, { OHeavyFactory })
end
