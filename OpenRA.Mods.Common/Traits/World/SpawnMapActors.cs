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
using OpenRA.Graphics;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Traits
{
	[TraitLocation(SystemActors.World)]
	[Desc("Spawns the initial units for each player upon game start.")]
	public class SpawnMapActorsInfo : TraitInfo<SpawnMapActors> { }

	public class SpawnMapActors : IWorldLoaded
	{
		public Dictionary<string, Actor> Actors = new();
		public uint LastMapActorID { get; private set; }

		public void WorldLoaded(World world, WorldRenderer wr)
		{
			var yaml = new List<MiniYamlNode>();
			var bridgeActors = new List<Actor>();
			var bridgeHutActors = new List<string>();

			Actor AddActor(string name, ActorInfo actor, ActorInit init = null)
			{
				var actorReference = new ActorReference(name)
				{
					new OwnerInit(world.WorldActor.Owner),
					new FactionInit(world.WorldActor.Owner.Faction.InternalName),
					new LocationInit(CPos.Zero),
				};

				if (init != null)
					actorReference.Add(init);

				Console.WriteLine($"Adding actor {name}");
				try
				{
					var a = world.CreateActor(false, actorReference);
					var bounds = a.TraitOrDefault<IMouseBounds>().MouseoverBounds(a, wr).BoundingRect;
					yaml.Add(new MiniYamlNode(name, bounds.ToString()));
					return a;
				}
				catch (Exception e)
				{
					Console.WriteLine($"Failed to get bounds for {name}: {e.Message}");
				}

				return null;
			}

			foreach (var ai in Game.ModData.DefaultRules.Actors)
			{
				if (!ai.Value.HasTraitInfo<IMouseBoundsInfo>())
					continue;

				if (ai.Value.HasTraitInfo<LegacyBridgeHutInfo>())
					bridgeHutActors.Add(ai.Key);
				else
				{
					var a = AddActor(ai.Key, ai.Value);
					if (ai.Value.HasTraitInfo<BridgeInfo>())
						bridgeActors.Add(a);
				}
			}

			foreach (var a in bridgeHutActors)
			{
				var bridge = bridgeActors.FirstOrDefault();
				if (bridge == null)
					continue;

				AddActor(a, Game.ModData.DefaultRules.Actors[a], new ParentActorInit(bridge));
			}

			yaml.WriteToFile($"mousebounds-{Game.ModData.Manifest.Id}.yaml");

			var preventMapSpawns = world.WorldActor.TraitsImplementing<IPreventMapSpawn>()
				.Concat(world.WorldActor.Owner.PlayerActor.TraitsImplementing<IPreventMapSpawn>())
				.ToArray();

			foreach (var kv in world.Map.ActorDefinitions)
			{
				var actorReference = new ActorReference(kv.Value.Value, kv.Value.ToDictionary());

				// If an actor's doesn't have a valid owner transfer ownership to neutral
				var ownerInit = actorReference.Get<OwnerInit>();
				if (!world.Players.Any(p => p.InternalName == ownerInit.InternalName))
				{
					actorReference.Remove(ownerInit);
					actorReference.Add(new OwnerInit(world.WorldActor.Owner));
				}

				actorReference.Add(new SkipMakeAnimsInit());
				actorReference.Add(new SpawnedByMapInit(kv.Key));

				if (PreventMapSpawn(world, actorReference, preventMapSpawns))
					continue;

				var actor = world.CreateActor(true, actorReference);
				Actors[kv.Key] = actor;
				LastMapActorID = actor.ActorID;
			}
		}

		static bool PreventMapSpawn(World world, ActorReference actorReference, IEnumerable<IPreventMapSpawn> preventMapSpawns)
		{
			foreach (var pms in preventMapSpawns)
				if (pms.PreventMapSpawn(world, actorReference))
					return true;

			return false;
		}
	}

	public class SkipMakeAnimsInit : RuntimeFlagInit { }
	public class SpawnedByMapInit : ValueActorInit<string>, ISuppressInitExport, ISingleInstanceInit
	{
		public SpawnedByMapInit(string value)
			: base(value) { }
	}
}
