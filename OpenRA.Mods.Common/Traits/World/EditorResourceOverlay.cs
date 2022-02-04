#region Copyright & License Information
/*
 * Copyright 2007-2022 The OpenRA Developers (see AUTHORS)
 * This file is part of OpenRA, which is free software. It is made
 * available to you under the terms of the GNU General Public License
 * as published by the Free Software Foundation, either version 3 of
 * the License, or (at your option) any later version. For more
 * information, see COPYING.
 */
#endregion

using System;
using System.Collections.Generic;
using OpenRA.Graphics;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Traits
{
	[TraitLocation(SystemActors.EditorWorld)]
	public class EditorResourceOverlayInfo : TraitInfo
	{
		[PaletteReference]
		[Desc("Palette to use for rendering the sprite.")]
		public readonly string Palette = TileSet.TerrainPaletteInternalName;

		[Desc("Sprite definition.")]
		public readonly string Image = "overlay";

		[SequenceReference("Image")]
		[Desc("Sequence to use for unselected resource tiles.")]
		public readonly string Sequence = "build-valid";

		[SequenceReference("Image")]
		[Desc("Sequence to use for selected resource tiles.")]
		public readonly string SelectedSequence = "build-invalid";

		[Desc("Custom opacity to apply to the overlay sprite.")]
		public readonly float Alpha = 1f;

		public override object Create(ActorInitializer init)
		{
			return new EditorResourceOverlay(init.Self, this);
		}
	}

	public class EditorResourceOverlay : IRenderAboveWorld, IWorldLoaded, INotifyActorDisposing
	{
		readonly EditorResourceOverlayInfo info;
		readonly World world;
		readonly Sprite sprite;
		readonly Sprite sprite2;

		public bool Enabled = false;
		TerrainSpriteLayer render;
		PaletteReference palette;

		EditorResourceLayer editorResourceLayer;

		readonly List<CashIndex> cashIndexes = new List<CashIndex>();

		bool disposed;

		public EditorResourceOverlay(Actor self, EditorResourceOverlayInfo info)
		{
			this.info = info;
			world = self.World;

			var rules = self.World.Map.Rules;

			sprite = rules.Sequences.GetSequence(info.Image, info.Sequence).GetSprite(0);
			sprite2 = rules.Sequences.GetSequence(info.Image, info.SelectedSequence).GetSprite(0);
		}

		void IWorldLoaded.WorldLoaded(World w, WorldRenderer wr)
		{
			render = new TerrainSpriteLayer(w, wr, sprite, BlendMode.Alpha, wr.World.Type != WorldType.Editor);

			world.Map.Resources.CellEntryChanged += UpdatesIndexCells;

			palette = wr.Palette(info.Palette);

			editorResourceLayer = world.WorldActor.TraitOrDefault<EditorResourceLayer>();

			foreach (var mc in editorResourceLayer.ResourceTypesByIndex)
			{
				cashIndexes.Add(new CashIndex(w, mc.Value, editorResourceLayer));
			}

			foreach (var i in cashIndexes)
			{
				i.ExecuteInAllDomains((c) => render.Update(c, sprite, palette, 1f, info.Alpha));
			}
		}

		readonly HashSet<CPos> dirtyCells = new HashSet<CPos>();
		ClusterContents selectedDomain;
		CashIndex selectedCashIndex;

		public int UpdateDomain(CPos cell)
		{
			if (!world.Map.AllCells.Contains(cell))
				return selectedDomain == null ? 0 : selectedDomain.ClusterId;

			foreach (var b in cashIndexes)
			{
				var domain = b.GetCluster(cell);
				if (b == selectedCashIndex && domain != selectedDomain)
				{
					b.DeleteOldOverlay((c) => render.Update(c, sprite, palette, 1f, info.Alpha));
					selectedCashIndex = null;
					selectedDomain = null;
				}
			}

			foreach (var b in cashIndexes)
			{
				var domain = b.GetCluster(cell);
				if (domain != selectedDomain && !domain.IsEmpty)
				{
					b.ExecuteInOneDomain(cell, (c) => render.Update(c, sprite2, palette, 1f, info.Alpha));
					if (selectedCashIndex == null)
					{
						selectedDomain = domain;
						selectedCashIndex = b;
						return domain.ClusterId;
					}
				}
			}

			return selectedDomain == null ? 0 : selectedDomain.ClusterId;
		}

		void UpdatesIndexCells(CPos dirty)
		{
			if (!dirtyCells.Contains(dirty))
				dirtyCells.Add(dirty);
		}

		void IRenderAboveWorld.RenderAboveWorld(Actor self, WorldRenderer wr)
		{
			if (Enabled)
			{
				if (dirtyCells.Count != 0)
				{
					foreach (var a in cashIndexes)
					{
						Console.WriteLine("Update " + a.ResourceType);
						a.UpdateCells(dirtyCells);
					}

					foreach (var a in dirtyCells)
					{
						var b = cashIndexes.Find(x => !x.GetCluster(a).IsEmpty);
						if (b == null)
						{
							render.Update(a, null, palette, 1f, info.Alpha);
							var list = cashIndexes.Find(x => x.OldDomain != null).OldDomain;
							// Console.WriteLine("omg");
							// list.Do(x => Console.WriteLine(x.X + " " + x.Y));
							list.Remove(a);
							// Console.WriteLine("fff");
							// list.Do(x => Console.WriteLine(x.X + " " + x.Y));
						}
						else
						{
							render.Update(a, b == selectedCashIndex ? sprite2 : sprite, palette, 1f, info.Alpha);
						}
					}

					dirtyCells.Clear();
				}

				render.Draw(wr.Viewport);
			}
		}

		void INotifyActorDisposing.Disposing(Actor self)
		{
			if (disposed)
				return;

			render.Dispose();
			disposed = true;
		}
	}

	class CashIndex : ClusterManager
	{
		public readonly string ResourceType;
		protected readonly EditorResourceLayer editorResourceLayer;
		public CashIndex(World world, string resourceType, EditorResourceLayer editorResourceLayer)
		: base(world)
		{
			this.editorResourceLayer = editorResourceLayer;
			ResourceType = resourceType;
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

		public ClusterContents GetCluster(CPos cell) { return clusterLayer[cell]; }

		public List<CPos> OldDomain;

		public void DeleteOldOverlay(Action<CPos> update)
		{
			if (OldDomain != null)
			{
				foreach (var c in OldDomain)
				{
					update(c);
				}

				OldDomain = null;
			}
		}

		public void ExecuteInOneDomain(CPos cell, Action<CPos> update)
		{
			var domain = clusterLayer[cell];
			foreach (var c in domain.Cells)
				update(c);

			OldDomain = new List<CPos>(domain.Cells);
		}

		protected override bool ClusterCondition(CPos p)
		{
			if (!map.Contains(p))
				return false;

			return editorResourceLayer.IsResoureCell(p, ResourceType);
		}

		protected override ushort BuildDomains()
		{
			var domain = base.BuildDomains();
			Log.Write("debug", "Found {0} cash indexes for resource types {1} on map {2}.", domain - 1, ResourceType, map.Title);
			return domain;
		}
	}
}
