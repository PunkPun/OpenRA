--[[
   Copyright (c) The OpenRA Developers and Contributors
   This file is part of OpenRA, which is free software. It is made
   available to you under the terms of the GNU General Public License
   as published by the Free Software Foundation, either version 3 of
   the License, or (at your option) any later version. For more
   information, see COPYING.
]]

EarlyGameStage = DateTime.Minutes(7)

AttackGroupSize =
{
	easy = 6,
	normal = 8,
	hard = 10
}

EarlyAttackDelays =
{

	easy = { DateTime.Seconds(9), DateTime.Seconds(13) },
	normal = { DateTime.Seconds(7), DateTime.Seconds(10) },
	hard = { DateTime.Seconds(4), DateTime.Seconds(7) }
}
LateAttackDelays =
{
	easy = { DateTime.Seconds(4), DateTime.Seconds(7) },
	normal = { DateTime.Seconds(2), DateTime.Seconds(5) },
	hard = { DateTime.Seconds(1), DateTime.Seconds(3) }
}

InitialProductionDelay = {
	AtreidesMain =
	{
		easy = DateTime.Seconds(180),
		normal = DateTime.Seconds(140),
		hard = DateTime.Seconds(80)
	},
	AtreidesSmall =
	{
		easy = DateTime.Seconds(140),
		normal = DateTime.Seconds(80),
		hard = DateTime.Seconds(30)
	},
	CorrinoMain =
	{
		easy = DateTime.Seconds(160),
		normal = DateTime.Seconds(140),
		hard = DateTime.Seconds(80)
	},
	CorrinoSmall =
	{
		easy = DateTime.Seconds(80),
		normal = DateTime.Seconds(60),
		hard = DateTime.Seconds(30)
	}
}

EnemyInfantryTypes = { "light_inf", "light_inf", "light_inf", "trooper", "trooper" }
EnemyVehicleTypes = { "trike", "trike", "quad" }

AtreidesMainTankTypes = {
	EarlyGame ={"combat_tank_a", "combat_tank_a", "siege_tank", "missile_tank"},
	LateGame = {"combat_tank_a", "combat_tank_a", "siege_tank", "missile_tank", "sonic_tank"}
}
AtreidesSmallTankTypes = { "combat_tank_a", "combat_tank_a", "siege_tank" }
AtreidesStarportTypes = {
	EarlyGame ={ "trike.starport", "trike.starport", "trike.starport", "quad.starport","quad.starport", "combat_tank_a.starport"},
	LateGame ={ "trike.starport", "trike.starport", "quad.starport", "combat_tank_a.starport", "combat_tank_a.starport", "siege_tank.starport", "missile_tank.starport"}
}
CorrinoMainInfantryTypes = { "light_inf", "light_inf", "trooper", "sardaukar" }
CorrinoMainTankTypes = {
	EarlyGame = {"combat_tank_h"},
	LateGame = {"combat_tank_h", "combat_tank_h", "siege_tank", "missile_tank" }
}
CorrinoSmallTankTypes = { "combat_tank_h", "combat_tank_h", "siege_tank" }
CorrinoStarportTypes = {
	EarlyGame = {"trike.starport", "trike.starport", "quad.starport", "combat_tank_h.starport", "combat_tank_h.starport", "siege_tank.starport", "missile_tank.starport"},
	LateGame = {"trike.starport", "trike.starport", "quad.starport"}
}

ActivateAI = function()
	IdlingUnits[AtreidesMain] = Utils.Concat(Reinforcements.Reinforce(AtreidesMain, InitialAtreidesReinforcements[1], InitialAtreidesPaths[1]), Utils.Concat(Reinforcements.Reinforce(AtreidesMain, InitialAtreidesReinforcements[2], InitialAtreidesPaths[2]), Reinforcements.Reinforce(AtreidesMain, InitialAtreidesReinforcements[3], InitialAtreidesPaths[3])))
	IdlingUnits[AtreidesSmall1] = Utils.Concat(Reinforcements.Reinforce(AtreidesSmall1, InitialAtreidesReinforcements[4], InitialAtreidesPaths[4]), Reinforcements.Reinforce(AtreidesSmall1, InitialAtreidesReinforcements[5], InitialAtreidesPaths[5]))
	IdlingUnits[AtreidesSmall2] = Reinforcements.Reinforce(AtreidesSmall2, InitialAtreidesReinforcements[6], InitialAtreidesPaths[6])
	IdlingUnits[CorrinoMain] = Reinforcements.Reinforce(CorrinoMain, InitialCorrinoReinforcements, InitialCorrinoPaths[1])
	IdlingUnits[CorrinoSmall] = Reinforcements.Reinforce(CorrinoMain, InitialCorrinoReinforcements, InitialCorrinoPaths[2])

	DefendAndRepairBase(AtreidesMain, AtreidesMainBase, 0.75, AttackGroupSize[Difficulty])
	DefendAndRepairBase(AtreidesSmall1, AtreidesSmall1Base, 0.75, AttackGroupSize[Difficulty])
	DefendAndRepairBase(AtreidesSmall2, AtreidesSmall2Base, 0.75, AttackGroupSize[Difficulty])
	DefendAndRepairBase(CorrinoMain, CorrinoMainBase, 0.75, AttackGroupSize[Difficulty])
	DefendAndRepairBase(CorrinoSmall, CorrinoSmallBase, 0.75, AttackGroupSize[Difficulty])

	local delay = function()
		if EarlyGameStage >= DateTime.GameTime then
			return Utils.RandomInteger(EarlyAttackDelays[Difficulty][1], EarlyAttackDelays[Difficulty][2] + 1)
		else
			return Utils.RandomInteger(LateAttackDelays[Difficulty][1], LateAttackDelays[Difficulty][2] + 1)
		end
	end
	local infantryToBuild = function() return { Utils.Random(EnemyInfantryTypes) } end
	local infantryToBuildCorrinoMain = function() return { Utils.Random(CorrinoMainInfantryTypes) } end
	local vehilcesToBuild = function() return { Utils.Random(EnemyVehicleTypes) } end
	local tanksToBuildAtreidesMain = function()
		if EarlyGameStage >= DateTime.GameTime then
		return { Utils.Random(AtreidesMainTankTypes["EarlyGame"]) }
		else
			return { Utils.Random(AtreidesMainTankTypes["LateGame"]) }
		end
	end
	local tanksToBuildAtreidesSmall = function() return { Utils.Random(AtreidesSmallTankTypes) } end

	local tanksToBuildCorrinoMain = function()
		if EarlyGameStage >= DateTime.GameTime then
		return { Utils.Random(CorrinoMainTankTypes["EarlyGame"]) }
		else
			return { Utils.Random(CorrinoMainTankTypes["LateGame"]) }
		end
	end
	local tanksToBuildCorrinoSmall = function() return { Utils.Random(CorrinoSmallTankTypes) } end
	local unitsToBuyAtreides = function()
		if EarlyGameStage >= DateTime.GameTime then
			return { Utils.Random(AtreidesStarportTypes["EarlyGame"]) }
		else
			return { Utils.Random(AtreidesStarportTypes["LateGame"]) }
		end
	end

	local unitsToBuyCorrino = function()
		if EarlyGameStage >= DateTime.GameTime then
			return { Utils.Random(CorrinoStarportTypes["EarlyGame"]) }
		else
			return { Utils.Random(CorrinoStarportTypes["LateGame"])}
		end
	end
	local attackThresholdSize = AttackGroupSize[Difficulty] * 2.5
	Trigger.AfterDelay(InitialProductionDelay["AtreidesMain"][Difficulty], function ()
		ProduceUnits(AtreidesMain, ABarracks1, delay, infantryToBuild, AttackGroupSize[Difficulty], attackThresholdSize)
		ProduceUnits(AtreidesMain, ALightFactory1, delay, vehilcesToBuild, AttackGroupSize[Difficulty], attackThresholdSize)
		ProduceUnits(AtreidesMain, AHeavyFactory1, delay, tanksToBuildAtreidesMain, AttackGroupSize[Difficulty], attackThresholdSize)
		ProduceUnits(AtreidesMain, AStarport, delay, unitsToBuyAtreides, AttackGroupSize[Difficulty], attackThresholdSize)
	end)
	Trigger.AfterDelay(InitialProductionDelay["AtreidesSmall"][Difficulty], function ()
		ProduceUnits(AtreidesSmall1, ABarracks3, delay, infantryToBuild, AttackGroupSize[Difficulty], attackThresholdSize)
		ProduceUnits(AtreidesSmall1, ALightFactory2, delay, vehilcesToBuild, AttackGroupSize[Difficulty], attackThresholdSize)
		ProduceUnits(AtreidesSmall1, AHeavyFactory2, delay, tanksToBuildAtreidesSmall, AttackGroupSize[Difficulty], attackThresholdSize)
	end)
	ProduceUnits(AtreidesSmall2, ABarracks4, delay, infantryToBuild, AttackGroupSize[Difficulty], attackThresholdSize)

	Trigger.AfterDelay(InitialProductionDelay["CorrinoMain"][Difficulty], function ()
		ProduceUnits(CorrinoMain, CBarracks1, delay, infantryToBuildCorrinoMain, AttackGroupSize[Difficulty], attackThresholdSize)
		ProduceUnits(CorrinoMain, CLightFactory1, delay, vehilcesToBuild, AttackGroupSize[Difficulty], attackThresholdSize)
		ProduceUnits(CorrinoMain, CHeavyFactory1, delay, tanksToBuildCorrinoMain, AttackGroupSize[Difficulty], attackThresholdSize)
		ProduceUnits(CorrinoMain, CStarport, delay, unitsToBuyCorrino, AttackGroupSize[Difficulty], attackThresholdSize)
	end)
	Trigger.AfterDelay(InitialProductionDelay["CorrinoSmall"][Difficulty], function ()
		ProduceUnits(CorrinoSmall, CBarracks2, delay, infantryToBuild, AttackGroupSize[Difficulty], attackThresholdSize)
		ProduceUnits(CorrinoSmall, CLightFactory2, delay, vehilcesToBuild, AttackGroupSize[Difficulty], attackThresholdSize)
		ProduceUnits(CorrinoSmall, CHeavyFactory2, delay, tanksToBuildCorrinoSmall, AttackGroupSize[Difficulty], attackThresholdSize)
	end)
	local crusherTypes = { "combat_tank_a", "combat_tank_a.starport", "combat_tank_h", "combat_tank_h.starport" }
	local crusherFactories = { AStarport, AHeavyFactory1, AHeavyFactory2, CHeavyFactory1, CHeavyFactory2, CStarport }
	ActivateCrusherOnProductions(crusherTypes, crusherFactories)
end
