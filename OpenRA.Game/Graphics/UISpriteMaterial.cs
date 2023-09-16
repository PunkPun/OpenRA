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
using OpenRA.Primitives;

namespace OpenRA.Graphics
{
	public class UISpriteMaterial : IOpenRAMaterial<Vertex>
	{
		readonly IShader shader;
		readonly IGraphicsContext context;

		public UISpriteMaterial(IGraphicsContext context)
		{
			this.context = context;
			shader = context.CreateShader(new UISpriteShaderBindings());
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
			// projection we are able to send less data to the GPU.
			shader.SetVec("Scroll", scroll.X, scroll.Y);
			shader.SetVec("p1", 2f / (downscale * sheetSize.Width), 2f / (downscale * sheetSize.Height));
		}

		public void SetDepthPreview(bool enabled, float contrast, float offset)
		{
			throw new NotImplementedException();
		}

		public void SetAntialiasingPixelsPerTexel(float pxPerTx)
		{
			shader.SetVec("AntialiasPixelsPerTexel", pxPerTx);
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
