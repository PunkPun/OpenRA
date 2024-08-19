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
using System.Linq;
using System.Threading.Tasks;
using OpenRA.FileSystem;
using OpenRA.GameRules;
using OpenRA.Traits;

namespace OpenRA
{
	public class Ruleset
	{
		public readonly ActorInfoDictionary Actors;
		public readonly IReadOnlyDictionary<string, WeaponInfo> Weapons;
		public readonly IReadOnlyDictionary<string, SoundInfo> Voices;
		public readonly IReadOnlyDictionary<string, SoundInfo> Notifications;
		public readonly IReadOnlyDictionary<string, MusicInfo> Music;
		public readonly ITerrainInfo TerrainInfo;
		public readonly IReadOnlyDictionary<string, MiniYamlNode> ModelSequences;
		public Dictionary<string, MiniYamlNode> UnresolvedRulesYamlDict;
		public List<MiniYamlNode> ResolvedRulesYaml;
		public Dictionary<string, MiniYamlNode> UnresolvedWeaponsYamlDict;
		public List<MiniYamlNode> ResolvedWeaponsYaml;

		public Ruleset(
			IReadOnlyDictionary<string, ActorInfo> actors,
			IReadOnlyDictionary<string, WeaponInfo> weapons,
			IReadOnlyDictionary<string, SoundInfo> voices,
			IReadOnlyDictionary<string, SoundInfo> notifications,
			IReadOnlyDictionary<string, MusicInfo> music,
			ITerrainInfo terrainInfo,
			IReadOnlyDictionary<string, MiniYamlNode> modelSequences,
			Dictionary<string, MiniYamlNode> unresolvedRulesYamlDict,
			List<MiniYamlNode> resolvedRulesYaml,
			Dictionary<string, MiniYamlNode> unresolvedWeaponsYamlDict,
			List<MiniYamlNode> resolvedWeaponsYaml)
		{
			Actors = new ActorInfoDictionary(actors);
			Weapons = weapons;
			Voices = voices;
			Notifications = notifications;
			Music = music;
			TerrainInfo = terrainInfo;
			ModelSequences = modelSequences;
			UnresolvedRulesYamlDict = unresolvedRulesYamlDict;
			ResolvedRulesYaml = resolvedRulesYaml;
			UnresolvedWeaponsYamlDict = unresolvedWeaponsYamlDict;
			ResolvedWeaponsYaml = resolvedWeaponsYaml;

			RulesetLoadedActors(Actors.Values);
			RulesetLoadedWeapons(Weapons.Values);
		}

		public Ruleset(
			IReadOnlyDictionary<string, ActorInfo> actors,
			IReadOnlyDictionary<string, WeaponInfo> weapons,
			IReadOnlyDictionary<string, SoundInfo> voices,
			IReadOnlyDictionary<string, SoundInfo> notifications,
			IReadOnlyDictionary<string, MusicInfo> music,
			ITerrainInfo terrainInfo,
			IReadOnlyDictionary<string, MiniYamlNode> modelSequences)
		: this(actors, weapons, voices, notifications, music, terrainInfo, modelSequences, null, null, null, null) { }

		public IEnumerable<KeyValuePair<string, MusicInfo>> InstalledMusic { get { return Music.Where(m => m.Value.Exists); } }

		static IReadOnlyDictionary<string, T> MergeOrDefault<T>(string name,
			IReadOnlyFileSystem fileSystem,
			IEnumerable<string> files,
			MiniYaml additional,
			IReadOnlyDictionary<string, T> defaults,
			Func<MiniYamlNode, T> makeObject,
			Func<MiniYamlNode, bool> filterNode = null)
		{
			if (additional == null && defaults != null)
				return defaults;

			IEnumerable<MiniYamlNode> yamlNodes = MiniYaml.Load(fileSystem, files, additional);

			// Optionally, the caller can filter out elements from the loaded set of nodes. Default behavior is unfiltered.
			if (filterNode != null)
				yamlNodes = yamlNodes.Where(k => !filterNode(k));

			return yamlNodes.ToDictionaryWithConflictLog(k => k.Key.ToLowerInvariant(), makeObject, "LoadFromManifest<" + name + ">");
		}

		public static MiniYaml ResolveIndividualNode(MiniYamlNode inputNode, List<MiniYamlNode> resolvedRulesYaml = null)
		{
			if (resolvedRulesYaml != null)
				return MiniYaml.AtomicMerge(inputNode, new List<IReadOnlyCollection<MiniYamlNode>>() { resolvedRulesYaml });
			else
				return MiniYaml.AtomicMerge(inputNode);
		}

		public static List<MiniYamlNode> LoadFilteredYaml(IReadOnlyFileSystem fileSystem, List<MiniYamlNode> yamlNodes, Func<MiniYamlNode, bool> filterNode = null)
		{
			// Optionally, the caller can filter out elements from the loaded set of nodes. Default behavior is unfiltered.
			if (filterNode != null)
				yamlNodes = yamlNodes.Where(k => !filterNode(k)).ToList();

			return yamlNodes;
		}

		public static Dictionary<string, MiniYamlNode> LoadFilteredYamlToDictionary(IReadOnlyFileSystem fileSystem, List<MiniYamlNode> yamlNodes,
			string debugName, Func<MiniYamlNode, bool> filterNode = null)
		{
			return LoadFilteredYaml(fileSystem, yamlNodes, filterNode).ToDictionaryWithConflictLog(k => k.Key.ToLowerInvariant(), debugName, null, null);
		}

		public Dictionary<string, MiniYamlNode> GetUnresolvedRulesYaml(ModData modData, string[] rulesFiles)
		{
			var yamlNodes = MiniYaml.LoadWithoutInherits(modData.DefaultFileSystem, rulesFiles, null);
			static bool FilterNode(MiniYamlNode node) => node.Key.StartsWith(ActorInfo.AbstractActorPrefix);
			return LoadFilteredYamlToDictionary(modData.DefaultFileSystem, yamlNodes, "UnresolvedRulesYaml", FilterNode);
		}

		public Dictionary<string, MiniYamlNode> GetUnresolvedWeaponsYaml(ModData modData, string[] weaponFiles)
		{
			var yamlNodes = MiniYaml.LoadWithoutInherits(modData.DefaultFileSystem, weaponFiles, null);
			return LoadFilteredYamlToDictionary(modData.DefaultFileSystem, yamlNodes, "UnresolvedWeaponsYaml");
		}

		public void ReloadRules(World world, ModData modData,
			Dictionary<string, MiniYamlNode> unresolvedRules,
			Dictionary<string, MiniYamlNode> unresolvedWeapons)
		{
			if (unresolvedRules != null)
			{
				List<MiniYamlNode> newResolvedRulesYaml = new();

				try
				{
					// This will only assign the new YAML if it did not raise an exception when loading the YAML to newResolvedRulesYaml
					newResolvedRulesYaml = MiniYaml.Load(modData.DefaultFileSystem, modData.Manifest.Rules, null); // We need all resolved rules reloaded, not just the ones in the files
					ResolvedRulesYaml = newResolvedRulesYaml;

					// No rules were resolved after processing the YAML file, so we exit early
					if (newResolvedRulesYaml.Count == 0)
						return;
				}
				catch (Exception ex)
				{
					TextNotificationsManager.Debug($"Resolving the YAML rule files raised the exception: {ex.GetType().FullName} : {ex.Message}. Aborting.");
					return;
				}

				var actorInfos = new List<ActorInfo>();
				foreach (var actorKey in unresolvedRules)
				{
					UnresolvedRulesYamlDict[actorKey.Key] = actorKey.Value; // update the Ruleset's rules Yaml

					var actor = Actors.FirstOrDefault(s => string.Equals(s.Key, actorKey.Key, StringComparison.InvariantCultureIgnoreCase)).Value;

					Console.WriteLine($"Hot Reloading Found Actor: {actor.Name}");

					if (actor == null || actor.ActorUnresolvedRules == null || actor.ActorResolvedRules == null)
						continue;

					try
					{
						actor.LoadTraits(modData.ObjectCreator, actorKey.Value, true);
						actorInfos.Add(actor);
					}
					catch (Exception ex)
					{
						TextNotificationsManager.Debug($"Loading Actor {actor.Name} from YAML raised exception: {ex.GetType().FullName} : {ex.Message}");
					}
				}

				RulesetLoadedActors(actorInfos);
			}

			if (unresolvedWeapons != null)
			{
				try
				{
					// This will only assign the new YAML if it did not raise an exception when loading the YAML to newResolvedWeaponsYaml
					var newResolvedWeaponsYaml = MiniYaml.Load(modData.DefaultFileSystem, modData.Manifest.Weapons, null);
					ResolvedWeaponsYaml = newResolvedWeaponsYaml;

					// No weapons were resolved after processing the YAML file, so we exit early
					if (newResolvedWeaponsYaml.Count == 0)
						return;
				}
				catch (Exception ex)
				{
					TextNotificationsManager.Debug($"Resolving the YAML weapon files raised the exception: {ex.GetType().FullName} : {ex.Message}. Aborting.");
					return;
				}

				var weaponInfos = new List<WeaponInfo>();

				foreach (var weaponKey in unresolvedWeapons)
				{
					UnresolvedWeaponsYamlDict[weaponKey.Key] = weaponKey.Value; // update the Ruleset's weapons Yaml

					var weapon = Weapons.FirstOrDefault(s => string.Equals(s.Key, weaponKey.Key, StringComparison.InvariantCultureIgnoreCase)).Value;

					Console.WriteLine($"Hot Reloading Found Weapon: {weapon.Name}");

					if (weapon == null)
						continue;

					var newWeaponUnresolvedRules = new MiniYamlNodeBuilder(unresolvedWeapons
						.FirstOrDefault(s => string.Equals(s.Key, weapon.Name, StringComparison.InvariantCultureIgnoreCase)).Value);

					try
					{
						weapon.LoadYaml(newWeaponUnresolvedRules);
						weaponInfos.Add(weapon);
					}
					catch (Exception ex)
					{
						TextNotificationsManager.Debug($"Loading Weapon {weapon.Name} from YAML raised exception: {ex.GetType().FullName} : {ex.Message}");
					}
				}

				RulesetLoadedWeapons(weaponInfos);
			}
		}

		public void LoadActorTraitsFromRulesActor(World world, ModData modData, string actorKey)
		{
			if (!UnresolvedRulesYamlDict.ContainsKey(actorKey))
			{
				TextNotificationsManager.Debug($"Cannot load {actorKey} as it does not exist in the YAML Files. Aborting.");
				return;
			}

			try
			{
				var yamlUnresolvedNodes = MiniYaml.LoadWithoutInherits(modData.DefaultFileSystem, modData.Manifest.Rules, null);
				ResolvedRulesYaml = MiniYaml.Load(modData.DefaultFileSystem, modData.Manifest.Rules, null);
				static bool FilterNode(MiniYamlNode node) => node.Key.StartsWith(ActorInfo.AbstractActorPrefix);

				var actorUnresolvedRules = LoadFilteredYamlToDictionary(
					modData.DefaultFileSystem,
					yamlUnresolvedNodes,
					"UnresolvedRulesYaml",
					FilterNode)[actorKey.ToLowerInvariant()];

				UnresolvedRulesYamlDict[actorKey] = actorUnresolvedRules; // update the Ruleset's rules Yaml

				var actor = Actors.FirstOrDefault(s => string.Equals(s.Key, actorKey, StringComparison.InvariantCultureIgnoreCase)).Value;

				Console.WriteLine($"Hot Reloading Found Actor: {actor.Name}");

				if (actor == null || actor.ActorUnresolvedRules == null || actor.ActorResolvedRules == null)
					return;

				actor.LoadTraits(modData.ObjectCreator, new MiniYamlNodeBuilder(actorUnresolvedRules), true);
				RulesetLoadedActors(actorKey);
			}
			catch (Exception ex)
			{
				TextNotificationsManager.Debug($"Loading the actor {actorKey} raised the exception: {ex.GetType().FullName} : {ex.Message}");
			}
		}

		public void LoadWeapon(World world, ModData modData, string weaponKey)
		{
			if (!UnresolvedWeaponsYamlDict.ContainsKey(weaponKey))
			{
				TextNotificationsManager.Debug($"Cannot load {weaponKey} as it does not exist in the YAML Files. Aborting.");
				return;
			}

			try
			{
				var yamlNodes = MiniYaml.LoadWithoutInherits(modData.DefaultFileSystem, modData.Manifest.Weapons, null);
				var unresolvedWeapon = LoadFilteredYamlToDictionary(modData.DefaultFileSystem, yamlNodes, "UnresolvedWeaponsYaml")[weaponKey.ToLowerInvariant()];
				ResolvedWeaponsYaml = MiniYaml.Load(modData.DefaultFileSystem, modData.Manifest.Weapons, null);

				UnresolvedWeaponsYamlDict[weaponKey] = unresolvedWeapon;

				var weapon = Weapons.FirstOrDefault(s => string.Equals(s.Key, weaponKey, StringComparison.InvariantCultureIgnoreCase)).Value;

				Console.WriteLine($"Hot Reloading Found Weapon: {weapon.Name}");

				if (weapon == null)
					return;

				var newWeaponUnresolvedRules = new MiniYamlNodeBuilder(unresolvedWeapon);

				weapon.LoadYaml(newWeaponUnresolvedRules);
				RulesetLoadedWeapons(weaponKey);
			}
			catch (Exception ex)
			{
				TextNotificationsManager.Debug($"Loading the weapon {weaponKey} raised the exception: {ex.GetType().FullName} : {ex.Message}");
			}
		}

		public static Ruleset LoadDefaults(ModData modData)
		{
			var m = modData.Manifest;
			var fs = modData.DefaultFileSystem;

			Ruleset ruleset = null;
			void LoadRuleset()
			{
				bool ActorFilterNode(MiniYamlNode node) => node.Key.StartsWith(ActorInfo.AbstractActorPrefix);
				var unresolvedRulesYaml = LoadFilteredYamlToDictionary(fs, MiniYaml.LoadWithoutInherits(fs, m.Rules, null), "UnresolvedRulesYaml", ActorFilterNode);
				var resolvedRulesYaml = MiniYaml.Load(fs, m.Rules, null); // needs to not filter in order to include Inheritance nodes for AtomicMerge

				var actors = MergeOrDefault("Manifest,Rules", fs, m.Rules, null, null,
					k => new ActorInfo(modData.ObjectCreator, k.Key.ToLowerInvariant(), k),
					filterNode: n => n.Key.StartsWith(ActorInfo.AbstractActorPrefix));

				var unresolvedWeaponsYaml = LoadFilteredYamlToDictionary(fs, MiniYaml.LoadWithoutInherits(fs, m.Weapons, null), "UnresolvedRulesYaml");
				var resolvedWeaponsYaml = MiniYaml.Load(fs, m.Weapons, null);

				var weapons = MergeOrDefault("Manifest,Weapons", fs, m.Weapons, null, null,
					k => new WeaponInfo(k, k.Key));

				var voices = MergeOrDefault("Manifest,Voices", fs, m.Voices, null, null,
					k => new SoundInfo(k.Value));

				var notifications = MergeOrDefault("Manifest,Notifications", fs, m.Notifications, null, null,
					k => new SoundInfo(k.Value));

				var music = MergeOrDefault("Manifest,Music", fs, m.Music, null, null,
					k => new MusicInfo(k.Key, k.Value));

				var modelSequences = MergeOrDefault("Manifest,ModelSequences", fs, m.ModelSequences, null, null,
					k => k);

				// The default ruleset does not include a preferred tileset
				ruleset = new Ruleset(actors, weapons, voices, notifications, music, null, modelSequences,
					unresolvedRulesYaml, resolvedRulesYaml, unresolvedWeaponsYaml, resolvedWeaponsYaml);
			}

			if (modData.IsOnMainThread)
			{
				modData.HandleLoadingProgress();

				var loader = new Task(LoadRuleset);
				loader.Start();

				// Animate the loadscreen while we wait
				while (!loader.Wait(40))
					modData.HandleLoadingProgress();
			}
			else
				LoadRuleset();

			return ruleset;
		}

		public void RulesetLoadedWeapons(string weaponKey = null)
		{
			List<WeaponInfo> weaponInfos;

			if (weaponKey != null)
				weaponInfos = Weapons.Values.Where(s => string.Equals(s.Name, weaponKey, StringComparison.InvariantCultureIgnoreCase)).ToList();
			else
				weaponInfos = Weapons.Values.ToList();

			RulesetLoadedWeapons(weaponInfos);
		}

		public void RulesetLoadedWeapons(IEnumerable<WeaponInfo> weaponInfos)
		{
			foreach (var weapon in weaponInfos)
			{
				weapon.RulesetLoaded(this, weapon);
				if (weapon.Projectile is IRulesetLoaded<WeaponInfo> projectileLoaded)
				{
					try
					{
						projectileLoaded.RulesetLoaded(this, weapon);
					}
					catch (YamlException e)
					{
						throw new YamlException($"Projectile type {weapon.Name}: {e.Message}");
					}
				}

				foreach (var warhead in weapon.Warheads)
				{
					if (warhead is IRulesetLoaded<WeaponInfo> cacher)
					{
						try
						{
							cacher.RulesetLoaded(this, weapon);
						}
						catch (YamlException e)
						{
							throw new YamlException($"Weapon type {weapon.Name}: {e.Message}");
						}
					}
				}
			}
		}

		public void RulesetLoadedActors(string actorKey = null)
		{
			List<ActorInfo> actorInfos;

			if (actorKey != null)
				actorInfos = Actors.Values.Where(s => string.Equals(s.Name, actorKey, StringComparison.InvariantCultureIgnoreCase)).ToList();
			else
				actorInfos = Actors.Values.ToList();

			RulesetLoadedActors(actorInfos);
		}

		public void RulesetLoadedActors(IEnumerable<ActorInfo> actorInfos)
		{
			foreach (var a in actorInfos)
			{
				a.RulesetLoaded(this, a);
				foreach (var t in a.TraitInfos<IRulesetLoaded>())
				{
					try
					{
						t.RulesetLoaded(this, a);
					}
					catch (YamlException e)
					{
						throw new YamlException($"Actor type {a.Name}: {e.Message}");
					}
				}
			}
		}

		public static Ruleset LoadDefaultsForTileSet(ModData modData, string tileSet)
		{
			var dr = modData.DefaultRules;
			var terrainInfo = modData.DefaultTerrainInfo[tileSet];

			return new Ruleset(dr.Actors, dr.Weapons, dr.Voices, dr.Notifications, dr.Music, terrainInfo, dr.ModelSequences);
		}

		public static Ruleset Load(ModData modData, IReadOnlyFileSystem fileSystem, string tileSet,
			MiniYaml mapRules, MiniYaml mapWeapons, MiniYaml mapVoices, MiniYaml mapNotifications,
			MiniYaml mapMusic, MiniYaml mapModelSequences)
		{
			var m = modData.Manifest;
			var dr = modData.DefaultRules;

			Ruleset ruleset = null;
			void LoadRuleset()
			{
				var unresolvedRulesYaml = LoadFilteredYamlToDictionary(
					fileSystem,
					MiniYaml.LoadWithoutInherits(fileSystem, m.Rules, mapRules),
					"UnresolvedRulesYaml",
					(MiniYamlNode node) => node.Key.StartsWith(ActorInfo.AbstractActorPrefix));

				// needs to not filter in order to include Inheritance nodes for AtomicMerge
				var resolvedRulesYaml = MiniYaml.Load(fileSystem, m.Rules, mapRules);

				var actors = MergeOrDefault("Rules", fileSystem, m.Rules, mapRules, dr.Actors,
					k => new ActorInfo(modData.ObjectCreator, k.Key.ToLowerInvariant(), k),
					filterNode: n => n.Key.StartsWith(ActorInfo.AbstractActorPrefix));

				var unresolvedWeaponsYaml = LoadFilteredYamlToDictionary(
					fileSystem,
					MiniYaml.LoadWithoutInherits(fileSystem, m.Weapons, mapWeapons),
					"UnresolvedRulesYaml");

				var resolvedWeaponsYaml = MiniYaml.Load(fileSystem, m.Weapons, mapWeapons);

				var weapons = MergeOrDefault("Weapons", fileSystem, m.Weapons, mapWeapons, dr.Weapons,
					k => new WeaponInfo(k, k.Key));

				var voices = MergeOrDefault("Voices", fileSystem, m.Voices, mapVoices, dr.Voices,
					k => new SoundInfo(k.Value));

				var notifications = MergeOrDefault("Notifications", fileSystem, m.Notifications, mapNotifications, dr.Notifications,
					k => new SoundInfo(k.Value));

				var music = MergeOrDefault("Music", fileSystem, m.Music, mapMusic, dr.Music,
					k => new MusicInfo(k.Key, k.Value));

				// TODO: Add support for merging custom terrain modifications
				var terrainInfo = modData.DefaultTerrainInfo[tileSet];

				var modelSequences = dr.ModelSequences;
				if (mapModelSequences != null)
					modelSequences = MergeOrDefault("ModelSequences", fileSystem, m.ModelSequences, mapModelSequences, dr.ModelSequences,
						k => k);

				ruleset = new Ruleset(actors, weapons, voices, notifications, music, terrainInfo, modelSequences,
					unresolvedRulesYaml, resolvedRulesYaml, unresolvedWeaponsYaml, resolvedWeaponsYaml);
			}

			if (modData.IsOnMainThread)
			{
				modData.HandleLoadingProgress();

				var loader = new Task(LoadRuleset);
				loader.Start();

				// Animate the loadscreen while we wait
				while (!loader.Wait(40))
					modData.HandleLoadingProgress();
			}
			else
				LoadRuleset();

			return ruleset;
		}

		static bool AnyCustomYaml(MiniYaml yaml)
		{
			return yaml != null && (yaml.Value != null || yaml.Nodes.Length > 0);
		}

		static bool AnyFlaggedTraits(ModData modData, IEnumerable<MiniYamlNode> actors)
		{
			foreach (var actorNode in actors)
			{
				foreach (var traitNode in actorNode.Value.Nodes)
				{
					try
					{
						var traitName = traitNode.Key.Split('@')[0];
						var traitType = modData.ObjectCreator.FindType(traitName + "Info");
						if (traitType != null && traitType.GetInterface(nameof(ILobbyCustomRulesIgnore)) == null)
							return true;
					}
					catch (Exception ex)
					{
						Log.Write("debug", "Error in AnyFlaggedTraits\n" + ex.ToString());
					}
				}
			}

			return false;
		}

		public static bool DefinesUnsafeCustomRules(ModData modData, IReadOnlyFileSystem fileSystem,
			MiniYaml mapRules, MiniYaml mapWeapons, MiniYaml mapVoices, MiniYaml mapNotifications, MiniYaml mapSequences)
		{
			// Maps that define any weapon, voice, notification, or sequence overrides are always flagged
			if (AnyCustomYaml(mapWeapons) || AnyCustomYaml(mapVoices) || AnyCustomYaml(mapNotifications) || AnyCustomYaml(mapSequences))
				return true;

			// Any trait overrides that aren't explicitly whitelisted are flagged
			if (mapRules == null)
				return false;

			if (AnyFlaggedTraits(modData, mapRules.Nodes))
				return true;

			if (mapRules.Value != null)
			{
				var mapFiles = FieldLoader.GetValue<string[]>("value", mapRules.Value);
				foreach (var f in mapFiles)
					if (AnyFlaggedTraits(modData, MiniYaml.FromStream(fileSystem.Open(f), f)))
						return true;
			}

			return false;
		}
	}
}
