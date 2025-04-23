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
	Ordos =
	{
		easy = DateTime.Seconds(120),
		normal = DateTime.Seconds(60),
		hard = DateTime.Seconds(0)
	},
	AtreidesEnemy =
	{
		easy = DateTime.Seconds(180),
		normal = DateTime.Seconds(120),
		hard = DateTime.Seconds(60)
	},
	MercenaryEnemy =
	{
		easy = DateTime.Seconds(160),
		normal = DateTime.Seconds(80),
		hard = DateTime.Seconds(30)
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
	easy = { DateTime.Seconds(10), DateTime.Seconds(13) },
	normal = { DateTime.Seconds(6), DateTime.Seconds(8) },
	hard = { DateTime.Seconds(3), DateTime.Seconds(5) }
}
LateAttackDelays =
{
	easy = { DateTime.Seconds(4), DateTime.Seconds(7) },
	normal = { DateTime.Seconds(2), DateTime.Seconds(5) },
	hard = { DateTime.Seconds(1), DateTime.Seconds(3) }
}

EnemyInfantryTypes = { "light_inf", "light_inf", "trooper", "trooper", "trooper" }

OrdosVehicleTypes = { "raider", "raider", "quad" }
OrdosTankTypes = {
	EarlyGame = { "combat_tank_o", "combat_tank_o", "siege_tank"},
	LateGame = { "combat_tank_o", "combat_tank_o", "siege_tank", "deviator" }
}
OrdosStarportTypes =
{
	EarlyGame = {"trike.starport", "trike.starport", "quad.starport", "combat_tank_o.starport"},
	LateGame = {"trike.starport", "trike.starport", "quad.starport", "combat_tank_o.starport", "combat_tank_o.starport", "siege_tank.starport", "missile_tank.starport"}
}

AtreidesVehicleTypes = { "trike", "trike", "quad" }
AtreidesTankTypes = {
	EarlyGame ={ "combat_tank_a" },
	LateGame ={ "combat_tank_a", "combat_tank_a", "siege_tank" }}
AtreidesStarportTypes = {
	EarlyGame ={"trike.starport", "trike.starport", "quad.starport", "combat_tank_a.starport", "combat_tank_a.starport"},
	LateGame ={"trike.starport", "quad.starport", "combat_tank_a.starport", "combat_tank_a.starport", "siege_tank.starport", "missile_tank.starport"} }

MercenaryTankTypes = { "combat_tank_o", "combat_tank_o", "siege_tank" }

ActivateAI = function()
	IdlingUnits[Ordos] = Utils.Concat(Reinforcements.Reinforce(Ordos, InitialOrdosReinforcements[1], InitialOrdosPaths[1]), Reinforcements.Reinforce(Ordos, InitialOrdosReinforcements[2], InitialOrdosPaths[2]))
	IdlingUnits[AtreidesEnemy] = Reinforcements.Reinforce(AtreidesEnemy, InitialAtreidesReinforcements, InitialAtreidesPath)
	IdlingUnits[AtreidesNeutral] = { }
	IdlingUnits[MercenaryEnemy] = Reinforcements.Reinforce(MercenaryEnemy, InitialMercenaryReinforcements, InitialMercenaryPath)
	IdlingUnits[MercenaryAlly] = { }

	DefendAndRepairBase(Ordos, OrdosBase, 0.75, AttackGroupSize[Difficulty])
	DefendAndRepairBase(AtreidesEnemy, AtreidesBase, 0.75, AttackGroupSize[Difficulty])
	DefendAndRepairBase(MercenaryEnemy, MercenaryBase, 0.75, AttackGroupSize[Difficulty])

	local delay = function()
		if EarlyGameStage >= DateTime.GameTime then
			return Utils.RandomInteger(EarlyAttackDelays[Difficulty][1], EarlyAttackDelays[Difficulty][2] + 1)
		else
			return Utils.RandomInteger(LateAttackDelays[Difficulty][1], LateAttackDelays[Difficulty][2] + 1)
		end
	end
	local infantryToBuild = function() return { Utils.Random(EnemyInfantryTypes) } end
	local vehilcesToBuildOrdos = function() return { Utils.Random(OrdosVehicleTypes) } end
	local vehilcesToBuildAtreides = function() return { Utils.Random(AtreidesVehicleTypes) } end

	local tanksToBuildOrdos = function()
		if EarlyGameStage >= DateTime.GameTime then
			return { Utils.Random(OrdosTankTypes["EarlyGame"]) }
		else
			return { Utils.Random(OrdosTankTypes["LateGame"]) }
		end
	end
	local tanksToBuildAtreides = function()
		if EarlyGameStage >= DateTime.GameTime then
			return { Utils.Random(AtreidesTankTypes["EarlyGame"]) }
		else
			return { Utils.Random(AtreidesTankTypes["LateGame"]) }
		end
	end
	local tanksToBuildMercenary = function() return { Utils.Random(MercenaryTankTypes) } end
	local unitsToBuyOrdos = function()
		if EarlyGameStage >= DateTime.GameTime then
			return { Utils.Random(OrdosStarportTypes["EarlyGame"]) }
		else
			return { Utils.Random(OrdosStarportTypes["LateGame"]) }
		end
	 end
	 local unitsToBuyAtreides = function()
		if EarlyGameStage >= DateTime.GameTime then
			return { Utils.Random(AtreidesStarportTypes["EarlyGame"]) }
		else
			return { Utils.Random(AtreidesStarportTypes["LateGame"]) }
		end
	  end
	local attackThresholdSize = AttackGroupSize[Difficulty] * 2.5

	Trigger.AfterDelay(InitialProductionDelay["Ordos"][Difficulty], function ()
		ProduceUnits(Ordos, OBarracks1, delay, infantryToBuild, AttackGroupSize[Difficulty], attackThresholdSize)
		ProduceUnits(Ordos, OLightFactory, delay, vehilcesToBuildOrdos, AttackGroupSize[Difficulty], attackThresholdSize)
		ProduceUnits(Ordos, OHeavyFactory, delay, tanksToBuildOrdos, AttackGroupSize[Difficulty], attackThresholdSize)
		ProduceUnits(Ordos, OStarport, delay, unitsToBuyOrdos, AttackGroupSize[Difficulty], attackThresholdSize)
	end)

	Trigger.AfterDelay(InitialProductionDelay["AtreidesEnemy"][Difficulty], function ()
		ProduceUnits(AtreidesEnemy, ABarracks1, delay, infantryToBuild, AttackGroupSize[Difficulty], attackThresholdSize)
		ProduceUnits(AtreidesEnemy, ALightFactory, delay, vehilcesToBuildAtreides, AttackGroupSize[Difficulty], attackThresholdSize)
		ProduceUnits(AtreidesEnemy, AHeavyFactory, delay, tanksToBuildAtreides, AttackGroupSize[Difficulty], attackThresholdSize)
		ProduceUnits(AtreidesEnemy, AStarport, delay, unitsToBuyAtreides, AttackGroupSize[Difficulty], attackThresholdSize)
	end)

	Trigger.AfterDelay(InitialProductionDelay["AtreidesEnemy"][Difficulty], function ()
		ProduceUnits(MercenaryEnemy, MHeavyFactory, delay, tanksToBuildMercenary, AttackGroupSize[Difficulty], attackThresholdSize)
	end)

	local crusherTypes = { "combat_tank_a", "combat_tank_a.starport", "combat_tank_o", "combat_tank_.starport" }
	local crusherFactories = { OHeavyFactory, OStarport, MHeavyFactory, MStarport, AHeavyFactory, AStarport }

	ActivateCrusherOnProductions(crusherTypes, crusherFactories)
end
