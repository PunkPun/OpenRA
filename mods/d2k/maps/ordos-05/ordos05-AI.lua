--[[
   Copyright (c) The OpenRA Developers and Contributors
   This file is part of OpenRA, which is free software. It is made
   available to you under the terms of the GNU General Public License
   as published by the Free Software Foundation, either version 3 of
   the License, or (at your option) any later version. For more
   information, see COPYING.
]]
EarlyGameStage = DateTime.Minutes(5)
InitialProductionDelay = {
	AtreidesMain =
	{
		easy = DateTime.Seconds(130),
		normal = DateTime.Seconds(80),
		hard = DateTime.Seconds(50)
	},
	AtreidesSmall1 =
	{
		easy = DateTime.Seconds(120),
		normal = DateTime.Seconds(60),
		hard = DateTime.Seconds(30)
	},
	AtreidesSmall2 =
	{
		easy = DateTime.Seconds(60),
		normal = DateTime.Seconds(30),
		hard = DateTime.Seconds(0)
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
	easy = { DateTime.Seconds(7), DateTime.Seconds(11) },
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
EnemyTankType = { "combat_tank_a" }

InitAIUnits = function(house)
	LastHarvesterEaten[house] = true
	if house ~= AtreidesSmall3 then
		IdlingUnits[house] = Reinforcements.Reinforce(house, InitialReinforcements[house.InternalName], InitialReinforcementsPaths[house.InternalName])
	else
		IdlingUnits[house] = { }
	end

	DefendAndRepairBase(house, Base[house.InternalName], 0.75, AttackGroupSize[Difficulty])
end

ActivateAIProduction = function()
	local delay = function()
		if EarlyGameStage >= DateTime.GameTime then
			return Utils.RandomInteger(EarlyAttackDelays[Difficulty][1], EarlyAttackDelays[Difficulty][2] + 1)
		else
			return Utils.RandomInteger(LateAttackDelays[Difficulty][1], LateAttackDelays[Difficulty][2] + 1)
		end
	end
	local infantryToBuild = function() return { Utils.Random(EnemyInfantryTypes) } end
	local vehiclesToBuild = function() return { Utils.Random(EnemyVehicleTypes) } end
	local tanksToBuild = function() return EnemyTankType end
	local attackTresholdSize = AttackGroupSize[Difficulty] * 2.5
	Trigger.AfterDelay(InitialProductionDelay["AtreidesMain"][Difficulty], function()
		ProduceUnits(AtreidesMain, ABarracks1, delay, infantryToBuild, AttackGroupSize[Difficulty], attackTresholdSize)
		ProduceUnits(AtreidesMain, ALightFactory, delay, vehiclesToBuild, AttackGroupSize[Difficulty], attackTresholdSize)
		ProduceUnits(AtreidesMain, AHeavyFactory, delay, tanksToBuild, AttackGroupSize[Difficulty], attackTresholdSize)
	end)
	Trigger.AfterDelay(InitialProductionDelay["AtreidesSmall1"][Difficulty], function()
		ProduceUnits(AtreidesSmall1, ABarracks2, delay, infantryToBuild, AttackGroupSize[Difficulty], attackTresholdSize)
	end)
	Trigger.AfterDelay(InitialProductionDelay["AtreidesSmall2"][Difficulty], function()
		ProduceUnits(AtreidesSmall2, ABarracks3, delay, infantryToBuild, AttackGroupSize[Difficulty], attackTresholdSize)
	end)

	ActivateCrusherOnProductions({"combat_tank_a","combat_tank_a.starport"}, {AStarport, AHeavyFactory})
	AIProductionActivated = true
end

ActivateAI = function()
	InitAIUnits(AtreidesMain)
	InitAIUnits(AtreidesSmall1)
	InitAIUnits(AtreidesSmall2)
	InitAIUnits(AtreidesSmall3)
end
