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
using OpenRA.Primitives;

namespace OpenRA.Mods.Common.Graphics
{
	public class ModelRenderable : IPalettedRenderable, IModifyableRenderable
	{
		readonly IEnumerable<ModelAnimation> models;
		readonly WRot camera;
		readonly WRot lightSource;
		readonly float[] lightAmbientColor;
		readonly float[] lightDiffuseColor;
		readonly PaletteReference normalsPalette;
		readonly PaletteReference shadowPalette;
		readonly float scale;

		public ModelRenderable(
			IEnumerable<ModelAnimation> models, WPos pos, int zOffset, in WRot camera, float scale,
			in WRot lightSource, float[] lightAmbientColor, float[] lightDiffuseColor,
			PaletteReference color, PaletteReference normals, PaletteReference shadow)
			: this(models, pos, zOffset, camera, scale,
				lightSource, lightAmbientColor, lightDiffuseColor,
				color, normals, shadow, 1f,
				float3.Ones, TintModifiers.None) { }

		public ModelRenderable(
			IEnumerable<ModelAnimation> models, WPos pos, int zOffset, in WRot camera, float scale,
			in WRot lightSource, float[] lightAmbientColor, float[] lightDiffuseColor,
			PaletteReference color, PaletteReference normals, PaletteReference shadow,
			float alpha, in float3 tint, TintModifiers tintModifiers)
		{
			this.models = models;
			Pos = pos;
			ZOffset = zOffset;
			this.scale = scale;
			this.camera = camera;
			this.lightSource = lightSource;
			this.lightAmbientColor = lightAmbientColor;
			this.lightDiffuseColor = lightDiffuseColor;
			Palette = color;
			normalsPalette = normals;
			shadowPalette = shadow;
			Alpha = alpha;
			Tint = tint;
			TintModifiers = tintModifiers;
		}

		public bool Opaque => Palette.Palette.Opaque;
		public WPos Pos { get; }
		public PaletteReference Palette { get; }
		public int ZOffset { get; }
		public bool IsDecoration => false;

		public float Alpha { get; }
		public float3 Tint { get; }
		public TintModifiers TintModifiers { get; }

		public IPalettedRenderable WithPalette(PaletteReference newPalette)
		{
			return new ModelRenderable(
				models, Pos, ZOffset, camera, scale,
				lightSource, lightAmbientColor, lightDiffuseColor,
				newPalette, normalsPalette, shadowPalette, Alpha, Tint, TintModifiers);
		}

		public IRenderable WithZOffset(int newOffset)
		{
			return new ModelRenderable(
				models, Pos, newOffset, camera, scale,
				lightSource, lightAmbientColor, lightDiffuseColor,
				Palette, normalsPalette, shadowPalette, Alpha, Tint, TintModifiers);
		}

		public IRenderable OffsetBy(in WVec vec)
		{
			return new ModelRenderable(
				models, Pos + vec, ZOffset, camera, scale,
				lightSource, lightAmbientColor, lightDiffuseColor,
				Palette, normalsPalette, shadowPalette, Alpha, Tint, TintModifiers);
		}

		public IRenderable AsDecoration() { return this; }

		public IModifyableRenderable WithAlpha(float newAlpha)
		{
			return new ModelRenderable(
				models, Pos, ZOffset, camera, scale,
				lightSource, lightAmbientColor, lightDiffuseColor,
				Palette, normalsPalette, shadowPalette, newAlpha, Tint, TintModifiers);
		}

		public IModifyableRenderable WithTint(in float3 newTint, TintModifiers newTintModifiers)
		{
			return new ModelRenderable(
				models, Pos, ZOffset, camera, scale,
				lightSource, lightAmbientColor, lightDiffuseColor,
				Palette, normalsPalette, shadowPalette, Alpha, newTint, newTintModifiers);
		}

		public void Render(WorldRenderer wr)
		{
			var draw = models.Where(v => v.IsVisible);
			var map = wr.World.Map;
			var groundOrientation = map.TerrainOrientation(map.CellContaining(Pos));
			Game.Renderer.WorldModelRenderer.RenderAsync(
				wr, draw, camera, scale, groundOrientation, lightSource,
				lightAmbientColor, lightDiffuseColor,
				Palette, normalsPalette, shadowPalette, Pos, Alpha, Tint, TintModifiers);
		}

		public void RenderDebugGeometry(WorldRenderer wr) { }

		static readonly uint[] CornerXIndex = new uint[] { 0, 0, 0, 0, 3, 3, 3, 3 };
		static readonly uint[] CornerYIndex = new uint[] { 1, 1, 4, 4, 1, 1, 4, 4 };
		static readonly uint[] CornerZIndex = new uint[] { 2, 5, 2, 5, 2, 5, 2, 5 };
		static void DrawBoundsBox(WorldRenderer wr, in float3 pxPos, float[] transform, float[] bounds, float width, Color c)
		{
			var cr = Game.Renderer.RgbaColorRenderer;
			var corners = new float2[8];
			for (var i = 0; i < 8; i++)
			{
				var vec = new[] { bounds[CornerXIndex[i]], bounds[CornerYIndex[i]], bounds[CornerZIndex[i]], 1 };
				var screen = OpenRA.Graphics.Util.MatrixVectorMultiply(transform, vec);
				corners[i] = wr.Viewport.WorldToViewPx(pxPos + new float3(screen[0], screen[1], screen[2]));
			}

			// Front face
			cr.DrawPolygon(new[] { corners[0], corners[1], corners[3], corners[2] }, width, c);

			// Back face
			cr.DrawPolygon(new[] { corners[4], corners[5], corners[7], corners[6] }, width, c);

			// Horizontal edges
			cr.DrawLine(corners[0], corners[4], width, c);
			cr.DrawLine(corners[1], corners[5], width, c);
			cr.DrawLine(corners[2], corners[6], width, c);
			cr.DrawLine(corners[3], corners[7], width, c);
		}

		public Rectangle ScreenBounds(WorldRenderer wr)
		{
			return Screen3DBounds(wr).Bounds;
		}

		(Rectangle Bounds, float2 Z) Screen3DBounds(WorldRenderer wr)
		{
			var pxOrigin = wr.ScreenPosition(Pos);
			var draw = models.Where(v => v.IsVisible);
			var scaleTransform = OpenRA.Graphics.Util.ScaleMatrix(scale, scale, scale);
			var cameraTransform = OpenRA.Graphics.Util.MakeFloatMatrix(camera.AsMatrix());

			var minX = float.MaxValue;
			var minY = float.MaxValue;
			var minZ = float.MaxValue;
			var maxX = float.MinValue;
			var maxY = float.MinValue;
			var maxZ = float.MinValue;

			foreach (var v in draw)
			{
				var bounds = v.Model.Bounds(v.FrameFunc());
				var rotation = OpenRA.Graphics.Util.MakeFloatMatrix(v.RotationFunc().AsMatrix());
				var worldTransform = OpenRA.Graphics.Util.MatrixMultiply(scaleTransform, rotation);

				var pxPos = pxOrigin + wr.ScreenVectorComponents(v.OffsetFunc());
				var screenTransform = OpenRA.Graphics.Util.MatrixMultiply(cameraTransform, worldTransform);

				for (var i = 0; i < 8; i++)
				{
					var vec = new float[] { bounds[CornerXIndex[i]], bounds[CornerYIndex[i]], bounds[CornerZIndex[i]], 1 };
					var screen = OpenRA.Graphics.Util.MatrixVectorMultiply(screenTransform, vec);
					minX = Math.Min(minX, pxPos.X + screen[0]);
					minY = Math.Min(minY, pxPos.Y + screen[1]);
					minZ = Math.Min(minZ, pxPos.Z + screen[2]);
					maxX = Math.Max(maxX, pxPos.X + screen[0]);
					maxY = Math.Max(maxY, pxPos.Y + screen[1]);
					maxZ = Math.Max(minZ, pxPos.Z + screen[2]);
				}
			}

			return (Rectangle.FromLTRB((int)minX, (int)minY, (int)maxX, (int)maxY), new float2(minZ, maxZ));
		}
	}
}
