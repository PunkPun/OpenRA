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
					creepEnemiesNode.Value.Value = string.Join(",",
						creepEnemiesNode.Value.Value.Split(',')
						.Where(x => !x.Contains(pNameNode.Value.Value)));
					continue;
				}

				// custom settings for player
				pl.Value.Nodes.Find(x => x.Key == "Faction").Value.Value = player.Faction.Name.ToLower();
				if (player.SpawnPoint != 0)
				{
					OverwriteNode(pl.Value.Nodes, "LockFaction", "True");
					OverwriteNode(pl.Value.Nodes, "LockSpawn", "True");
					OverwriteNode(pl.Value.Nodes, "LockTeam", "True");
					OverwriteNode(pl.Value.Nodes, "Team", player.PlayerReference.Team.ToString());
					OverwriteNode(pl.Value.Nodes, "Spawn", player.SpawnPoint.ToString());
					occupiedSpawns.Add(player.SpawnPoint);
				}
			}

			var actorDefinitions = new List<MiniYamlNode>();
			var spawnId = 0; // for removing unused spawns
			var lastActorId = 0; // to track actor internal id
			foreach (var a in actors)
			{
				// remove unused spawns
				if (a.Info.Name == "mpspawn")
				{
					spawnId++;
					if (!occupiedSpawns.Any(x => x == spawnId))
					{
						continue;
					}
				}

				var actor = new MiniYamlNode("Actor" + a.ActorID, a.Info.Name == "proc" ? "proc.noharv" : a.Info.Name);
				actorDefinitions.Add(actor);

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

			var ruleDefinitions = map.RuleDefinitions ?? new MiniYaml("");

			// world actor settings
			OverwriteNode(ruleDefinitions.Nodes, "World", "", new List<MiniYamlNode>()
			{
				new MiniYamlNode("-SpawnMPUnits", "")
			});

			// player actor settings
			OverwriteNode(ruleDefinitions.Nodes, "Player", "", new List<MiniYamlNode>()
			{
				new MiniYamlNode("PlayerResources", "", new List<MiniYamlNode>()
				{
					new MiniYamlNode("SelectableCash", "0"),
					new MiniYamlNode("DefaultCash", "0"),
					new MiniYamlNode("DefaultCashDropdownLocked", "True")
				})
			});

			// add a definition for a refinery without a free harvester
			OverwriteNode(ruleDefinitions.Nodes, "PROC.NoHarv", "", new List<MiniYamlNode>()
				{
					new MiniYamlNode("Inherits", "PROC"),
					new MiniYamlNode("-FreeActor", ""),
					new MiniYamlNode("RenderSprites", "", new List<MiniYamlNode>()
					{
						new MiniYamlNode("Image", "PROC")
					})
				});

			// adds actors that give cash to players
			foreach (var pl in world.Players)
			{
				if (pl.SpawnPoint == 0)
					continue;

				// to rules definitions
				OverwriteNode(ruleDefinitions.Nodes, "cash" + pl.InternalName.ToLower(), "", new List<MiniYamlNode>()
				{
					new MiniYamlNode("Immobile", ""),
					new MiniYamlNode("KillsSelf", ""),
					new MiniYamlNode("CashTrickler", "", new List<MiniYamlNode>()
					{
						new MiniYamlNode("Amount", pl.PlayerActor.TraitOrDefault<PlayerResources>().Cash.ToString()),
						new MiniYamlNode("ShowTicks", "False")
					})
				});

				// to actors definitions (doesn't need to overwrite)
				lastActorId++;
				OverwriteNode(actorDefinitions, "Actor" + lastActorId, "Cash" + pl.InternalName, new List<MiniYamlNode>()
				{
					new MiniYamlNode("Owner", pl.InternalName),
					new MiniYamlNode("Location", "0,0")
				});
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

		MiniYamlNode OverwriteNode(List<MiniYamlNode> list, string key, string value)
		{
			return OverwriteNode(list, key, value, new List<MiniYamlNode>());
		}

		MiniYamlNode OverwriteNode(List<MiniYamlNode> list, string key, string value, List<MiniYamlNode> values)
		{
			Console.WriteLine(key);
			var node = list.Find(x => x.Key == key);
			if (node is null)
			{
				var tnode = new MiniYamlNode(key, value, values);
				list.Add(tnode);
				Console.WriteLine(1);
				return tnode;
			}
			Console.WriteLine(2);
			foreach (var n in values)
			{
				OverwriteNode(node.Value.Nodes, n.Key, n.Value.Value);
			}
			Console.WriteLine(3);
			node.Value.Value = value;
			Console.WriteLine(4);
			return node;
		}
	}
}
