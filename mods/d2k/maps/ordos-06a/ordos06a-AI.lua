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
		easy = DateTime.Seconds(130),
		normal = DateTime.Seconds(80),
		hard = DateTime.Seconds(50)
	},
	HarkonnenSmall =
	{
		easy = DateTime.Seconds(120),
		normal = DateTime.Seconds(60),
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
	easy = { DateTime.Seconds(7), DateTime.Seconds(10) },
	normal = { DateTime.Seconds(5), DateTime.Seconds(7) },
	hard = { DateTime.Seconds(3), DateTime.Seconds(5) }
}
LateAttackDelays =
{
	easy = { DateTime.Seconds(4), DateTime.Seconds(7) },
	normal = { DateTime.Seconds(2), DateTime.Seconds(5) },
	hard = { DateTime.Seconds(1), DateTime.Seconds(3) }
}

EnemyInfantryTypes = { "light_inf", "light_inf", "light_inf", "trooper", "trooper" }
EnemyVehicleTypes = { "trike", "trike", "quad" }

AtreidesTankTypes = { "combat_tank_a", "siege_tank", "missile_tank" }
HarkonnenTankTypes = { "combat_tank_h", "siege_tank" }

InitAIUnits = function(house)
	LastHarvesterEaten[house] = true
	IdlingUnits[house] = Reinforcements.Reinforce(house, InitialReinforcements[house.InternalName], InitialReinforcementsPaths[house.InternalName])

	DefendAndRepairBase(house, Base[house.InternalName], 0.75, AttackGroupSize[Difficulty])
end

ActivateAI = function()
	InitAIUnits(Atreides)
	InitAIUnits(Harkonnen)

	local delay = function()
		if EarlyGameStage >= DateTime.GameTime then
			return Utils.RandomInteger(EarlyAttackDelays[Difficulty][1], EarlyAttackDelays[Difficulty][2] + 1)
		else
			return Utils.RandomInteger(LateAttackDelays[Difficulty][1], LateAttackDelays[Difficulty][2] + 1)
		end
	end
	local infantryToBuild = function() return { Utils.Random(EnemyInfantryTypes) } end
	local vehiclesToBuild = function() return { Utils.Random(EnemyVehicleTypes) } end
	local tanksToBuildAtreides = function() return { Utils.Random(AtreidesTankTypes) } end
	local tanksToBuildHarkonnen = function() return { Utils.Random(HarkonnenTankTypes) } end
	local attackTresholdSize = AttackGroupSize[Difficulty] * 2.5
	Trigger.AfterDelay(InitialProductionDelay["AtreidesMain"][Difficulty], function()
		ProduceUnits(Atreides, ABarracks, delay, infantryToBuild, AttackGroupSize[Difficulty], attackTresholdSize)
		ProduceUnits(Atreides, ALightFactory, delay, vehiclesToBuild, AttackGroupSize[Difficulty], attackTresholdSize)
		ProduceUnits(Atreides, AHeavyFactory, delay, tanksToBuildAtreides, AttackGroupSize[Difficulty], attackTresholdSize)
	end)

	Trigger.AfterDelay(InitialProductionDelay["HarkonnenSmall"][Difficulty], function()
		ProduceUnits(Harkonnen, HBarracks, delay, infantryToBuild, AttackGroupSize[Difficulty], attackTresholdSize)
		ProduceUnits(Harkonnen, HHeavyFactory, delay, tanksToBuildHarkonnen, AttackGroupSize[Difficulty], attackTresholdSize)
	end)

	ActivateCrusherOnProductions({ "combat_tank_a", "combat_tank_h" }, { HHeavyFactory, AHeavyFactory, })


end
