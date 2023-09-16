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

namespace OpenRA.Graphics
{
	public class SpriteRenderer : Renderer.IBatchRenderer
	{
		public const int SheetCount = 8;

		readonly Renderer renderer;
		public IOpenRAMaterial<Vertex> Material;

		Vertex[] vertices;
		readonly Sheet[] sheets = new Sheet[SheetCount];

		BlendMode currentBlend = BlendMode.Alpha;
		int nv = 0;
		int ns = 0;

		public SpriteRenderer(Renderer renderer)
		{
			this.renderer = renderer;
		}

		public void Initialise(IGraphicsContext context, IOpenRAMaterial<Vertex> material)
		{
			Material = material;
			vertices = renderer.Context.CreateVertices<Vertex>(renderer.TempBufferSize);
		}

		public void Flush()
		{
			if (nv > 0)
			{
				for (short i = 0; i < ns; i++)
				{
					Material.SetTexture(i, sheets[i].GetTexture());
					sheets[i] = null;
				}

				renderer.Context.SetBlendMode(currentBlend);
				Material.PrepareRender();

				// PERF: The renderer may choose to replace vertices with a different temporary buffer.
				renderer.DrawBatch(ref vertices, Material, nv, PrimitiveType.TriangleList);
				renderer.Context.SetBlendMode(BlendMode.None);

				nv = 0;
				ns = 0;
			}
		}

		int2 SetRenderStateForSprite(Sprite s)
		{
			renderer.CurrentBatchRenderer = this;

			if (s.BlendMode != currentBlend || nv + 6 > renderer.TempBufferSize)
				Flush();

			currentBlend = s.BlendMode;

			// Check if the sheet (or secondary data sheet) have already been mapped
			var sheet = s.Sheet;
			var sheetIndex = 0;
			for (; sheetIndex < ns; sheetIndex++)
				if (sheets[sheetIndex] == sheet)
					break;

			var secondarySheetIndex = 0;
			var ss = s as SpriteWithSecondaryData;
			if (ss != null)
			{
				var secondarySheet = ss.SecondarySheet;
				for (; secondarySheetIndex < ns; secondarySheetIndex++)
					if (sheets[secondarySheetIndex] == secondarySheet)
						break;

				// If neither sheet has been mapped both index values will be set to ns.
				// This is fine if they both reference the same texture, but if they don't
				// we must increment the secondary sheet index to the next free sampler.
				if (secondarySheetIndex == sheetIndex && secondarySheet != sheet)
					secondarySheetIndex++;
			}

			// Make sure that we have enough free samplers to map both if needed, otherwise flush
			if (Math.Max(sheetIndex, secondarySheetIndex) >= sheets.Length)
			{
				Flush();
				sheetIndex = 0;
				secondarySheetIndex = ss != null && ss.SecondarySheet != sheet ? 1 : 0;
			}

			if (sheetIndex >= ns)
			{
				sheets[sheetIndex] = sheet;
				ns++;
			}

			if (secondarySheetIndex >= ns && ss != null)
			{
				sheets[secondarySheetIndex] = ss.SecondarySheet;
				ns++;
			}

			return new int2(sheetIndex, secondarySheetIndex);
		}

		static float ResolveTextureIndex(Sprite s, PaletteReference pal)
		{
			if (pal == null)
				return 0;

			// PERF: Remove useless palette assignments for RGBA sprites
			// HACK: This is working around the limitation that palettes are defined on traits rather than on sequences,
			// and can be removed once this has been fixed
			if (s.Channel == TextureChannel.RGBA && !pal.HasColorShift)
				return 0;

			return pal.TextureIndex;
		}

		internal void DrawSprite(Sprite s, float paletteTextureIndex, in float3 location, in float3 scale, float rotation = 0f)
		{
			var samplers = SetRenderStateForSprite(s);
			Util.FastCreateQuad(vertices, location + scale * s.Offset, s, samplers, paletteTextureIndex, nv, scale * s.Size, float3.Ones,
								1f, rotation);
			nv += 6;
		}

		internal void DrawSprite(Sprite s, float paletteTextureIndex, in float3 location, float scale, float rotation = 0f)
		{
			var samplers = SetRenderStateForSprite(s);
			Util.FastCreateQuad(vertices, location + scale * s.Offset, s, samplers, paletteTextureIndex, nv, scale * s.Size, float3.Ones,
								1f, rotation);
			nv += 6;
		}

		public void DrawSprite(Sprite s, PaletteReference pal, in float3 location, float scale = 1f, float rotation = 0f)
		{
			DrawSprite(s, ResolveTextureIndex(s, pal), location, scale, rotation);
		}

		internal void DrawSprite(Sprite s, float paletteTextureIndex, in float3 location, float scale, in float3 tint, float alpha,
			float rotation = 0f)
		{
			var samplers = SetRenderStateForSprite(s);
			Util.FastCreateQuad(vertices, location + scale * s.Offset, s, samplers, paletteTextureIndex, nv, scale * s.Size, tint, alpha,
								rotation);
			nv += 6;
		}

		public void DrawSprite(Sprite s, PaletteReference pal, in float3 location, float scale, in float3 tint, float alpha,
			float rotation = 0f)
		{
			DrawSprite(s, ResolveTextureIndex(s, pal), location, scale, tint, alpha, rotation);
		}

		internal void DrawSprite(Sprite s, float paletteTextureIndex, in float3 a, in float3 b, in float3 c, in float3 d, in float3 tint, float alpha)
		{
			var samplers = SetRenderStateForSprite(s);
			Util.FastCreateQuad(vertices, a, b, c, d, s, samplers, paletteTextureIndex, tint, alpha, nv);
			nv += 6;
		}

		public void DrawVertexBuffer(IVertexBuffer<Vertex> buffer, int start, int length, PrimitiveType type, IEnumerable<Sheet> sheets, BlendMode blendMode)
		{
			short i = 0;
			foreach (var s in sheets)
			{
				if (i >= SheetCount)
					ThrowSheetOverflow(nameof(sheets));

				if (s != null)
					Material.SetTexture(i++, s.GetTexture());
			}

			renderer.Context.SetBlendMode(blendMode);
			Material.PrepareRender();
			renderer.DrawBatch(buffer, Material, start, length, type);
			renderer.Context.SetBlendMode(BlendMode.None);
		}

		// PERF: methods that throw won't be inlined by the JIT, so extract a static helper for use on hot paths
		static void ThrowSheetOverflow(string paramName)
		{
			throw new ArgumentException($"SpriteRenderer only supports {SheetCount} simultaneous textures", paramName);
		}

		// For RGBAColorRenderer
		internal void DrawRGBAVertices(Vertex[] v, BlendMode blendMode)
		{
			renderer.CurrentBatchRenderer = this;

			if (currentBlend != blendMode || nv + v.Length > renderer.TempBufferSize)
				Flush();

			currentBlend = blendMode;
			Array.Copy(v, 0, vertices, nv, v.Length);
			nv += v.Length;
		}
	}
}
