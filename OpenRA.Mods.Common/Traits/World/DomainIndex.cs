#region Copyright & License Information
/*
 * Copyright 2007-2021 The OpenRA Developers (see AUTHORS)
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
using OpenRA.Support;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Traits
{
	[TraitLocation(SystemActors.World)]
	[Desc("Identify untraversable regions of the map for faster pathfinding, especially with AI.",
		"This trait is required. Every mod needs it attached to the world actor.")]
	class DomainIndexInfo : TraitInfo<DomainIndex> { }

	public class DomainIndex : IWorldLoaded, IRenderAboveWorld
	{
		readonly Dictionary<uint, MovementClassDomainIndex> domainIndexes = new Dictionary<uint, MovementClassDomainIndex>();

		static readonly Dictionary<uint, TerrainSpriteLayer> renders = new Dictionary<uint, TerrainSpriteLayer>();
		static PaletteReference palette;

		Sprite sprite1;
		public void WorldLoaded(World world, WorldRenderer wr)
		{
			sprite1 = world.Map.Rules.Sequences.GetSequence("overlay", "build-invalid").GetSprite(0);
			// sprite2 = world.Map.Rules.Sequences.GetSequence("overlay", "target-select").GetSprite(0);

			palette = wr.Palette("ra");

			var locomotors = world.WorldActor.TraitsImplementing<Locomotor>().Where(l => !string.IsNullOrEmpty(l.Info.Name));
			var movementClasses = locomotors.Select(t => t.MovementClass).Distinct();

			foreach (var mc in movementClasses)
			{
				domainIndexes[mc] = new MovementClassDomainIndex(world, mc);
				renders[mc] = new TerrainSpriteLayer(world, wr, sprite1, BlendMode.Alpha, wr.World.Type != WorldType.Regular);
			}

			foreach (var d in domainIndexes)
			{
				d.Value.ExecuteInAllDomains((c) => renders[d.Key].Update(c, sprite1, palette, 1f, TileSet.TerrainPaletteInternalName == "ts" ? 1f : 0.35f));
			}
		}

		public bool IsPassable(CPos p1, CPos p2, Locomotor locomotor)
		{
			// HACK: Work around units in other movement layers from being blocked
			// when the point in the main layer is not pathable
			if (p1.Layer != 0 || p2.Layer != 0)
			{
				return true;
			}

			if (locomotor.Info.DisableDomainPassabilityCheck)
			{
				return true;
			}

			return domainIndexes[locomotor.MovementClass].IsPassable(p1, p2);
		}

		/// <summary>Regenerate the domain index for a group of cells.</summary>
		public void UpdateCells(IEnumerable<CPos> cells)
		{
			Console.WriteLine("Update Cells");
			var dirty = cells.ToHashSet();
			foreach (var index in domainIndexes)
				index.Value.UpdateCells(dirty);
		}

		public void AddFixedConnection(IEnumerable<CPos> cells)
		{
			foreach (var index in domainIndexes)
				index.Value.AddFixedConnection(cells);
		}

		int timer = 0;
		void IRenderAboveWorld.RenderAboveWorld(Actor self, WorldRenderer wr)
		{
			if (timer >= domainIndexes.Count * 50)
			{
				timer = 0;
			}

			timer++;

			var cnt = 0;
			foreach (var a in renders)
			{
				if (cnt == timer / 50)
					a.Value.Draw(wr.Viewport);
				cnt++;
			}
		}

		public string GetDomain(CPos cell)
		{
			var cnt = 0;
			foreach (var a in domainIndexes)
			{
				if (cnt == timer / 50)
					return a.Value.GetDomain(cell).ToString() + " " + a.Key.ToString();
				cnt++;
			}

			return "EmptyClusterrror";
		}
	}

	class MovementClassDomainIndex : ClusterManager
	{
		protected readonly uint movementClass;
		public MovementClassDomainIndex(World world, uint movementClass)
		: base(world)
		{
			this.movementClass = movementClass;
			using (new PerfTimer($"BuildDomains: {world.Map.Title} for movement class {movementClass}"))
				BuildDomains();
		}

		public void ExecuteInAllDomains(Action<CPos> update)
		{
			foreach (var domain in clusters)
			{
				if (!domain.IsEmpty)
				{
					foreach (var c in domain.Cells)
						update(c);
				}
			}
		}

		protected override bool ClusterCondition(CPos p)
		{
			if (!map.Contains(p))
				return false;

			return (movementClass & (1 << world.Map.GetTerrainIndex(p))) > 0;
		}

		public ushort GetDomain(CPos cell) { return clusterLayer[cell].ClusterId; }

		protected override ushort BuildDomains()
		{
			var domain = base.BuildDomains();
			Log.Write("debug", "Found {0} domains for movement class {1} on map {2}.", domain - 1, movementClass, map.Title);
			return domain;
		}
	}
}
