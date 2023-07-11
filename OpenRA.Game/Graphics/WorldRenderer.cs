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
using OpenRA.Effects;
using OpenRA.Primitives;
using OpenRA.Traits;

namespace OpenRA.Graphics
{
	public sealed class WorldRenderer : IDisposable
	{
		public static readonly Func<IRenderable, int> RenderableZPositionComparisonKey =
			r => r.Pos.Y + r.Pos.Z + r.ZOffset;

		public readonly Size TileSize;
		public readonly int TileScale;
		public readonly World World;
		public Viewport Viewport { get; }
		public readonly ITerrainLighting TerrainLighting;

		public event Action PaletteInvalidated = null;

		readonly HashSet<Actor> onScreenActors = new();
		readonly HardwarePalette palette = new();
		readonly Dictionary<string, PaletteReference> palettes = new();
		readonly IRenderTerrain terrainRenderer;
		readonly Lazy<DebugVisualizations> debugVis;
		readonly Func<string, PaletteReference> createPaletteReference;
		readonly bool enableDepthBuffer;

		readonly List<IRenderable> preparedRenderables = new();
		readonly List<IRenderable> preparedOverlayRenderables = new();
		readonly List<IRenderable> preparedAnnotationRenderables = new();

		readonly List<IRenderable> renderablesBuffer = new();

		internal WorldRenderer(ModData modData, World world)
		{
			World = world;
			TileSize = World.Map.Grid.TileSize;
			TileScale = World.Map.Grid.TileScale;
			Viewport = new Viewport(this, world.Map);

			createPaletteReference = CreatePaletteReference;

			var mapGrid = modData.Manifest.Get<MapGrid>();
			enableDepthBuffer = mapGrid.EnableDepthBuffer;

			foreach (var pal in world.TraitDict.ActorsWithTrait<ILoadsPalettes>())
				pal.Trait.LoadPalettes(this);

			foreach (var p in world.Players)
				UpdatePalettesForPlayer(p.InternalName, p.Color, false);

			palette.Initialize();

			TerrainLighting = world.WorldActor.TraitOrDefault<ITerrainLighting>();
			terrainRenderer = world.WorldActor.TraitOrDefault<IRenderTerrain>();

			debugVis = Exts.Lazy(() => world.WorldActor.TraitOrDefault<DebugVisualizations>());
		}

		public void UpdatePalettesForPlayer(string internalName, Color color, bool replaceExisting)
		{
			foreach (var pal in World.WorldActor.TraitsImplementing<ILoadsPlayerPalettes>())
				pal.LoadPlayerPalettes(this, internalName, color, replaceExisting);
		}

		PaletteReference CreatePaletteReference(string name)
		{
			var pal = palette.GetPalette(name);
			return new PaletteReference(name, palette.GetPaletteIndex(name), pal, palette);
		}

		public PaletteReference Palette(string name)
		{
			// HACK: This is working around the fact that palettes are defined on traits rather than sequences
			// and can be removed once this has been fixed.
			return string.IsNullOrEmpty(name) ? null : palettes.GetOrAdd(name, createPaletteReference);
		}

		public void AddPalette(string name, ImmutablePalette pal, bool allowModifiers = false, bool allowOverwrite = false)
		{
			if (allowOverwrite && palette.Contains(name))
				ReplacePalette(name, pal);
			else
			{
				var oldHeight = palette.Height;
				palette.AddPalette(name, pal, allowModifiers);

				if (oldHeight != palette.Height)
					PaletteInvalidated?.Invoke();
			}
		}

		public void ReplacePalette(string name, IPalette pal)
		{
			palette.ReplacePalette(name, pal);

			// Update cached PlayerReference if one exists
			if (palettes.TryGetValue(name, out var paletteReference))
				paletteReference.Palette = pal;
		}

		public void SetPaletteColorShift(string name, float hueOffset, float satOffset, float valueModifier, float minHue, float maxHue)
		{
			palette.SetColorShift(name, hueOffset, satOffset, valueModifier, minHue, maxHue);
		}

		public void Draw()
		{
			if (World.WorldActor.Disposed)
				return;

			RefreshPalette();

			var debugGeometry = debugVis.Value != null && debugVis.Value.RenderGeometry;

			debugVis.Value?.UpdateDepthBuffer();

			var bounds = Viewport.GetScissorBounds(World.Type != WorldType.Editor);
			Game.Renderer.EnableScissor(bounds);

			if (enableDepthBuffer)
				Game.Renderer.Context.EnableDepthBuffer();

			terrainRenderer?.RenderTerrain(this, Viewport);

			Game.Renderer.Flush();

			// PERF: Reuse collection to avoid allocations.
			onScreenActors.UnionWith(World.ScreenMap.RenderableActorsInBox(Viewport.TopLeft, Viewport.BottomRight));

			foreach (var actor in onScreenActors)
			{
				foreach (var r in actor.Render(this))
				{
					if (r.Opaque)
						r.Render(this);
					else
						renderablesBuffer.Add(r);

					if (debugGeometry)
						preparedRenderables.Add(r);
				}
			}

			foreach (var r in World.WorldActor.Render(this))
			{
				if (r.Opaque)
					r.Render(this);
				else
					renderablesBuffer.Add(r);

				if (debugGeometry)
					preparedRenderables.Add(r);
			}

			if (World.RenderPlayer != null)
			{
				foreach (var r in World.RenderPlayer.PlayerActor.Render(this))
				{
					if (r.Opaque)
						r.Render(this);
					else
						renderablesBuffer.Add(r);

					if (debugGeometry)
						preparedRenderables.Add(r);
				}
			}

			if (World.OrderGenerator != null)
			{
				foreach (var r in World.OrderGenerator.Render(this, World))
				{
					if (r.Opaque)
						r.Render(this);
					else
						renderablesBuffer.Add(r);

					if (debugGeometry)
						preparedRenderables.Add(r);
				}
			}

			// Unpartitioned effects
			foreach (var e in World.UnpartitionedEffects)
			{
				foreach (var r in e.Render(this))
				{
					if (r.Opaque)
						r.Render(this);
					else
						renderablesBuffer.Add(r);

					if (debugGeometry)
						preparedRenderables.Add(r);
				}
			}

			// Partitioned, currently on-screen effects
			foreach (var e in World.ScreenMap.RenderableEffectsInBox(Viewport.TopLeft, Viewport.BottomRight))
			{
				foreach (var r in e.Render(this))
				{
					if (r.Opaque)
						r.Render(this);
					else
						renderablesBuffer.Add(r);

					if (debugGeometry)
						preparedRenderables.Add(r);
				}
			}

			// Renderables must be ordered using a stable sorting algorithm to avoid flickering artefacts
			foreach (var renderable in renderablesBuffer.OrderBy(RenderableZPositionComparisonKey))
				renderable.Render(this);

			// PERF: Reuse collection to avoid allocations.
			renderablesBuffer.Clear();

			World.ApplyToActorsWithTrait<IRenderAboveShroud>((actor, trait) =>
			{
				if (!actor.IsInWorld || actor.Disposed || (trait.SpatiallyPartitionable && !onScreenActors.Contains(actor)))
					return;

				foreach (var renderable in trait.RenderAboveShroud(actor, this))
					preparedOverlayRenderables.Add(renderable);
			});

			foreach (var a in World.Selection.Actors)
			{
				if (!a.IsInWorld || a.Disposed)
					continue;

				foreach (var t in a.TraitsImplementing<IRenderAboveShroudWhenSelected>())
				{
					if (t.SpatiallyPartitionable && !onScreenActors.Contains(a))
						continue;

					foreach (var renderable in t.RenderAboveShroud(a, this))
						preparedOverlayRenderables.Add(renderable);
				}
			}

			foreach (var e in World.Effects)
			{
				if (e is not IEffectAboveShroud ea)
					continue;

				foreach (var renderable in ea.RenderAboveShroud(this))
					preparedOverlayRenderables.Add(renderable);
			}

			if (World.OrderGenerator != null)
				foreach (var renderable in World.OrderGenerator.RenderAboveShroud(this, World))
					preparedOverlayRenderables.Add(renderable);

			if (enableDepthBuffer)
				Game.Renderer.ClearDepthBuffer();

			World.ApplyToActorsWithTrait<IRenderAboveWorld>((actor, trait) =>
			{
				if (actor.IsInWorld && !actor.Disposed)
					trait.RenderAboveWorld(actor, this);
			});

			if (enableDepthBuffer)
				Game.Renderer.ClearDepthBuffer();

			World.ApplyToActorsWithTrait<IRenderShroud>((actor, trait) => trait.RenderShroud(this));

			if (enableDepthBuffer)
				Game.Renderer.Context.DisableDepthBuffer();

			Game.Renderer.DisableScissor();

			// HACK: Keep old grouping behaviour
			var groupedOverlayRenderables = preparedOverlayRenderables.GroupBy(prs => prs.GetType());
			foreach (var g in groupedOverlayRenderables)
				foreach (var r in g)
					r.Render(this);

			Game.Renderer.Flush();
		}

		public void DrawAnnotations()
		{
			var debugGeometry = debugVis.Value != null && debugVis.Value.RenderGeometry;

			Game.Renderer.EnableAntialiasingFilter();
			World.ApplyToActorsWithTrait<IRenderAnnotations>((actor, trait) =>
			{
				if (!actor.IsInWorld || actor.Disposed || (trait.SpatiallyPartitionable && !onScreenActors.Contains(actor)))
					return;

				foreach (var renderAnnotation in trait.RenderAnnotations(actor, this))
				{
					renderAnnotation.Render(this);
					if (debugGeometry)
						preparedAnnotationRenderables.Add(renderAnnotation);
				}
			});

			foreach (var a in World.Selection.Actors)
			{
				if (!a.IsInWorld || a.Disposed)
					continue;

				foreach (var t in a.TraitsImplementing<IRenderAnnotationsWhenSelected>())
				{
					if (t.SpatiallyPartitionable && !onScreenActors.Contains(a))
						continue;

					foreach (var renderAnnotation in t.RenderAnnotations(a, this))
					{
						renderAnnotation.Render(this);
						if (debugGeometry)
							preparedAnnotationRenderables.Add(renderAnnotation);
					}
				}
			}

			foreach (var e in World.Effects)
			{
				if (e is not IEffectAnnotation ea)
					continue;

				foreach (var renderAnnotation in ea.RenderAnnotation(this))
				{
					renderAnnotation.Render(this);
					if (debugGeometry)
						preparedAnnotationRenderables.Add(renderAnnotation);
				}
			}

			if (World.OrderGenerator != null)
				foreach (var renderAnnotation in World.OrderGenerator.RenderAnnotations(this, World))
				{
					renderAnnotation.Render(this);
					if (debugGeometry)
						preparedAnnotationRenderables.Add(renderAnnotation);
				}

			Game.Renderer.DisableAntialiasingFilter();

			// Engine debugging overlays
			if (debugGeometry)
			{
				for (var i = 0; i < preparedRenderables.Count; i++)
					preparedRenderables[i].RenderDebugGeometry(this);

				preparedRenderables.Clear();

				for (var i = 0; i < preparedOverlayRenderables.Count; i++)
					preparedOverlayRenderables[i].RenderDebugGeometry(this);

				for (var i = 0; i < preparedAnnotationRenderables.Count; i++)
					preparedAnnotationRenderables[i].RenderDebugGeometry(this);

				preparedAnnotationRenderables.Clear();
			}

			if (debugVis.Value != null && debugVis.Value.ScreenMap)
			{
				foreach (var r in World.ScreenMap.RenderBounds(World.RenderPlayer))
				{
					var tl = Viewport.WorldToViewPx(new float2(r.Left, r.Top));
					var br = Viewport.WorldToViewPx(new float2(r.Right, r.Bottom));
					Game.Renderer.RgbaColorRenderer.DrawRect(tl, br, 1, Color.MediumSpringGreen);
				}

				foreach (var b in World.ScreenMap.MouseBounds(World.RenderPlayer))
				{
					var points = new float2[b.Vertices.Length];
					for (var index = 0; index < b.Vertices.Length; index++)
					{
						var vertex = b.Vertices[index];
						points[index] = Viewport.WorldToViewPx(vertex).ToFloat2();
					}

					Game.Renderer.RgbaColorRenderer.DrawPolygon(points, 1, Color.OrangeRed);
				}
			}

			Game.Renderer.Flush();

			onScreenActors.Clear();
			preparedOverlayRenderables.Clear();
		}

		public void RefreshPalette()
		{
			palette.ApplyModifiers(World.WorldActor.TraitsImplementing<IPaletteModifier>());
			Game.Renderer.SetPalette(palette);
		}

		// Conversion between world and screen coordinates
		public float2 ScreenPosition(WPos pos)
		{
			return new float2((float)TileSize.Width * pos.X / TileScale, (float)TileSize.Height * (pos.Y - pos.Z) / TileScale);
		}

		public float3 Screen3DPosition(WPos pos)
		{
			// The projection from world coordinates to screen coordinates has
			// a non-obvious relationship between the y and z coordinates:
			// * A flat surface with constant y (e.g. a vertical wall) in world coordinates
			//   transforms into a flat surface with constant z (depth) in screen coordinates.
			// * Increasing the world y coordinate increases screen y and z coordinates equally.
			// * Increases the world z coordinate decreases screen y but doesn't change screen z.
			var z = pos.Y * (float)TileSize.Height / TileScale;
			return new float3((float)TileSize.Width * pos.X / TileScale, (float)TileSize.Height * (pos.Y - pos.Z) / TileScale, z);
		}

		public int2 ScreenPxPosition(WPos pos)
		{
			// Round to nearest pixel
			var px = ScreenPosition(pos);
			return new int2((int)Math.Round(px.X), (int)Math.Round(px.Y));
		}

		public float3 Screen3DPxPosition(WPos pos)
		{
			// Round to nearest pixel
			var px = Screen3DPosition(pos);
			return new float3((float)Math.Round(px.X), (float)Math.Round(px.Y), px.Z);
		}

		// For scaling vectors to pixel sizes in the model renderer
		public float3 ScreenVectorComponents(in WVec vec)
		{
			return new float3(
				(float)TileSize.Width * vec.X / TileScale,
				(float)TileSize.Height * (vec.Y - vec.Z) / TileScale,
				(float)TileSize.Height * vec.Z / TileScale);
		}

		// For scaling vectors to pixel sizes in the model renderer
		public float[] ScreenVector(in WVec vec)
		{
			var xyz = ScreenVectorComponents(vec);
			return new[] { xyz.X, xyz.Y, xyz.Z, 1f };
		}

		public int2 ScreenPxOffset(in WVec vec)
		{
			// Round to nearest pixel
			var xyz = ScreenVectorComponents(vec);
			return new int2((int)Math.Round(xyz.X), (int)Math.Round(xyz.Y));
		}

		/// <summary>
		/// Returns a position in the world that is projected to the given screen position.
		/// There are many possible world positions, and the returned value chooses the value with no elevation.
		/// </summary>
		public WPos ProjectedPosition(int2 screenPx)
		{
			return new WPos(TileScale * screenPx.X / TileSize.Width, TileScale * screenPx.Y / TileSize.Height, 0);
		}

		public void Dispose()
		{
			// HACK: Disposing the world from here violates ownership
			// but the WorldRenderer lifetime matches the disposal
			// behavior we want for the world, and the root object setup
			// is so horrible that doing it properly would be a giant mess.
			World.Dispose();

			palette.Dispose();
		}
	}
}
