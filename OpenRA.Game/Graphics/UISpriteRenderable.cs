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

using OpenRA.Primitives;

namespace OpenRA.Graphics
{
	public class UISpriteRenderable : IRenderable, IPalettedRenderable
	{
		readonly Sprite sprite;
		readonly int2 screenPos;
		readonly float scale;
		readonly float alpha;
		readonly float rotation = 0f;

		public UISpriteRenderable(Sprite sprite, WPos effectiveWorldPos, int2 screenPos, int zOffset, PaletteReference palette, float scale = 1f, float alpha = 1f, float rotation = 0f)
		{
			this.sprite = sprite;
			Pos = effectiveWorldPos;
			this.screenPos = screenPos;
			ZOffset = zOffset;
			Palette = palette;
			this.scale = scale;
			this.alpha = alpha;
			this.rotation = rotation;

			// PERF: Remove useless palette assignments for RGBA sprites
			// HACK: This is working around the fact that palettes are defined on traits rather than sequences
			// and can be removed once this has been fixed
			if (sprite.Channel == TextureChannel.RGBA && !(palette?.HasColorShift ?? false))
				Palette = null;
		}

		public bool Opaque => Palette.Palette.Opaque;

		// Does not exist in the world, so a world positions don't make sense
		public WPos Pos { get; }
		public WVec Offset => WVec.Zero;
		public bool IsDecoration => true;

		public PaletteReference Palette { get; }
		public int ZOffset { get; }

		public IPalettedRenderable WithPalette(PaletteReference newPalette) { return new UISpriteRenderable(sprite, Pos, screenPos, ZOffset, newPalette, scale, alpha, rotation); }
		public IRenderable WithZOffset(int newOffset) { return this; }
		public IRenderable OffsetBy(in WVec vec) { return this; }
		public IRenderable AsDecoration() { return this; }

		public void Render(WorldRenderer wr)
		{
			Game.Renderer.SpriteRenderer.DrawSprite(sprite, Palette, screenPos, scale, float3.Ones, alpha, rotation);
		}

		public void RenderDebugGeometry(WorldRenderer wr)
		{
			var offset = screenPos + sprite.Offset.XY;
			if (rotation == 0f)
				Game.Renderer.RgbaColorRenderer.DrawRect(offset, offset + sprite.Size.XY, 1, Color.Red);
			else
				Game.Renderer.RgbaColorRenderer.DrawPolygon(Util.RotateQuad(offset, sprite.Size, rotation), 1, Color.Red);
		}

		public Rectangle ScreenBounds(WorldRenderer wr)
		{
			var offset = screenPos + sprite.Offset;
			return Util.BoundingRectangle(offset, sprite.Size, rotation);
		}
	}
}
