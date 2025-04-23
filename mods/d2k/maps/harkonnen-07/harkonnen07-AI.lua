--[[
   Copyright (c) The OpenRA Developers and Contributors
   This file is part of OpenRA, which is free software. It is made
   available to you under the terms of the GNU General Public License
   as published by the Free Software Foundation, either version 3 of
   the License, or (at your option) any later version. For more
   information, see COPYING.
]]
EarlyGameStage = DateTime.Minutes(7)
InitialProductionDelay = {
	AtreidesMain =
	{
		easy = DateTime.Seconds(150),
		normal = DateTime.Seconds(100),
		hard = DateTime.Seconds(50)
	},
	AtreidesSmall =
	{
		easy = DateTime.Seconds(120),
		normal = DateTime.Seconds(60),
		hard = DateTime.Seconds(30)
	},
	Corrino =
	{
		easy = DateTime.Seconds(100),
		normal = DateTime.Seconds(120),
		hard = DateTime.Seconds(60)
	}
}
AttackGroupSize =
{
	easy = 6,
	normal = 8,
	hard = 10
}

EarlyAttackDelays =
{
	easy = { DateTime.Seconds(9), DateTime.Seconds(12) },
	normal = { DateTime.Seconds(6), DateTime.Seconds(8) },
	hard = { DateTime.Seconds(3), DateTime.Seconds(6) }
}
LateAttackDelays =
{
	easy = { DateTime.Seconds(4), DateTime.Seconds(7) },
	normal = { DateTime.Seconds(2), DateTime.Seconds(5) },
	hard = { DateTime.Seconds(1), DateTime.Seconds(3) }
}

AtreidesInfantryTypes = { "light_inf", "light_inf", "light_inf", "trooper", "trooper" }
AtreidesVehicleTypes = { "trike", "trike", "quad" }
AtreidesMainTankTypes =
{
	EarlyGame = {"combat_tank_a", "combat_tank_a", "siege_tank"},
	LateGame = {"combat_tank_a", "siege_tank", "missile_tank", "sonic_tank" }
}
AtreidesSmallTankTypes = { "combat_tank_a", "combat_tank_a", "siege_tank" }
AtreidesStarportTypes = { "trike.starport", "trike.starport", "quad.starport", "combat_tank_a.starport", "combat_tank_a.starport", "siege_tank.starport", "missile_tank.starport" }

CorrinoInfantryTypes = { "light_inf", "trooper", "sardaukar" }

ActivateAI = function()
	IdlingUnits[AtreidesMain] = Utils.Concat(Reinforcements.Reinforce(AtreidesMain, InitialAtreidesReinforcements[Difficulty][1], InitialAtreidesPaths[1]), Reinforcements.Reinforce(AtreidesMain, InitialAtreidesReinforcements[Difficulty][2], InitialAtreidesPaths[2]))
	IdlingUnits[AtreidesSmall] = Reinforcements.Reinforce(AtreidesSmall, InitialAtreidesReinforcements[Difficulty][1], InitialAtreidesPaths[3])
	IdlingUnits[Corrino] = Reinforcements.Reinforce(Corrino, InitialCorrinoReinforcements, InitialCorrinoPath)

	DefendAndRepairBase(AtreidesMain, AtreidesMainBase, 0.75, AttackGroupSize[Difficulty])
	DefendAndRepairBase(AtreidesSmall, AtreidesSmallBase, 0.75, AttackGroupSize[Difficulty])
	DefendAndRepairBase(Corrino, CorrinoBase, 0.75, AttackGroupSize[Difficulty])

	local delay = function()
		if EarlyGameStage >= DateTime.GameTime then
			return Utils.RandomInteger(EarlyAttackDelays[Difficulty][1], EarlyAttackDelays[Difficulty][2] + 1)
		else
			return Utils.RandomInteger(LateAttackDelays[Difficulty][1], LateAttackDelays[Difficulty][2] + 1)
		end
	end
	local infantryToBuildAtreides = function() return { Utils.Random(AtreidesInfantryTypes) } end
	local infantryToBuildCorrino = function() return { Utils.Random(CorrinoInfantryTypes) } end
	local vehilcesToBuild = function() return { Utils.Random(AtreidesVehicleTypes) } end
	local tanksToBuildMain = function()
		if EarlyGameStage >= DateTime.GameTime then
			return { Utils.Random(AtreidesMainTankTypes["EarlyGame"]) }
		else
			return { Utils.Random(AtreidesMainTankTypes["LateGame"]) }
		end end
	local tanksToBuildSmall = function() return { Utils.Random(AtreidesSmallTankTypes) } end
	local unitsToBuy = function() return { Utils.Random(AtreidesStarportTypes) } end
	local attackThresholdSize = AttackGroupSize[Difficulty] * 2.5

	Trigger.AfterDelay(InitialProductionDelay["AtreidesMain"][Difficulty], function()
		ProduceUnits(AtreidesMain, ALightFactory1, delay, vehilcesToBuild, AttackGroupSize[Difficulty], attackThresholdSize)
		ProduceUnits(AtreidesMain, AHeavyFactory1, delay, tanksToBuildMain, AttackGroupSize[Difficulty], attackThresholdSize)
		ProduceUnits(AtreidesMain, AStarport, delay, unitsToBuy, AttackGroupSize[Difficulty], attackThresholdSize)
	end)
	Trigger.AfterDelay(InitialProductionDelay["AtreidesSmall"][Difficulty], function()
		ProduceUnits(AtreidesSmall, ABarracks, delay, infantryToBuildAtreides, AttackGroupSize[Difficulty], attackThresholdSize)
		ProduceUnits(AtreidesSmall, AHeavyFactory2, delay, tanksToBuildSmall, AttackGroupSize[Difficulty], attackThresholdSize)
	end)
	Trigger.AfterDelay(InitialProductionDelay["Corrino"][Difficulty], function()
		ProduceUnits(Corrino, CBarracks, delay, infantryToBuildCorrino, AttackGroupSize[Difficulty], attackThresholdSize)
	end)

	ActivateCrusherOnProductions({ "combat_tank_a", "combat_tank_a.starport" },  { AHeavyFactory1, AHeavyFactory2, AStarport })
end
