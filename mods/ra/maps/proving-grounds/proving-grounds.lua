--[[
   Copyright (c) The OpenRA Developers and Contributors
   This file is part of OpenRA, which is free software. It is made
   available to you under the terms of the GNU General Public License
   as published by the Free Software Foundation, either version 3 of
   the License, or (at your option) any later version. For more
   information, see COPYING.
]]

--- This mission's human player.
local USSR = Player.GetPlayer("USSR")

--- Owner of the inactive Soviet bases.
local BadGuy = Player.GetPlayer("BadGuy")

--- Owner of most hostile Allied forces.
--- (France guards crates near the west edge.)
local Greece = Player.GetPlayer("Greece")

--- Owner of the green smoke signals.
local England = Player.GetPlayer("England")

--- Both Soviet players, grouped here for later targeting.
local SovietPlayers = { BadGuy, USSR }

---@type integer Objective to defend the Tech Center.
local TechObjective

---@type integer Objective to reach the Sub Pen.
local PenObjective

---@type integer Objective to wipe out all intruders owned by Greece.
local IntruderObjective

--- Are the Soviets defeated, including the player?
local DefeatStarted = false

--- Is victory ready to be announced?
local VictoryStarted = false

--- Has the player regained use of the Sub Pen?
local NavalBaseFound = false

--- Has the Cruiser team arrived?
local CruisersArrived = false

--- Has Tanya's first boat been reinforced?
local TanyaReinforced = false

--- Prefix for the player messages.
local BattlefieldControl = UserInterface.GetFluentMessage("battlefield-control")

--- Color to use for most timer text.
local TimerColor = HSLColor.White

--- Ticks until Cruisers arrive on each difficulty.
--- The timer begins once the Service Depot is regained.
local CruiserDelays =
{
	easy = DateTime.Seconds(390),
	normal = DateTime.Seconds(300),
	hard = DateTime.Seconds(240)
}

--- Ticks until the Longbow attack arrives on each difficulty.
--- The timer begins once the Service Depot is regained.
local LongbowDelays =
{
	easy = CruiserDelays.easy * 2,
	normal = CruiserDelays.normal * 2,
	hard = CruiserDelays.hard
}

--- A collection of teams triggered by the Cruisers' river travel.
local EndTeams =
{
	end1 =
	{
		types = { "e3", "e3", "e3", "e3", "e3" },
		path = { Waypoint90.Location, NavalRocketUnload.Location }
	},
	end2 =
	{
		types = { "2tnk", "2tnk", "arty" },
		path = { Waypoint93.Location, Waypoint95.Location }
	},
	end5 =
	{
		types = { "2tnk", "2tnk", "2tnk" },
		path = { Waypoint65.Location, Waypoint66.Location }
	},
	sea7 =
	{
		types = { "1tnk", "1tnk", "arty", "arty" },
		path = { Waypoint94.Location, Waypoint67.Location }
	},
	sea8 =
	{
		types = { "1tnk", "1tnk", "1tnk", "arty" },
		path = { Waypoint65.Location, Waypoint66.Location }
	}
}

--- Collection of cells used for triggers created at startup.
local Footprints =
{
	--- Beach rescue trigger by Waypoint 74.
	atk2 =
	{
		CPos.New(91, 92),
		CPos.New(91, 93),
		CPos.New(91, 94),
		CPos.New(91, 95),
		CPos.New(91, 96)
	},
	--- Outside the starting base by Waypoint 75.
	atk6 =
	{
		CPos.New(71, 72),
		CPos.New(71, 73),
		CPos.New(71, 74),
		CPos.New(71, 75),
		CPos.New(71, 76),
		CPos.New(71, 77),
		CPos.New(71, 78),
		CPos.New(71, 79)
	},
	--- Cells north/northeast of the mining outpost.
	atk7 =
	{
		--- Ore patch cells.
		CPos.New(83, 51),
		CPos.New(84, 51),
		CPos.New(85, 51),
		CPos.New(86, 51),
		CPos.New(87, 51),
		CPos.New(88, 51),
		CPos.New(89, 51),
		CPos.New(90, 51),
		CPos.New(91, 51),
		CPos.New(92, 51),
		CPos.New(93, 51),
		--- Path to NE corner base.
		CPos.New(98, 56),
		CPos.New(99, 56),
		CPos.New(99, 57),
		CPos.New(100, 57),
		CPos.New(101, 57),
		CPos.New(101, 58),
		CPos.New(102, 58),
		CPos.New(103, 58),
		CPos.New(103, 59),
		CPos.New(104, 59),
		CPos.New(104, 60),
	},
	--- Cells at the airbase that reveal the nearby river.
	des1 =
	{
		CPos.New(46, 19),
		CPos.New(46, 20),
		CPos.New(46, 21),
		CPos.New(46, 22),
		CPos.New(46, 23),
		CPos.New(46, 24),
		CPos.New(46, 25),
		CPos.New(46, 26),
		CPos.New(46, 27),
		CPos.New(46, 28),
		CPos.New(46, 29)
	},
	--- Land approach to the airbase.
	des3 =
	{
		--- West end.
		CPos.New(20, 18),
		CPos.New(20, 19),
		CPos.New(20, 20),
		CPos.New(20, 21),
		CPos.New(20, 22),
		CPos.New(20, 23),
		CPos.New(20, 24),
		CPos.New(20, 25),
		CPos.New(20, 26),
		CPos.New(20, 27),
		CPos.New(20, 28),
		CPos.New(20, 29),
		--- South end.
		CPos.New(21, 29),
		CPos.New(22, 29),
		CPos.New(23, 29),
		CPos.New(24, 29),
		CPos.New(25, 29),
		CPos.New(26, 29),
		CPos.New(27, 29),
		CPos.New(28, 29),
		CPos.New(29, 29),
		CPos.New(30, 29),
		CPos.New(31, 29),
		CPos.New(32, 29),
		CPos.New(33, 29),
		CPos.New(34, 29),
		--- East end.
		CPos.New(34, 28),
		CPos.New(34, 27),
		CPos.New(34, 26),
		CPos.New(34, 25),
		CPos.New(34, 24),
		CPos.New(34, 23),
		CPos.New(34, 22),
		CPos.New(33, 22),
		CPos.New(33, 21),
		CPos.New(33, 20),
		CPos.New(33, 19),
		CPos.New(33, 18)
	},
	--- River between waypoints 73 and 84.
	ent2 =
	{
		CPos.New(35, 44),
		CPos.New(35, 45),
		CPos.New(36, 45),
		CPos.New(36, 46),
		CPos.New(37, 46),
		CPos.New(37, 47),
		CPos.New(38, 47),
		CPos.New(38, 48),
		CPos.New(38, 48),
		CPos.New(39, 48),
		CPos.New(39, 49),
		CPos.New(39, 50)
	},
	--- Small strip of coast west of Waypoint 57.
	ent3 =
	{
		CPos.New(69, 18),
		CPos.New(69, 19),
		CPos.New(69, 20),
		CPos.New(69, 21),
		CPos.New(69, 22),
		CPos.New(69, 23)
	},
	--- River between waypoints 73 and 76.
	ent4 =
	{
		CPos.New(21, 62),
		CPos.New(22, 62),
		CPos.New(23, 62),
		CPos.New(24, 62),
		CPos.New(25, 62),
		CPos.New(26, 62),
		CPos.New(27, 62),
		CPos.New(28, 62),
		CPos.New(29, 62),
		CPos.New(30, 62)
	},
	--- River between waypoints 76 and 53.
	ent5 =
	{
		CPos.New(19, 82),
		CPos.New(20, 82),
		CPos.New(21, 82),
		CPos.New(22, 82),
		CPos.New(23, 82),
		CPos.New(24, 82),
		CPos.New(25, 82),
		CPos.New(26, 82),
		CPos.New(27, 82),
		CPos.New(28, 82),
		CPos.New(29, 82)
	},
	--- River between waypoints 53 and 72.
	ent6 =
	{
		CPos.New(19, 101),
		CPos.New(20, 101),
		CPos.New(21, 101),
		CPos.New(22, 101),
		CPos.New(23, 101),
		CPos.New(24, 101),
		CPos.New(25, 101),
		CPos.New(26, 101),
		CPos.New(27, 101),
		CPos.New(28, 101),
		CPos.New(29, 101),
		CPos.New(30, 101)
	},
	--- River between waypoints 73 and 84.
	rev3 =
	{
		CPos.New(33, 45),
		CPos.New(33, 46),
		CPos.New(33, 47),
		CPos.New(34, 47),
		CPos.New(34, 48),
		CPos.New(35, 48),
		CPos.New(35, 49),
		CPos.New(36, 48),
		CPos.New(36, 49),
		CPos.New(36, 50),
		CPos.New(37, 49),
		CPos.New(37, 50),
		CPos.New(37, 51)
	},
	--- River near Waypoint 84.
	rev4 =
	{
		CPos.New(49, 34),
		CPos.New(48, 35),
		CPos.New(49, 35),
		CPos.New(47, 36),
		CPos.New(48, 36),
		CPos.New(46, 37),
		CPos.New(47, 37),
		CPos.New(45, 38),
		CPos.New(46, 38),
	},
	--- Between the first flare and a beach to its south. Non-original.
	tanya =
	{
		CPos.New(92, 90),
		CPos.New(93, 90),
		CPos.New(94, 90),
		CPos.New(95, 90),
		CPos.New(96, 90)
	}
}

local function FinishCountdown()
	DateTime.TimeLimit = 0
	TimerColor = USSR.Color
	local text = UserInterface.GetFluentMessage("cruisers-arrived")

	for i = 0, 5 do
		local flash

		if i % 2 == 0 then
			flash = HSLColor.White
		end

		Trigger.AfterDelay(DateTime.Seconds(i), function()
			UserInterface.SetMissionText(text, flash or TimerColor)
		end)
	end

	Trigger.AfterDelay(DateTime.Seconds(6), function()
		UserInterface.SetMissionText("")
	end)
end

---@param ticks integer
---@param interval integer
local function UpdateCountdown(ticks, interval)
	if ticks < 1 then
		return
	end

	local text = UserInterface.GetFluentMessage("cruisers-arrive-in", { ["time"] = Utils.FormatTime(ticks)})
	UserInterface.SetMissionText(text, TimerColor)

	Trigger.AfterDelay(interval, function()
		UpdateCountdown(ticks - interval, interval)
	end)
end

---@param duration integer
local function StartCountdown(duration)
	DateTime.TimeLimit = duration
	Media.PlaySpeechNotification(USSR, "TimerStarted")
	UpdateCountdown(duration, DateTime.Seconds(1))
	Trigger.OnTimerExpired(FinishCountdown)

	Trigger.AfterDelay(duration - DateTime.Seconds(120), function()
		TimerColor = USSR.Color
	end)
end

--- Play a speech for the player after a delay.
---@param speech string Speech notification to play.
---@param delay integer Ticks until the speech plays.
local function PlayDelayedSpeech(speech, delay)
	Trigger.AfterDelay(delay, function()
		Media.PlaySpeechNotification(USSR, speech)
	end)
end

--- End the mission as a success for the player.
local function AnnounceSovietVictory()
	if VictoryStarted then
		return
	end

	VictoryStarted = true
	PlayDelayedSpeech("ObjectiveMet", DateTime.Seconds(1))

	-- Like the original, it is possible to destroy Greece without naval units.
	-- For that, PenObjective is marked here (for a second time in most cases).
	local objectives = { PenObjective, IntruderObjective, TechObjective }

	Trigger.AfterDelay(DateTime.Seconds(2), function()
		Utils.Do(objectives, USSR.MarkCompletedObjective)
	end)
end

--- Mark a failed Soviet objective after a delay, with optional speech.
---@param objective integer ID of the failed objective.
---@param delay? integer Delay until the failed objective is marked.
---@param speech? string Notification speech to be played.
---@param speechDelay? integer Delay until the speech is started.
local function AnnounceSovietDefeat(objective, delay, speech, speechDelay)
	if DefeatStarted or VictoryStarted then
		return
	end
	DefeatStarted = true

	if speech then
		PlayDelayedSpeech(speech, speechDelay or DateTime.Seconds(1))
	end

	Trigger.AfterDelay(delay or DateTime.Seconds(2), function()
		USSR.MarkFailedObjective(objective)
	end)
end

--- Tell a bot to sell their structures and make a final attack.
---@param player player
local function StartFireSale(player)
	local buildings = Utils.Where(player.GetActors(), function(actor)
		return actor.HasProperty("StartBuildingRepairs") end)

	if #buildings == 0 then
		Utils.Do(player.GetGroundAttackers(), IdleHunt)
		return
	end

	Utils.Do(buildings, function(b)
		b.Sell()
	end)

	-- Delay until the buildings are done selling
	-- (and spawning more soldiers or technicians).
	Trigger.OnAllRemovedFromWorld(buildings, function()
		Utils.Do(player.GetGroundAttackers(), IdleHunt)
	end)
end

--- Remove an actor with a safety check and optional delay.
---@param actor actor Actor to be removed.
---@param delay? integer Ticks to delay removal.
local function RemoveActor(actor, delay)
	if delay then
		Trigger.AfterDelay(delay, function()
			RemoveActor(actor)
		end)

		return
	end

	if actor and actor.IsInWorld then
		actor.Destroy()
	end
end

--- Spawn a supporting actor with optional delay and self-removal.
---@param type string Type of the new actor.
---@param owner player Owner of the new actor.
---@param location? cpos Location of the new actor.
---@param duration? integer Ticks to delay the actor's removal, after creation.
---@param delay? integer Ticks to delay adding the new actor to the world.
---@return actor
local function SpawnMiscActor(type, owner, location, duration, delay)
	delay = delay or 0
	local actor = Actor.Create(type, delay <= 0, { Owner = owner, Location = location or CPos.Zero })

	if delay > 0 then
		Trigger.AfterDelay(delay, function()
			actor.IsInWorld = true
		end)
	end

	if duration and duration > 0 then
		RemoveActor(actor, delay + duration)
	end

	return actor
end

--- Spawn a semi-permanent signal flare.
---@param location cpos
---@return actor
local function SpawnSignalFlare(location)
	return SpawnMiscActor("flare", England, location, -1)
end

--- Spawn a reveal camera for the player.
---@param location cpos Cell to use as the camera's center.
---@param duration? integer Ticks for the camera to linger (after delay, if any).
---@param type? string Camera's actor type.
---@param delay? integer Ticks to delay the camera's creation.
---@return actor
local function SpawnRevealCamera(location, duration, type, delay)
	duration = duration or DateTime.Seconds(6)
	type = type or "camera"
	return SpawnMiscActor(type, USSR, location, duration, delay)
end

--- Unload a team of Allied hunters at a beach.
---@param types string[]
---@param path cpos[]
local function ReinforceBasicBoatTeam(types, path)
	local boatTeam = Reinforcements.ReinforceWithTransport(Greece, "lst", types, path, { path[1] })
	local boat, passengers = boatTeam[1], boatTeam[2]
	Utils.Do(passengers, IdleHunt)

	return boat, passengers
end

--- Prepare a camera to show a transport shortly before (and after) it unloads.
---@param transport actor Transport to reveal.
---@param location cpos Spawn location for the camera and trigger.
---@param hold? integer Ticks to continue revealing once the unload is done.
---@param type? string Type of camera actor to spawn.
---@param range? wdist Distance at which the transport triggers the reveal.
local function PrepareTransportReveal(transport, location, hold, type, range)
	range = range or WDist.FromCells(6)
	local pos = Map.CenterOfCell(location)
	local activated = false

	Trigger.OnEnteredProximityTrigger(pos, range, function(actor, id)
		if activated or actor ~= transport then
			return
		end

		activated = true
		Trigger.RemoveProximityTrigger(id)
		local camera = SpawnRevealCamera(location, -1, type)

		Trigger.OnPassengerExited(transport, function()
			if transport.HasPassengers then
				return
			end

			RemoveActor(camera, hold)
		end)

		Trigger.OnKilled(transport, function()
			RemoveActor(camera, hold)
		end)
	end)
end

--- Call a function once all actors in a group are added to the world.
---@param actors actor[]
---@param action fun()
local function OnAllAddedToWorld(actors, action)
	local i = 0

	Utils.Do(actors, function(actor)
		Trigger.OnAddedToWorld(actor, function()
			i = i + 1

			if i == #actors then
				action()
			end
		end)
	end)
end

--- Is this actor east of the mountain ridge at the center?
---@param actor actor
---@return boolean
local function IsEastOfCenterRidge(actor)
	return actor.Location.X > Waypoint99.Location.X
end

--- Is this actor an available wall type?
---@param actor actor
---@return boolean
local function IsWall(actor)
	return actor.Type == "brik" or actor.Type == "cycl" or actor.Type == "fenc"
end

--- Is this actor mobile and owned by the player?
---@param actor actor
local function IsMobileSoviet(actor)
	return actor.Owner == USSR and actor.HasProperty("Move")
end

--- Is this actor a hostile Cruiser?
---@param actor actor
local function IsCruiser(actor)
	return actor.Owner == Greece and actor.Type == "ca"
end

--- Calculate the squared distance between two points.
---@param posA wpos
---@param posB wpos
---@return integer
local function SquaredDistance(posA, posB)
	local diffX = posA.X - posB.X
	local diffY = posA.Y - posB.Y
	return diffX * diffX + diffY * diffY
end

--- Return a list of targets sorted by least to most distance from a point.
---@param origin wpos Point from which distances are measured.
---@param types string[] Target types to find.
---@param enemyPlayers player[] Players who will have their actors targeted.
---@param filter? fun(actor: actor):boolean Filter applied to actors after type and owner.
---@return actor[]
local function TargetsByDistance(origin, types, enemyPlayers, filter)
	local targets = { }
	local sort = table.sort

	Utils.Do(enemyPlayers, function(player)
		targets = Utils.Concat(targets, player.GetActorsByTypes(types))
	end)

	if filter then
		targets = Utils.Where(targets, filter)
	end

	sort(targets, function(a, b)
		return SquaredDistance(origin, a.CenterPosition) < SquaredDistance(origin, b.CenterPosition)
	end)

	return targets
end

--- Is this actor non-nil, alive, and owned by an non-ally?
---@param target actor
---@param harasser actor
---@return boolean
local function IsValidHarassTarget(target, harasser)
	return target and not target.IsDead and not target.Owner.IsAlliedWith(harasser.Owner)
end

--- Stop a unit's idle harassment once they run out of possible targets.
---@param harasser actor Unit finished with harassment.
---@param onFinished? fun(harasser: actor) Called after OnIdle is clear.
local function ClearIdleHarass(harasser, onFinished)
	Trigger.Clear(harasser, "OnIdle")

	if onFinished then
		-- Delay in case the next function adds another OnIdle.
		Trigger.AfterDelay(1, function()
			if harasser.IsDead then
				return
			end

			onFinished(harasser)
		end)
	end
end

--- Give a list of targets to a unit, and orders for interacting with each.
--- When idle, the unit performs these orders until no valid targets remain.
---@param harasser actor Unit taking orders.
---@param types string[] Target types to find.
---@param enemyPlayers player[] Players who will have their actors targeted.
---@param filter? fun(a: actor):boolean Filter applied to actors after type and owner.
---@param onNewTarget fun(harasser: actor, target: actor) Called when a new target is selected.
---@param onFinished? fun(harasser: actor) Called once no more targets can be found.
local function IdleHarass(harasser, types, enemyPlayers, filter, onNewTarget, onFinished)
	if harasser.IsDead then
		return
	end

	Trigger.OnIdle(harasser, function()
		local tbd = TargetsByDistance(harasser.CenterPosition, types, enemyPlayers, filter)
		-- Cap the list size to limit how outdated it may become.
		local targets = Utils.Take(5, tbd)

		if #targets == 0 then
			ClearIdleHarass(harasser, onFinished)
			return
		end

		Utils.Do(targets, function(target)
			harasser.CallFunc(function()
				-- Target outdated? Assume a list refresh is needed.
				if not IsValidHarassTarget(target, harasser) then
					targets = { }
					return
				end

				onNewTarget(harasser, target)
			end)
		end)
	end)
end

--- Order units to harass when idle, each with their own initial target.
--- The intent is to reduce or stagger TargetsByDistance calls from groups.
---@param harassers actor[] Units taking orders.
---@param types string[] Target types to find.
---@param enemyPlayers player[] Players who will have their actors targeted.
---@param filter? fun(a: actor):boolean Filter applied to actors after type and owner.
---@param onNewTarget fun(harasser: actor, target: actor) Called when a new target is selected.
---@param onFinished? fun(harasser: actor) Called once no more targets can be found.
local function GroupHarass(harassers, types, enemyPlayers, filter, onNewTarget, onFinished)
	local targets
	local targetId = 1

	Utils.Do(harassers, function(harasser)
		IdleHarass(harasser, types, enemyPlayers, filter, onNewTarget, onFinished)

		if not harasser.IsInWorld then
			return
		end

		targets = targets or TargetsByDistance(harasser.CenterPosition, types, enemyPlayers, filter)

		if targets[targetId] then
			onNewTarget(harasser, targets[targetId])
			targetId = targetId + 1
		end
	end)
end

--- Find a new target for a plane, starting with types of highest priority.
---@param plane actor Plane that will attack.
---@param types string[] Enemy types to attack, ordered by high to low priority.
---@param enemyPlayer player Owner of the enemy types.
---@return actor
local function NewPlaneTarget(plane, types, enemyPlayer)
	local enemies = enemyPlayer.GetActorsByTypes(types)
	local target

	Utils.Do(types, function(type)
		if target then
			return
		end

		local matches = Utils.Where(enemies, function(enemy)
			return enemy.Type == type and plane.CanTarget(enemy) end)

		if #matches > 0 then
			target = Utils.Random(matches)
		end
	end)

	return target
end

--- Order a plane to attack enemies when idle.
---@param plane actor Plane that will attack.
---@param types string[] Enemy types to attack, ordered by high to low priority.
---@param enemyPlayer player Owner of the enemy types.
local function IdlePlaneHarass(plane, types, enemyPlayer)
	local target

	Trigger.OnIdle(plane, function()
		local ammoStarved = plane.AmmoCount() == 0 and not plane.Owner.HasPrerequisites({ "afld" })

		if ammoStarved then
			plane.Move(Waypoint64.Location)
			plane.Destroy()
			return
		end

		target = target or NewPlaneTarget(plane, types, enemyPlayer)

		if not target or target.IsDead then
			plane.ReturnToBase()
			target = nil
			return
		end

		-- Ensure vision for targeting.
		SpawnMiscActor("camera.spotter", plane.Owner, target.Location, 3)

		Trigger.AfterDelay(1, function()
			if not target or target.IsDead or plane.IsDead or not plane.CanTarget(target) then
				target = nil
				return
			end

			plane.Attack(target)
		end)
	end)
end

---@param types string[] Plane types to spawn.
---@param targetTypes string[] Enemy types to attack, ordered by high to low priority.
local function SendAlliedPlanes(types, targetTypes)
	if not Greece.HasPrerequisites({ "afld"}) then
		return
	end

	-- The RA1 planes were set to reinforce from Waypoint 95 but because that
	-- sat on a land tile, the edge directly south was (and is) used instead.
	local path = { AlliedPlaneEntry.Location }
	local planes = Reinforcements.Reinforce(Greece, types, path)

	Utils.Do(planes, function(plane)
		IdlePlaneHarass(plane, targetTypes, USSR)
		Trigger.OnCapture(plane, Trigger.ClearAll)
	end)

	if Difficulty ~= "hard" then
		return
	end

	local delay = Actor.BuildTime(planes[1].Type) * 2 * #types

	Trigger.OnAllKilled(planes, function()
		Trigger.AfterDelay(delay, function()
			SendAlliedPlanes(types, targetTypes)
		end)
	end)
end

--- Reinforce a pair of Yak planes and send them to hunt vehicles.
--- The original team was "air1" and they treated harvesters as high priority.
local function SendYaks()
	local priorityList = { "harv", "v2rl", "3tnk", "mnly", "4tnk" }
	SendAlliedPlanes({ "yak", "yak" }, priorityList)
end

--- Reinforce a MiG bomber and send it hunting.
--- The original team was "air2", with vehicles and power plants as targets.
local function SendJet()
	-- Ore Trucks are a lower priority here than they were in RA1 to compensate
	-- for aircraft and MiGs in particular being more effective in OpenRA.
	local priorityList = { "v2rl", "3tnk", "mnly", "4tnk", "harv", "powr", "apwr" }
	SendAlliedPlanes({ "mig" }, priorityList)
end

--- Spawn some Longbows. Each will move to create a spread formation.
local function ReinforceLongbows()
	local types = { "heli", "heli", "heli", "heli" }
	local path = { Waypoint94.Location, Waypoint94.Location + CVec.New(-5, 10) }

	return Reinforcements.Reinforce(Greece, types, path, 20)
end

--- Prepare an area trigger that will dispose of retreating Longbows.
--- Their repulsion trait makes the usual Move + Destroy orders unreliable.
---@param units actor[]
local function PrepareLongbowDisposal(units)
	local disposal = Trigger.OnEnteredProximityTrigger(AlliedHeliDisposal.CenterPosition, WDist.FromCells(2), function(actor)
		if actor.Type ~= "heli" then
			return
		end

		actor.Stop()
		actor.Destroy()
	end)

	Trigger.OnAllRemovedFromWorld(units, function()
		Trigger.RemoveProximityTrigger(disposal)
	end)
end

--- Send a Longbow team that will normally attack at the start base.
--- The original team was "heli1" from trigger "ent1".
local function SendLongbows()
	local targetTypes = { "ftur", "tsla", "apwr", "sam" }
	local longbows = ReinforceLongbows()
	PrepareLongbowDisposal(longbows)

	OnAllAddedToWorld(longbows, function()
		local targets = TargetsByDistance(Waypoint94.CenterPosition, targetTypes, SovietPlayers, IsEastOfCenterRidge)

		-- Ensure vision for targeting.
		SpawnMiscActor("camera", Greece, Waypoint67.Location, DateTime.Seconds(1))

		Utils.Do(longbows, function(longbow)
			if longbow.IsDead then
				return
			end

			longbow.Stop()
			local samFound = false

			Utils.Do(targets, function(target)
				if samFound or target.Type == "sam" then
					samFound = true
					return
				end

				longbow.Attack(target)
			end)

			Trigger.OnIdle(longbow, function()
				longbow.Move(AlliedHeliDisposal.Location)
			end)
		end)
	end)
end

--- Prepare a fire sale once Greece has lost both Cruisers.
local function OnAllCruisersKilled()
	local attackers = Greece.GetGroundAttackers()

	if #attackers == 0 then
		StartFireSale(Greece)
		return
	end

	Utils.Do(attackers, function(a)
		-- Remaining ships should be made obvious. Move them near the Sub Pen.
		if a.Type == "dd" or a.Type == "dd.escort" then
			a.AttackMove(Waypoint52.Location + CVec.New(0, 2), 3)
		end

		IdleHunt(a)
	end)

	Trigger.OnAllKilled(attackers, function()
		StartFireSale(Greece)
	end)
end

--- Send ships that move ahead of Cruisers to search for hostiles.
--- The original team was "sea3" from trigger "des2".
local function SendCruiserEscorts()
	local types = { "dd.escort", "dd.escort" }
	local path = { Waypoint90.Location, Waypoint83.Location }

	Reinforcements.Reinforce(Greece, types, path, DateTime.Seconds(1), function(escort)
		Trigger.OnIdle(escort, function()
			escort.Stance = "AttackAnything"
			local cruisers = Greece.GetActorsByType("ca")

			if #cruisers == 0 then
				escort.Hunt()
				return
			end

			Utils.Do(cruisers, escort.Guard)
		end)
	end)
end

--- Send in the Allies' Cruisers.
--- The original team was "cruis" from trigger "crus".
local function SendCruisers()
	Trigger.AfterDelay(DateTime.Seconds(30), SendCruiserEscorts)
	PlayDelayedSpeech("AlliedForcesApproaching", 20)
	Media.DisplayMessage(UserInterface.GetFluentMessage("unauthorized-naval-units"), BattlefieldControl, USSR.Color)

	Trigger.AfterDelay(DateTime.Seconds(4), function()
		Media.DisplayMessage(UserInterface.GetFluentMessage("protect-our-tech-center-at-all-costs"), BattlefieldControl, USSR.Color)
	end)

	local path =
	{
		Waypoint90.Location,
		Waypoint85.Location,
		Waypoint86.Location,
		Waypoint84.Location,
		Waypoint73.Location,
		Waypoint72.Location,
		Waypoint71.Location
	}

	local cruisers = Reinforcements.Reinforce(Greece, { "ca", "ca" }, path, DateTime.Seconds(2))
	Trigger.OnAllKilled(cruisers, OnAllCruisersKilled)
	SpawnRevealCamera(path[1])

	Utils.Do(cruisers, function(cruiser)
		Trigger.OnIdle(cruiser, function()
			cruiser.Move(Waypoint71.Location, 2)
		end)
	end)

	Trigger.AfterDelay(DateTime.Seconds(6), function()
		CruisersArrived = true
	end)
end

--- Send tanks to guard a path between the refineries and vehicle depot.
--- The original team was "end4" from trigger "des3".
local function SendEastEdgeTanks()
	local path = { Waypoint68.Location, Waypoint69.Location }
	local types = { "2tnk.widescan", "2tnk.widescan", "2tnk.widescan" }
	local passengers = Reinforcements.ReinforceWithTransport(Greece, "lst", types, path, { path[1] })[2]

	Utils.Do(passengers, function(p)
		Trigger.OnAddedToWorld(p, function()
			p.AttackMove(Waypoint63.Location, 2)
		end)
	end)
end

--- Return any walls blocking a path by the naval base, sorted by distance.
---@return actor[]
local function NavalWallTargets()
	local sort = table.sort
	local nw, se = NavalRocketScanWest.CenterPosition, NavalRocketScanEast.CenterPosition
	local walls = Map.ActorsInBox(nw, se, IsWall)

	-- Ensure our target list is ordered from west to east.
	sort(walls, function(a, b)
		return a.Location.X < b.Location.X
	end)

	return walls
end

--- Send a boat of Rocket Soldiers to attack the Soviet naval base.
--- Since these infantry can't outrange Flame Towers like in RA1,
--- they will try to make their way through the walls instead on Hard.
--- The original team was "end1" from trigger "ent4".
local function SendNavalRockets(types, path)
	if not NavalBaseFound then
		-- Cruisers are halfway down the river and the player seems
		-- to be running late. Delay this attack as a small courtesy.
		Trigger.OnObjectiveCompleted(USSR, function(_, id)
			if id ~= PenObjective then
				return
			end

			SendNavalRockets(types, path)
		end)

		return
	end

	local rockets = Reinforcements.ReinforceWithTransport(Greece, "lst", types, path, { path[1] })[2]

	if Difficulty ~= "hard" then
		Utils.Do(rockets, IdleHunt)
		return
	end

	local walls = NavalWallTargets()

	Utils.Do(rockets, function(rocket)
		Trigger.OnAddedToWorld(rocket, function()
			rocket.AttackMove(NavalRocketScanWest.Location, 4)

			Utils.Do(walls, function(wall)
				rocket.Attack(wall)
				rocket.AttackMove(wall.Location, 1)
			end)

			rocket.AttackMove(Waypoint52.Location, 2)
			IdleHunt(rocket)
		end)
	end)
end

--- The Soviet naval base has died, possibly while still in shroud.
--- Under normal circumstances, it should remain unharmed until regained.
local function OnNavalBaseKilled()
	if NavalBaseFound then
		return
	end

	SpawnRevealCamera(Waypoint52.Location, -1)
	AnnounceSovietDefeat(PenObjective, DateTime.Seconds(2), "ObjectiveNotMet", DateTime.Seconds(1))
	Media.DisplayMessage(UserInterface.GetFluentMessage("naval-base-destroyed"), BattlefieldControl, USSR.Color)
	Media.PlaySoundNotification(USSR, "AlertBleep")
end

--- The player's vital tech center has been killed or captured.
local function OnTechCenterLost()
	SpawnRevealCamera(Waypoint89.Location, -1)
	AnnounceSovietDefeat(TechObjective, DateTime.Seconds(2), "ObjectiveNotMet", DateTime.Seconds(1))
	Media.PlaySoundNotification(USSR, "AlertBleep")

	if TechCenter.IsDead then
		Media.DisplayMessage(UserInterface.GetFluentMessage("our-tech-center-destroyed"), BattlefieldControl, USSR.Color)
		return
	end

	Media.DisplayMessage(UserInterface.GetFluentMessage("our-tech-center-captured"), BattlefieldControl, USSR.Color)

	Trigger.AfterDelay(DateTime.Seconds(2), function()
		if TechCenter.IsInWorld then
			TechCenter.Sell()
		end
	end)
end

--- Reassign the actors of a base to the player.
---@param actors actor[]
local function RegainBase(actors)
	Utils.Do(actors, function(actor)
		if actor.IsDead then
			return
		end

		actor.Owner = USSR

		if actor.Type == "harv" then
			actor.FindResources()
		end
	end)
end

--- Reveal part of the river as the player approaches the airbase.
local function RevealAirbaseRiver()
	-- This potential skip is not original but it should be fitting.
	if CruisersArrived then
		return
	end

	SpawnRevealCamera(Waypoint85.Location)
	SpawnRevealCamera(Waypoint86.Location)
end

--- The player has regained refineries and harvesters.
local function OnMineBaseReached()
	local trucks = { MineTruck1, MineTruck2, MineTruck3 }
	local structures = { MineRefinery1, MineRefinery2, MineRefinery3, MinePower1, MinePower2, MinePower3, MinePower4, MineSilo1, MineSilo2, MineSilo3, MineSilo4, MineSilo5, MineSilo6, MineSilo7, MineSilo8 }
	RegainBase(structures)
	RegainBase(trucks)

	Media.DisplayMessage(UserInterface.GetFluentMessage("mining-outpost-regained"), BattlefieldControl, USSR.Color)
	PlayDelayedSpeech("SignalFlareNorth", DateTime.Seconds(4))

	if Difficulty == "hard" then
		Trigger.AfterDelay(DateTime.Seconds(60), SendYaks)
		Trigger.AfterDelay(DateTime.Seconds(120), SendJet)
		return
	end

	if Difficulty == "easy" then
		local proxy = Actor.Create("powerproxy.paratroopers", false, { Owner = USSR })
		local plane = proxy.TargetParatroopers(Waypoint55.CenterPosition, Angle.New(896))[1]

		Trigger.OnPassengerExited(plane, function()
			Trigger.Clear(plane, "OnPassengerExited")
			Media.PlaySpeechNotification(USSR, "ReinforcementsArrived")
			RemoveActor(proxy)
		end)
	end
end

--- The player has regained the War Factory and Service Depot.
local function OnServiceBaseReached()
	local base = { ServiceDepot, ServiceFactory, ServiceFlame1, ServiceFlame2, ServiceFlame3, ServiceFlame4, ServicePower1, ServicePower2 }
	RegainBase(base)

	Media.DisplayMessage(UserInterface.GetFluentMessage("vehicle-depot-regained"), BattlefieldControl, USSR.Color)
	PlayDelayedSpeech("SignalFlareWest", DateTime.Seconds(4))
	SpawnRevealCamera(Waypoint1.Location)

	Trigger.AfterDelay(CruiserDelays[Difficulty], SendCruisers)
	Trigger.AfterDelay(LongbowDelays[Difficulty], SendLongbows)

	Trigger.AfterDelay(DateTime.Seconds(20), function()
		StartCountdown(CruiserDelays[Difficulty] - DateTime.Seconds(20))
	end)

	-- On Hard, planes will already be triggered from the mining base.
	if Difficulty == "hard" then
		return
	end

	Trigger.AfterDelay(DateTime.Seconds(60), SendYaks)
	Trigger.AfterDelay(DateTime.Seconds(120), SendJet)

	if Difficulty == "easy" and not ServiceFactory.IsDead then
		local tankType = "4tnk"
		ServiceFactory.Produce(tankType)

		Trigger.AfterDelay(1, function()
			RegainBase(BadGuy.GetActorsByType(tankType))
		end)
	end
end

--- The player has reached the hostile airbase.
local function OnAirbaseReached()
	SpawnRevealCamera(Waypoint2.Location)

	if NavalBaseFound then
		return
	end

	Trigger.AfterDelay(DateTime.Seconds(4), function()
		Media.DisplayMessage(UserInterface.GetFluentMessage("airfields-regained"), BattlefieldControl, USSR.Color)
		Media.PlaySpeechNotification(USSR, "SignalFlareSouth")
	end)
end

--- The player has regained the Sub Pen, by approaching the
--- gate or otherwise discovering the production structures.
local function OnNavalBaseFound()
	if VictoryStarted or NavalBaseFound then
		return
	end

	NavalBaseFound = true
	USSR.MarkCompletedObjective(PenObjective)
	Media.PlaySpeechNotification(USSR, "NewOptions")
	local structures = { NavalPower1, NavalPower2, NavalFlame1, NavalFlame2, NavalConstructionYard, NavalPen }
	RegainBase(structures)
	SpawnRevealCamera(Waypoint52.Location)

	Trigger.AfterDelay(DateTime.Seconds(4), function()
		IntruderObjective = AddPrimaryObjective(USSR, "wipe-out-intruders-with-new-submarines")
	end)
end

--- Prepare the chain of Soviet signal flares and their triggers.
--- Each trigger spawns the next flare and schedules the current one's removal.
local function PrepareSovietSignals()
	local signals =
	{
		{
			action = OnMineBaseReached,
			location = Waypoint0.Location,
			radius = WDist.FromCells(3)
		},
		{
			action = OnServiceBaseReached,
			location = Waypoint1.Location,
			radius = WDist.FromCells(11)
		},
		{
			action = OnAirbaseReached,
			location = Waypoint2.Location,
			radius = WDist.FromCells(6)
		},
		{
			location = Waypoint3.Location + CVec.New(0, 2),
			radius = WDist.FromCells(3)
		}
	}

	for i = 1, #signals do
		local activated = false

		Trigger.OnEnteredProximityTrigger(Map.CenterOfCell(signals[i].location), signals[i].radius, function(actor, id)
			if activated or not IsMobileSoviet(actor) then
				return
			end

			activated = true
			Trigger.RemoveProximityTrigger(id)
			RemoveActor(signals[i].smoke, DateTime.Minutes(2))

			if signals[i].action then
				signals[i].action()
			end

			if signals[i + 1] and not NavalBaseFound then
				signals[i + 1].smoke = SpawnSignalFlare(signals[i + 1].location)
			end
		end)
	end

	-- Wait for the startup messages to expire.
	Trigger.AfterDelay(DateTime.Seconds(12), function()
		Media.DisplayMessage(UserInterface.GetFluentMessage("scouts-mark-the-way-to-bases"), BattlefieldControl, USSR.Color)
	end)

	Trigger.AfterDelay(DateTime.Seconds(18), function()
		signals[1].smoke = SpawnSignalFlare(signals[1].location)
		Media.PlaySpeechNotification(USSR, "SignalFlare")
	end)
end

--- Send some engineers to capture the tech center if playing Hard.
--- This is a nod to the "end3" team unused in the RA1 mission.
local function SendEngineers()
	if Difficulty ~= "hard" then
		return
	end

	local cargoTypes = { "e6", "e6", "e1", "e3", "e3" }
	local entryPath = { Waypoint66.Location, CPos.New(56, 92), Waypoint87.Location }
	local exitPath = { Waypoint66.Location, EngineerTransportExit.Location }
	local apc = Reinforcements.ReinforceWithTransport(Greece, "apc", cargoTypes, entryPath, exitPath)[1]
	local captureTypes = { "stek", "iron" }

	Utils.Do(apc.Passengers, function(passenger)
		Trigger.OnAddedToWorld(passenger, function()
			if not passenger.HasProperty("Capture") then
				IdleHunt(passenger)
				return
			end

			passenger.Move(Waypoint89.Location, 2)

			IdleHarass(passenger, captureTypes, SovietPlayers, nil, function(harasser, target)
				harasser.Capture(target)
			end)
		end)
	end)
end

--- Order the first attack on the starting base.
local function OrderStartAttackers()
	SendEngineers()
	local attackers = { StartAttacker1, StartAttacker2, StartAttacker3 }

	Utils.Do(attackers, function(a)
		-- Maintain their spread formation as they approach.
		local goal = a.Location + CVec.New(0, 6)
		a.AttackMove(goal, 2)
		IdleHunt(a)
	end)
end

--- Play one of Tanya's sounds after a delay if she still lives.
---@param actor actor Actor that must survive for the speech to play.
---@param sound string Sound notification to play.
---@param delay integer Ticks until the speech plays.
---@param camera? string Camera type to reveal Tanya as she speaks.
local function TauntWithTanya(actor, sound, delay, camera)
	Trigger.AfterDelay(delay, function()
		if actor.IsDead then
			return
		end

		if camera then
			SpawnRevealCamera(actor.Location, DateTime.Seconds(3), camera)
		end

		Media.PlaySoundNotification(USSR, sound)
	end)
end

--- Arrange extraction for Tanya once her sabotage is complete.
---@param tanya actor
local function ExtractTanya(tanya)
	local path = { Waypoint93.Location, TanyaBackupUnload.Location }
	local boat = Reinforcements.Reinforce(Greece, { "lst" }, path)[1]
	tanya.Move(TanyaBackupUnload.Location)

	Trigger.OnIdle(boat, function()
		if tanya.IsDead or boat.HasPassengers then
			boat.Move(path[1])
			boat.Destroy()
			return
		end

		if tanya.IsIdle then
			tanya.EnterTransport(boat)
		end
	end)

	Trigger.OnPassengerEntered(boat, function()
		TauntWithTanya(boat, "TanyaLetsRock", 15, "camera.small")
	end)

	Trigger.OnIdle(tanya, function()
		if boat.IsDead then
			tanya.Hunt()
		end
	end)
end

-- Reinforce Tanya's backup team by boat.
local function ReinforceTanyaBackup()
	local path = { Waypoint68.Location, TanyaBackupUnload.Location }
	local exit = { Waypoint94.Location }
	local types = { "e3.vehiclehunter", "e3.vehiclehunter", "e3.vehiclehunter", "e1", "2tnk.widescan" }
	local boatTeam = Reinforcements.ReinforceWithTransport(Greece, "lst", types, path, exit)
	local boat, cargo = boatTeam[1], boatTeam[2]
	PrepareTransportReveal(boat, path[#path], DateTime.Seconds(1), "camera.small")

	return cargo
end

--- Send Tanya's backup team if Hard, and order them to defend near the flare.
local function SendTanyaBackup()
	if Difficulty ~= "hard" then
		return
	end

	local units = ReinforceTanyaBackup()
	local attacking = false
	local guardGoal = Waypoint55.Location + CVec.New(0, 2)

	OnAnyDamaged(units, function()
		attacking = true
	end)

	Utils.Do(units, function(unit)
		Trigger.OnAddedToWorld(unit, function()
			-- Ignore structures for now and leave them to Tanya.
			unit.Stance = "Defend"
			unit.AttackMove(guardGoal, 2)
		end)

		Trigger.OnIdle(unit, function()
			if not attacking then
				return
			end

			if unit.Location ~= Waypoint0.Location then
				unit.AttackMove(Waypoint0.Location)
				return
			end

			unit.Stance = "AttackAnything"
			unit.Hunt()
		end)
	end)
end

--- Reinforce Tanya by boat, along with any backup she may have.
---@return actor
local function ReinforceTanya()
	local path = { Waypoint93.Location, Waypoint92.Location }
	local cargo = { "e7" }
	local boatTeam = Reinforcements.ReinforceWithTransport(Greece, "lst", cargo, path, { path[1] })
	local boat, tanya = boatTeam[1], boatTeam[2][1]
	PrepareTransportReveal(boat, path[#path], DateTime.Seconds(3), "camera", WDist.FromCells(4))
	Trigger.OnAddedToWorld(tanya, SendTanyaBackup)

	return tanya
end

--- Begin Tanya's demolition spree. She will attempt extraction when done.
local function SendTanya()
	if TanyaReinforced then
		return
	end

	TanyaReinforced = true
	local tanya = ReinforceTanya()
	local demolitionTypes = { "proc", "powr", "silo" }
	local lastTarget

	local onNewTarget = function(demolisher, target)
		-- Guard against a re-demolish, which may happen if the target
		-- list refreshes as a bomb timer is still ticking down.
		if target == lastTarget then
			return
		end

		demolisher.Demolish(target)
		lastTarget = target
	end

	Trigger.OnAddedToWorld(tanya, function()
		TauntWithTanya(tanya, "TanyaLaugh", 1)
		TauntWithTanya(tanya, "TanyaGiveItToMe", DateTime.Seconds(6))
		tanya.Move(Waypoint77.Location)
		tanya.Move(TanyaDemolishStart.Location)
		IdleHarass(tanya, demolitionTypes, SovietPlayers, IsEastOfCenterRidge, onNewTarget, ExtractTanya)
	end)

	-- Tanya's unload cell from the boat is randomized. Since that affects
	-- her travel time, this will be more consistent than a delay.
	Trigger.OnEnteredProximityTrigger(TanyaDemolishStart.CenterPosition, WDist.New(1536), function(actor, id)
		if actor ~= tanya then
			return
		end

		Trigger.RemoveProximityTrigger(id)
		TauntWithTanya(actor, "TanyaKissItByeBye", 1, "camera.small")
	end)
end

--- Airlift thieves to the mining base and order them to steal.
--- The original team was "air3" from trigger "ent3".
local function SendThieves()
	local thiefTypes = { "thf", "thf" }
	if Difficulty == "hard" then
		thiefTypes = { "thf", "thf", "thf", "thf" }
	end

	local path = { Waypoint68.Location, Waypoint55.Location }
	local thieves = Reinforcements.ReinforceWithTransport(Greece, "tran", thiefTypes, path, { path[1] })[2]
	local stealTypes = { "proc", "silo" }

	local onNewTarget = function(thief, target)
		thief.Infiltrate(target)
	end

	OnAllAddedToWorld(thieves, function()
		GroupHarass(thieves, stealTypes, SovietPlayers, IsEastOfCenterRidge, onNewTarget)
	end)
end

--- Send vehicles to help the beach runner, and prepare Tanya's arrival.
--- The original team was "sea4" from trigger "atk2".
local function SendBeachRescue()
	local path = { Waypoint94.Location, Waypoint95.Location }
	local cargoTypes = { "1tnk", "arty", "arty" }
	if Difficulty == "hard" then
		cargoTypes = { "2tnk", "2tnk", "e3", "e3", "arty" }
	end

	local boat, cargo = ReinforceBasicBoatTeam(cargoTypes, path)
	PrepareTransportReveal(boat, Waypoint74.Location, DateTime.Seconds(1), "camera.small", WDist.FromCells(15))

	Trigger.OnAllKilled(cargo, function()
		SendTanya()
		IdleHunt(BeachRunner)
	end)
end

--- Order a soldier to run for help when Soviets approach.
--- The original team was "inf1" from trigger "atk6".
local function OrderBeachRunner()
	if BeachRunner.IsDead then
		return
	end

	SpawnRevealCamera(BeachRunner.Location)
	BeachRunner.Stop()
	BeachRunner.Move(RidgeRunPoint1.Location)
	BeachRunner.Move(RidgeRunPoint2.Location)
	BeachRunner.Move(Waypoint74.Location)

	Trigger.OnIdle(BeachRunner, function()
		if BeachRunner.Location == Waypoint74.Location then
			Trigger.Clear(BeachRunner, "OnIdle")
			BeachRunner.Stance = "AttackAnything"
			return
		end

		BeachRunner.Move(Waypoint74.Location)
	end)
end

--- Set up triggers for the beach runner.
local function PrepareBeachRunner()
	local team = { BeachRunnerRocket1, BeachRunnerRocket2, BeachRunner }
	local alerted = false

	Trigger.OnEnteredFootprint(Footprints.atk6, function(actor, id)
		if alerted or not IsMobileSoviet(actor) then
			return
		end

		alerted = true
		OrderBeachRunner()
		Trigger.RemoveFootprintTrigger(id)
	end)

	OnAnyDamaged(team, function()
		if alerted then
			return
		end

		alerted = true
		OrderBeachRunner()
	end)

	Trigger.OnEnteredFootprint(Footprints.atk2, function(actor, id)
		if actor ~= BeachRunner then
			return
		end

		Trigger.RemoveFootprintTrigger(id)
		SendBeachRescue()
	end)
end

--- Send some APC troops for a flank attack at the northern edge.
--- A possibility of instead attacking the Soviet vehicle base is preserved.
--- The original team was "air4" from trigger "ent3".
local function SendLakeAmbushers()
	local path = { Waypoint57.Location, Waypoint56.Location }
	local cargoTypes = { "e3", "e3", "e3", "e3", "e3" }
	local apc = Reinforcements.ReinforceWithTransport(Greece, "apc", cargoTypes, path)[1]

	Trigger.OnPassengerExited(apc, function(_, passenger)
		IdleHunt(passenger)
	end)

	IdleHunt(apc)
end

--- Unload some rocket soldiers at a beach near the ore mining base.
--- The original team was "sea5" from trigger "atk8".
--- Hard adds the intended but bugged "sea6" team, plus an Artillery unit.
local function SendEastEdgeRockets()
	local path = { Waypoint68.Location, Waypoint70.Location }
	local cargoTypes = { "e3.vehiclehunter", "e3.vehiclehunter", "e3.vehiclehunter", "e3.vehiclehunter", "e3.vehiclehunter" }
	local boat = ReinforceBasicBoatTeam(cargoTypes, path)
	PrepareTransportReveal(boat, Waypoint70.Location, DateTime.Seconds(1), "camera.small")

	if Difficulty ~= "hard" then
		return
	end

	local extras = { "e3.vehiclehunter", "e3.vehiclehunter", "e3.vehiclehunter", "arty" }
	local extraPath = { Waypoint68.Location, Waypoint69.Location }

	Trigger.AfterDelay(10, function()
		ReinforceBasicBoatTeam(extras, extraPath)
	end)
end

--- Prepare some simple triggers activated by player units.
local function PrepareBasicFootprints()
	local triggers =
	{
		{
			cells = Footprints.atk7,
			actions = { SendEastEdgeRockets }
		},
		{
			cells = Footprints.des1,
			actions = { RevealAirbaseRiver }
		},
		{
			cells = Footprints.des3,
			actions = { SendEastEdgeTanks }
		},
		{
			cells = Footprints.ent3,
			actions = { SendLakeAmbushers, SendThieves }
		},
		{
			cells = Footprints.tanya,
			actions = { SendTanya }
		}
	}

	Utils.Do(triggers, function(trigger)
		local activated = false

		Trigger.OnEnteredFootprint(trigger.cells, function(actor, id)
			if activated or not IsMobileSoviet(actor) then
				return
			end

			activated = true
			Trigger.RemoveFootprintTrigger(id)

			Utils.Do(trigger.actions, function(action)
				action()
			end)
		end)
	end)
end

--- Prepare reveals and base attacks that trigger as Cruisers travel the river.
local function PrepareCruiserFootprints()
	local triggers =
	{
		{
			cells = Footprints.rev4,
			reveal = Waypoint84.Location
		},
		{
			cells = Footprints.rev3,
			reveal = Waypoint73.Location
		},
		{
			cells = Footprints.ent2,
			teams = { EndTeams.sea7, EndTeams.sea8 }
		},
		{
			cells = Footprints.ent4,
			reveal = Waypoint76.Location,
			teams = { EndTeams.end1 },
			teamFunc = SendNavalRockets
		},
		{
			cells = Footprints.ent5,
			reveal = Waypoint53.Location,
			teams = { EndTeams.end2, EndTeams.end5 }
		},
		{
			cells = Footprints.ent6,
			reveal = Waypoint72.Location,
		}
	}

	Utils.Do(triggers, function(trigger)
		local activated = false

		Trigger.OnEnteredFootprint(trigger.cells, function(actor, id)
			if activated or not IsCruiser(actor) then
				return
			end

			activated = true
			Trigger.RemoveFootprintTrigger(id)

			if trigger.reveal then
				SpawnRevealCamera(trigger.reveal)
			end

			if not trigger.teams then
				return
			end

			local teamFunc = trigger.teamFunc or ReinforceBasicBoatTeam

			Utils.Do(trigger.teams, function(team)
				teamFunc(team.types, team.path)
			end)
		end)
	end)
end

--- Swap out the end teams' artillery if playing on Easy.
--- Tesla Coils outrange Allied artillery with ease in RA1,
--- but that's not the case here and a difficulty spike may result.
local function SwapEndArtillery()
	if Difficulty ~= "easy" then
		return
	end

	Utils.Do(EndTeams, function(team)
		for i = 1, #team.types do
			if team.types[i] == "arty" then
				team.types[i] = "1tnk"
			end
		end
	end)
end

WorldLoaded = function()
	PrepareSovietSignals()
	PrepareCruiserFootprints()
	PrepareBasicFootprints()
	PrepareBeachRunner()
	OrderStartAttackers()
	SwapEndArtillery()

	Camera.Position = TechCoil1.CenterPosition
	USSR.Cash = 500
	BadGuy.Cash = 5000

	InitObjectives(USSR)
	PenObjective = AddPrimaryObjective(USSR, "regain-control-of-our-naval-base")
	TechObjective = AddPrimaryObjective(USSR, "tech-center-survive")

	Trigger.OnKilledOrCaptured(TechCenter, OnTechCenterLost)
	Trigger.OnAllKilled({ NavalConstructionYard, NavalPen }, OnNavalBaseKilled)
	Trigger.OnDiscovered(NavalConstructionYard, OnNavalBaseFound)
	Trigger.OnDiscovered(NavalPen, OnNavalBaseFound)

	-- Fallback trigger that lets the player continue if using the
	-- visibility cheat; OnDiscovered events will be blocked.
	Trigger.OnEnteredProximityTrigger(NavalConstructionYard.CenterPosition, WDist.FromCells(4), function(actor, id)
		if not IsMobileSoviet(actor) then
			return
		end

		Trigger.RemoveProximityTrigger(id)
		OnNavalBaseFound()
	end)

	Trigger.AfterDelay(DateTime.Seconds(6), function()
		-- Ownership is delayed to help avoid the speech muddling others.
		RegainBase({ IronCurtain })
	end)
end

Tick = function()
	if CruisersArrived and TechCenter.IsInWorld and Greece.HasNoRequiredUnits() then
		AnnounceSovietVictory()
	end
end
