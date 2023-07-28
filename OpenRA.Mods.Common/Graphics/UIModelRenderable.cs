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
	public class UIModelRenderable : IRenderable, IPalettedRenderable
	{
		readonly IEnumerable<ModelAnimation> models;
		readonly int2 screenPos;
		readonly WRot camera;
		readonly WRot lightSource;
		readonly float[] lightAmbientColor;
		readonly float[] lightDiffuseColor;
		readonly PaletteReference normalsPalette;
		readonly PaletteReference shadowPalette;
		readonly float scale;

		public UIModelRenderable(
			IEnumerable<ModelAnimation> models, WPos effectiveWorldPos, int2 screenPos, int zOffset,
			in WRot camera, float scale, in WRot lightSource, float[] lightAmbientColor, float[] lightDiffuseColor,
			PaletteReference color, PaletteReference normals, PaletteReference shadow)
		{
			this.models = models;
			Pos = effectiveWorldPos;
			this.screenPos = screenPos;
			ZOffset = zOffset;
			this.scale = scale;
			this.camera = camera;
			this.lightSource = lightSource;
			this.lightAmbientColor = lightAmbientColor;
			this.lightDiffuseColor = lightDiffuseColor;
			Palette = color;
			normalsPalette = normals;
			shadowPalette = shadow;
		}

		public WPos Pos { get; }
		public PaletteReference Palette { get; }
		public int ZOffset { get; }
		public bool IsDecoration => false;

		public IPalettedRenderable WithPalette(PaletteReference newPalette)
		{
			return new UIModelRenderable(
				models, Pos, screenPos, ZOffset, camera, scale,
				lightSource, lightAmbientColor, lightDiffuseColor,
				newPalette, normalsPalette, shadowPalette);
		}

		public IRenderable WithZOffset(int newOffset) { return this; }
		public IRenderable OffsetBy(in WVec vec) { return this; }
		public IRenderable AsDecoration() { return this; }

		public void Render(WorldRenderer wr) { }

		public void RenderDebugGeometry(WorldRenderer wr) { }

		public Rectangle ScreenBounds(WorldRenderer wr)
		{
			return Screen3DBounds(wr).Bounds;
		}

		static readonly uint[] CornerXIndex = { 0, 0, 0, 0, 3, 3, 3, 3 };
		static readonly uint[] CornerYIndex = { 1, 1, 4, 4, 1, 1, 4, 4 };
		static readonly uint[] CornerZIndex = { 2, 5, 2, 5, 2, 5, 2, 5 };
		(Rectangle Bounds, float2 Z) Screen3DBounds(WorldRenderer wr)
		{
			var pxOrigin = screenPos;
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
