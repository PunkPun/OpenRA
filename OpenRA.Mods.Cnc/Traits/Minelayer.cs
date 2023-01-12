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
using OpenRA.Mods.Cnc.Activities;
using OpenRA.Mods.Common.Orders;
using OpenRA.Mods.Common.Traits;
using OpenRA.Primitives;
using OpenRA.Traits;

namespace OpenRA.Mods.Cnc.Traits
{
	public class MinelayerInfo : TraitInfo, Requires<RearmableInfo>
	{
		[ActorReference]
		public readonly string Mine = "minv";

		public readonly string AmmoPoolName = "primary";

		public readonly WDist MinefieldDepth = new WDist(1536);

		[VoiceReference]
		[Desc("Voice to use when ordered to lay a minefield.")]
		public readonly string Voice = "Action";

		[Desc("Color to use for the target line when laying mines.")]
		public readonly Color TargetLineColor = Color.Crimson;

		[Desc("Sprite overlay to use for valid minefield cells.")]
		public readonly string TileValidName = "build-valid";

		[Desc("Sprite overlay to use for invalid minefield cells.")]
		public readonly string TileInvalidName = "build-invalid";

		[Desc("Sprite overlay to use for minefield cells hidden behind fog or shroud.")]
		public readonly string TileUnknownName = "build-unknown";

		[Desc("Only allow laying mines on listed terrain types. Leave empty to allow all terrain types.")]
		public readonly HashSet<string> TerrainTypes = new HashSet<string>();

		[CursorReference]
		[Desc("Cursor to display when able to lay a mine.")]
		public readonly string DeployCursor = "deploy";

		[CursorReference]
		[Desc("Cursor to display when unable to lay a mine.")]
		public readonly string DeployBlockedCursor = "deploy-blocked";

		[CursorReference]
		[Desc("Cursor to display when able to lay a mine.")]
		public readonly string AbilityCursor = "ability";

		[Desc("Ammo the minelayer consumes per mine.")]
		public readonly int AmmoUsage = 1;

		public override object Create(ActorInitializer init) { return new Minelayer(init.Self, this); }
	}

	public class Minelayer : IIssueOrder, IResolveOrder, ISync, IIssueDeployOrder, IOrderVoice, ITick
	{
		public readonly MinelayerInfo Info;
		public readonly Sprite Tile;

		public readonly Actor Actor;

		[Sync]
		CPos minefieldStart;

		public Minelayer(Actor self, MinelayerInfo info)
		{
			Info = info;
			Actor = self;

			var tileset = self.World.Map.Tileset.ToLowerInvariant();
			if (self.World.Map.Rules.Sequences.HasSequence("overlay", $"{Info.TileValidName}-{tileset}"))
				Tile = self.World.Map.Rules.Sequences.GetSequence("overlay", $"{Info.TileValidName}-{tileset}").GetSprite(0);
			else
				Tile = self.World.Map.Rules.Sequences.GetSequence("overlay", Info.TileValidName).GetSprite(0);
		}

		IEnumerable<IOrderTargeter> IIssueOrder.Orders
		{
			get
			{
				yield return new BeginMinefieldOrderTargeter(Actor, Info.AbilityCursor);
				yield return new DeployOrderTargeter(Actor, "PlaceMine", 5, () => IsCellAcceptable(Actor.Location) ? Info.DeployCursor : Info.DeployBlockedCursor);
			}
		}

		Order IIssueOrder.IssueOrder(IOrderTargeter order, in Target target, bool queued)
		{
			switch (order.OrderID)
			{
				case "BeginMinefield":
					var start = Actor.World.Map.CellContaining(target.CenterPosition);
					if (Actor.World.OrderGenerator is MinefieldOrderGenerator generator)
						generator.AddMinelayer(Actor);
					else
						Actor.World.OrderGenerator = new MinefieldOrderGenerator(Actor, start, queued);

					return new Order("BeginMinefield", Actor, Target.FromCell(Actor.World, start), queued);
				case "PlaceMine":
					return new Order("PlaceMine", Actor, Target.FromCell(Actor.World, Actor.Location), queued);
				default:
					return null;
			}
		}

		Order IIssueDeployOrder.IssueDeployOrder(bool queued)
		{
			return new Order("PlaceMine", Actor, Target.FromCell(Actor.World, Actor.Location), queued);
		}

		bool IIssueDeployOrder.CanIssueDeployOrder(bool queued)
		{
			return IsCellAcceptable(Actor.Location);
		}

		void IResolveOrder.ResolveOrder(Order order)
		{
			if (order.OrderString != "BeginMinefield" && order.OrderString != "PlaceMinefield" && order.OrderString != "PlaceMine")
				return;

			var cell = Actor.World.Map.CellContaining(order.Target.CenterPosition);
			if (order.OrderString == "BeginMinefield")
				minefieldStart = cell;
			else if (order.OrderString == "PlaceMine")
			{
				if (IsCellAcceptable(cell))
					Actor.QueueActivity(order.Queued, new LayMines(Actor));
			}
			else if (order.OrderString == "PlaceMinefield")
			{
				// A different minelayer might have started laying the field without this minelayer knowing the start
				minefieldStart = order.ExtraLocation;

				var movement = Actor.Trait<IPositionable>();
				var mobile = movement as Mobile;

				var minefield = GetMinefieldCells(minefieldStart, cell, Info.MinefieldDepth)
					.Where(c => IsCellAcceptable(c) && Actor.Owner.Shroud.IsExplored(c)
						&& movement.CanEnterCell(c, null, BlockedByActor.Immovable) && mobile != null && mobile.CanStayInCell(c))
					.OrderBy(c => (c - minefieldStart).LengthSquared).ToList();

				Actor.QueueActivity(order.Queued, new LayMines(Actor, minefield));
				Actor.ShowTargetLines();
			}
		}

		void ITick.Tick()
		{
			if (Actor.CurrentActivity != null)
				foreach (var field in Actor.CurrentActivity.ActivitiesImplementing<LayMines>())
					field.CleanMineField();
		}

		string IOrderVoice.VoicePhraseForOrder(Order order)
		{
			if (order.OrderString == "PlaceMine" || order.OrderString == "PlaceMinefield")
				return Info.Voice;

			return null;
		}

		static IEnumerable<CPos> GetMinefieldCells(CPos start, CPos end, WDist depth)
		{
			var mins = new CPos(Math.Min(start.X, end.X), Math.Min(start.Y, end.Y));
			var maxs = new CPos(Math.Max(start.X, end.X), Math.Max(start.Y, end.Y));

			// TODO: proper endcaps, if anyone cares (which won't happen unless depth is large)
			var p = end - start;
			var q = new float2(p.Y, -p.X);
			q = (start != end) ? (1 / q.Length) * q : new float2(1, 0);
			var c = -float2.Dot(q, new float2(start.X, start.Y));

			// return all points such that |ax + by + c| < depth
			// HACK: This will return the wrong results for isometric cells
			for (var i = mins.X; i <= maxs.X; i++)
				for (var j = mins.Y; j <= maxs.Y; j++)
					if (Math.Abs(q.X * i + q.Y * j + c) * 1024 < depth.Length)
						yield return new CPos(i, j);
		}

		public bool IsCellAcceptable(CPos cell)
		{
			if (!Actor.World.Map.Contains(cell))
				return false;

			if (Info.TerrainTypes.Count == 0)
				return true;

			var terrainType = Actor.World.Map.GetTerrainInfo(cell).Type;
			return Info.TerrainTypes.Contains(terrainType);
		}

		class MinefieldOrderGenerator : OrderGenerator
		{
			readonly List<Actor> minelayers;
			readonly Minelayer minelayer;
			readonly Sprite validTile, unknownTile, blockedTile;
			readonly float validAlpha, unknownAlpha, blockedAlpha;
			readonly CPos minefieldStart;
			readonly bool queued;
			readonly string cursor;

			public MinefieldOrderGenerator(Actor a, CPos xy, bool queued)
			{
				minelayers = new List<Actor>() { a };
				minefieldStart = xy;
				this.queued = queued;

				minelayer = a.Trait<Minelayer>();
				var tileset = a.World.Map.Tileset.ToLowerInvariant();
				if (a.World.Map.Rules.Sequences.HasSequence("overlay", $"{minelayer.Info.TileValidName}-{tileset}"))
				{
					var validSequence = a.World.Map.Rules.Sequences.GetSequence("overlay", $"{minelayer.Info.TileValidName}-{tileset}");
					validTile = validSequence.GetSprite(0);
					validAlpha = validSequence.GetAlpha(0);
				}
				else
				{
					var validSequence = a.World.Map.Rules.Sequences.GetSequence("overlay", minelayer.Info.TileValidName);
					validTile = validSequence.GetSprite(0);
					validAlpha = validSequence.GetAlpha(0);
				}

				if (a.World.Map.Rules.Sequences.HasSequence("overlay", $"{minelayer.Info.TileUnknownName}-{tileset}"))
				{
					var unknownSequence = a.World.Map.Rules.Sequences.GetSequence("overlay", $"{minelayer.Info.TileUnknownName}-{tileset}");
					unknownTile = unknownSequence.GetSprite(0);
					unknownAlpha = unknownSequence.GetAlpha(0);
				}
				else
				{
					var unknownSequence = a.World.Map.Rules.Sequences.GetSequence("overlay", minelayer.Info.TileUnknownName);
					unknownTile = unknownSequence.GetSprite(0);
					unknownAlpha = unknownSequence.GetAlpha(0);
				}

				if (a.World.Map.Rules.Sequences.HasSequence("overlay", $"{minelayer.Info.TileInvalidName}-{tileset}"))
				{
					var blockedSequence = a.World.Map.Rules.Sequences.GetSequence("overlay", $"{minelayer.Info.TileInvalidName}-{tileset}");
					blockedTile = blockedSequence.GetSprite(0);
					blockedAlpha = blockedSequence.GetAlpha(0);
				}
				else
				{
					var blockedSequence = a.World.Map.Rules.Sequences.GetSequence("overlay", minelayer.Info.TileInvalidName);
					blockedTile = blockedSequence.GetSprite(0);
					blockedAlpha = blockedSequence.GetAlpha(0);
				}

				cursor = minelayer.Info.AbilityCursor;
			}

			public void AddMinelayer(Actor a)
			{
				minelayers.Add(a);
			}

			protected override IEnumerable<Order> OrderInner(World world, CPos cell, int2 worldPixel, MouseInput mi)
			{
				if (mi.Button == Game.Settings.Game.MouseButtonPreference.Cancel)
				{
					world.CancelInputMode();
					yield break;
				}

				if (mi.Button == Game.Settings.Game.MouseButtonPreference.Action)
				{
					minelayers.First().World.CancelInputMode();
					foreach (var minelayer in minelayers)
						yield return new Order("PlaceMinefield", minelayer, Target.FromCell(world, cell), queued) { ExtraLocation = minefieldStart };
				}
			}

			protected override void SelectionChanged(World world, IEnumerable<Actor> selected)
			{
				minelayers.Clear();
				minelayers.AddRange(selected.Where(s => !s.IsDead && s.Info.HasTraitInfo<MinelayerInfo>()));
				if (minelayers.Count == 0)
					world.CancelInputMode();
			}

			protected override IEnumerable<IRenderable> Render(WorldRenderer wr, World world) { yield break; }
			protected override IEnumerable<IRenderable> RenderAboveShroud(WorldRenderer wr, World world)
			{
				var minelayer = minelayers.FirstOrDefault(m => m.IsInWorld && !m.IsDead);
				if (minelayer == null)
					yield break;

				// We get the biggest depth so we cover all cells that mines could be placed on.
				var lastMousePos = wr.Viewport.ViewToWorld(Viewport.LastMousePos);
				var minefield = GetMinefieldCells(minefieldStart, lastMousePos,
					minelayers.Max(m => m.Info.TraitInfo<MinelayerInfo>().MinefieldDepth));

				var movement = minelayer.Trait<IPositionable>();
				var mobile = movement as Mobile;
				var pal = wr.Palette(TileSet.TerrainPaletteInternalName);
				foreach (var c in minefield)
				{
					var tile = validTile;
					var alpha = validAlpha;
					if (!world.Map.Contains(c))
					{
						tile = blockedTile;
						alpha = blockedAlpha;
					}
					else if (world.ShroudObscures(c))
					{
						tile = blockedTile;
						alpha = blockedAlpha;
					}
					else if (world.FogObscures(c))
					{
						tile = unknownTile;
						alpha = unknownAlpha;
					}
					else if (!this.minelayer.IsCellAcceptable(c)
						|| !movement.CanEnterCell(c, null, BlockedByActor.Immovable) || (mobile != null && !mobile.CanStayInCell(c)))
					{
						tile = blockedTile;
						alpha = blockedAlpha;
					}

					yield return new SpriteRenderable(tile, world.Map.CenterOfCell(c), WVec.Zero, -511, pal, 1f, alpha, float3.Ones, TintModifiers.IgnoreWorldTint, true);
				}
			}

			protected override IEnumerable<IRenderable> RenderAnnotations(WorldRenderer wr, World world) { yield break; }

			protected override string GetCursor(World world, CPos cell, int2 worldPixel, MouseInput mi)
			{
				return cursor;
			}
		}

		class BeginMinefieldOrderTargeter : IOrderTargeter
		{
			public string OrderID => "BeginMinefield";
			public int OrderPriority => 5;
			public readonly Actor Actor;

			readonly string cursor;

			public BeginMinefieldOrderTargeter(Actor self, string cursor)
			{
				Actor = self;
				this.cursor = cursor;
			}

			public bool TargetOverridesSelection(in Target target, List<Actor> actorsAt, CPos xy, TargetModifiers modifiers) { return true; }

			public bool CanTarget(in Target target, ref TargetModifiers modifiers, ref string cursor)
			{
				if (target.Type != TargetType.Terrain)
					return false;

				var location = Actor.World.Map.CellContaining(target.CenterPosition);
				if (!Actor.World.Map.Contains(location))
					return false;

				cursor = this.cursor;

				IsQueued = modifiers.HasModifier(TargetModifiers.ForceQueue);

				return modifiers.HasModifier(TargetModifiers.ForceAttack);
			}

			public bool IsQueued { get; protected set; }
		}
	}
}
