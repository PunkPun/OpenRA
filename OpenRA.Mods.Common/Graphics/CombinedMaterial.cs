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
using OpenRA.Graphics;
using OpenRA.Primitives;

namespace OpenRA.Mods.Common.Graphics
{
	public class CombinedMaterial : IOpenRAMaterial<Vertex>
	{
		readonly IShader shader;
		readonly IGraphicsContext context;

		[ObjectCreator.UseCtor]
		public CombinedMaterial(IGraphicsContext context)
		{
			this.context = context;
			shader = context.CreateShader(new CombinedShaderBindings());
		}

		public IVertexBuffer<Vertex> CreateVertexBuffer(int count)
		{
			return context.CreateVertexBuffer<Vertex>(count);
		}

		public void PrepareRender()
		{
			shader.PrepareRender();
		}

		public void Bind()
		{
			shader.Bind();
		}

		public void SetTexture(short index, ITexture texture)
		{
			shader.SetTexture($"Texture{index}", texture);
		}

		public void SetPalette(ITexture palette, ITexture colorShifts)
		{
			shader.SetTexture("Palette", palette);
			shader.SetTexture("ColorShifts", colorShifts);
		}

		public void SetView(Size sheetSize, int downscale, float depthMargin, int2 scroll)
		{
			// OpenGL expects x and y coordinates to vary from [-1, 1]. We project world coordinates using
			// scale (p1) to values [0, 2] and then subtract by 1 using p2, where p stands for projection.
			// It's standard practice for shader to use a projection matrix, but as we use a orthografic
			// projection we are able to less data to the GPU.
			var width = 2f / (downscale * sheetSize.Width);
			var height = 2f / (downscale * sheetSize.Height);

			// Depth is more complicated:
			// * The OpenGL z axis is inverted (negative is closer) relative to OpenRA (positive is closer).
			// * We want to avoid clipping pixels that are behind the nominal z == y plane at the
			//   top of the map, or above the nominal z == y plane at the bottom of the map.
			//   We therefore expand the depth range by an extra margin that is calculated based on
			//   the maximum expected world height (see Renderer.InitializeDepthBuffer).
			// * Sprites can specify an additional per-pixel depth offset map, which is applied in the
			//   fragment shader. The fragment shader operates in OpenGL window coordinates, not NDC,
			//   with a depth range [0, 1] corresponding to the NDC [-1, 1]. We must therefore multiply the
			//   sprite channel value [0, 1] by 255 to find the pixel depth offset, then by our depth scale
			//   to find the equivalent NDC offset, then divide by 2 to find the window coordinate offset.
			// * If depthMargin == 0 (which indicates per-pixel depth testing is disabled) sprites that
			//   extend beyond the top of bottom edges of the screen may be pushed outside [-1, 1] and
			//   culled by the GPU. We avoid this by forcing everything into the z = 0 plane.
			var depth = depthMargin != 0f ? 2f / (downscale * (sheetSize.Height + depthMargin)) : 0;
			shader.SetVec("DepthTextureScale", 128 * depth);
			shader.SetVec("Scroll", scroll.X, scroll.Y, depthMargin != 0f ? scroll.Y : 0);
			shader.SetVec("p1", width, height, -depth);
			shader.SetVec("p2", -1, -1, depthMargin != 0f ? 1 : 0);
		}

		public void SetDepthPreview(bool enabled, float contrast, float offset)
		{
			shader.SetBool("EnableDepthPreview", enabled);
			shader.SetVec("DepthPreviewParams", contrast, offset);
		}

		public void SetAntialiasingPixelsPerTexel(float pxPerTx)
		{
			throw new NotImplementedException();
		}

		public void SetModelUniforms(
			float[] transforms, float[] lightDirection,
			float[] ambientLight, float[] diffuseLight,
			float colorPaletteTextureMidIndex, float normalsPaletteTextureMidIndex)
		{
			throw new NotImplementedException();
		}
	}
}
