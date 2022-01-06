#region Copyright & License Information
/*
 * Copyright 2007-2020 The OpenRA Developers (see AUTHORS)
 * This file is part of OpenRA, which is free software. It is made
 * available to you under the terms of the GNU General Public License
 * as published by the Free Software Foundation, either version 3 of
 * the License, or (at your option) any later version. For more
 * information, see COPYING.
 */
#endregion

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OpenRA.FileSystem;
using OpenRA.Graphics;
using OpenRA.Mods.Common.Lint;
using OpenRA.Mods.Common.Traits;
using OpenRA.Traits;
using OpenRA.Widgets;

namespace OpenRA.Mods.Common.Widgets.Logic.Ingame
{
	[ChromeLogicArgsHotkeys("SaveGamestate")]
	public class SaveGamestateLogic : SingleHotkeyBaseLogic
	{
		readonly World world;
		public static Map CurrentMap;

		[ObjectCreator.UseCtor]
		public SaveGamestateLogic(Widget widget, ModData modData, WorldRenderer worldRenderer, World world, Dictionary<string, MiniYaml> logicArgs)
			: base(widget, modData, "SaveGamestateKey", "WORLD_KEYHANDLER", logicArgs)
		{
			this.world = world;
		}

		protected override bool OnHotkeyActivated(KeyInput e)
		{
			var actors = world.Actors
				.Where(a => a.IsInWorld && a.OccupiesSpace != null)
			 	.ToList();

			if (!actors.Any())
				return true;

			var map = world.Map.Copy();

			var playerDefinitions = new List<MiniYamlNode>(map.PlayerDefinitions);
			var occupiedSpawns = new List<int>();
			var creepEnemiesNode = map.PlayerDefinitions.Find(x => x.Value.Nodes.Any(y => y.Key == "Name" && y.Value.Value == "Creeps"))
				.Value.Nodes.Find(x => x.Key == "Enemies");

			// edit player definitions
			foreach (var pl in playerDefinitions)
			{
				var pNameNode = pl.Value.Nodes.Find(x => x.Key == "Name");
				var player = Array.Find(world.Players, x => x.InternalName == pNameNode.Value.Value);

				// remove unused players if there were more spawns than players
				if (player is null)
				{
					map.PlayerDefinitions.Remove(pl);
					creepEnemiesNode.Value.Value = String.Join(",",
						creepEnemiesNode.Value.Value.Split(',')
						.Where(x => !x.Contains(pNameNode.Value.Value)));
					continue;
				}

				// custom settings for player
				pl.Value.Nodes.Find(x => x.Key == "Faction").Value.Value = player.Faction.Name.ToLower();
				if (player.SpawnPoint != 0)
				{
					pl.Value.Nodes.Add(new MiniYamlNode("LockFaction", "True"));
					pl.Value.Nodes.Add(new MiniYamlNode("LockSpawn", "True"));
					pl.Value.Nodes.Add(new MiniYamlNode("LockTeam", "True"));
					pl.Value.Nodes.Add(new MiniYamlNode("Team", player.PlayerReference.Team.ToString()));
					pl.Value.Nodes.Add(new MiniYamlNode("Spawn", player.SpawnPoint.ToString()));
					occupiedSpawns.Add(player.SpawnPoint);
				}
			}

			var actorDefinitions = new List<MiniYamlNode>();
			var spawnId = 0; // for removing unused spawns
			var lastActorId = 0; // to track actor internal id
			foreach (var a in actors)
			{
				var actor = new MiniYamlNode("Actor" + a.ActorID, a.Info.Name);
				actorDefinitions.Add(actor);

				// remove unused spawns
				if (a.Info.Name == "mpspawn")
				{
					spawnId++;
					if (!occupiedSpawns.Any(x => x == spawnId))
					{
						continue;
					}
				}

				actor.Value.Nodes.Add(new MiniYamlNode("Owner", a.Owner.InternalName.ToString()));

				// if (a.TraitOrDefault<FactionInfo>() != null)
				// 	actor.Value.Nodes.Add(new MiniYamlNode("Faction", a.Trait<FactionInfo>().Name.ToString()));
				actor.Value.Nodes.Add(new MiniYamlNode("Location", a.Location.X + "," + a.Location.Y));
				var health = a.TraitOrDefault<Health>();
				if (health != null)
				{
					var hp = health.HP * 100 / health.MaxHP;
					if (hp != 100)
						actor.Value.Nodes.Add(new MiniYamlNode("Health", hp.ToString()));
				}

				var mobile = a.TraitOrDefault<Mobile>();
				if (mobile != null && mobile.ToSubCell != SubCell.FullCell)
					actor.Value.Nodes.Add(new MiniYamlNode("SubCell", ((int)mobile.ToSubCell).ToString()));

				var facing = a.TraitOrDefault<IFacing>();
				if (facing != null)
					actor.Value.Nodes.Add(new MiniYamlNode("Facing", facing.Facing.ToString()));

				lastActorId = (int)a.ActorID;
			}

			// create rules if they are missing
			var ruleDefinitions = map.RuleDefinitions;
			if (ruleDefinitions is null)
			{
				ruleDefinitions = new MiniYaml("");
			}

			// create world actor if missing
			var worldActor = ruleDefinitions.Nodes.Find(x => x.Key == "World");
			if (worldActor is null)
			{
				var temp = new MiniYamlNode("World", "");
				ruleDefinitions.Nodes.Add(temp);
				worldActor = temp;
			}

			// worldActor.Value.Nodes.Add(new MiniYamlNode("MPStartLocations", "",  new List<MiniYamlNode>()
			// {
			// 	new MiniYamlNode("SeparateTeamSpawnsCheckboxVisible", "False")
			// }));
			// worldActor.Value.Nodes.Add(new MiniYamlNode("-MPStartLocations", ""));
			worldActor.Value.Nodes.Add(new MiniYamlNode("-SpawnMPUnits", ""));

			// create player actor if missing
			var playerActor = ruleDefinitions.Nodes.Find(x => x.Key == "Player");
			if (playerActor is null)
			{
				var temp = new MiniYamlNode("Player", "");
				ruleDefinitions.Nodes.Add(temp);
				playerActor = temp;
			}

			playerActor.Value.Nodes.Add(new MiniYamlNode("PlayerResources", "", new List<MiniYamlNode>()
			{
				new MiniYamlNode("SelectableCash", "0"),
				new MiniYamlNode("DefaultCash", "0"),
				new MiniYamlNode("DefaultCashDropdownLocked", "True")
			}));

			// adds actors that give cash to players
			foreach (var pl in world.Players)
			{
				if (pl.SpawnPoint == 0)
					continue;

				// to rules definitions
				var actor = new MiniYamlNode("cash" + pl.InternalName.ToLower(), "");
				ruleDefinitions.Nodes.Add(actor);

				actor.Value.Nodes.Add(new MiniYamlNode("Immobile", ""));
				actor.Value.Nodes.Add(new MiniYamlNode("KillsSelf", ""));
				actor.Value.Nodes.Add(new MiniYamlNode("CashTrickler", "", new List<MiniYamlNode>()
				{
				new MiniYamlNode("Amount", pl.PlayerActor.TraitOrDefault<PlayerResources>().Cash.ToString()),
				new MiniYamlNode("ShowTicks", "False")
				}));

				// to actors definitions
				var mapActor = new MiniYamlNode("Actor" + lastActorId, "Cash" + pl.InternalName);
				actorDefinitions.Add(mapActor);

				mapActor.Value.Nodes.Add(new MiniYamlNode("Owner", pl.InternalName));
				mapActor.Value.Nodes.Add(new MiniYamlNode("Location", "0,0"));
				lastActorId++;
			}

			map.ActorDefinitions = actorDefinitions;
			map.RuleDefinitions = ruleDefinitions;

			map.Title += "-save";

			var combinedPath = Platform.ResolvePath(Path.Combine(Path.GetDirectoryName(map.Package.Name), map.Title + ".oramap"));
			var package = map.Package as IReadWritePackage;

			new Folder(Path.GetDirectoryName(map.Package.Name)).Delete(combinedPath); // why not
			package = ZipFileLoader.Create(combinedPath);
			map.Save(package);

			return true;
		}
	}
}
