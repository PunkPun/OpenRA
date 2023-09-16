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
	public class ModelMaterial : IOpenRAMaterial<ModelVertex>
	{
		readonly IShader shader;
		readonly IGraphicsContext context;

		[ObjectCreator.UseCtor]
		public ModelMaterial(IGraphicsContext context)
		{
			this.context = context;
			shader = context.CreateShader(new ModelShaderBindings());
		}

		public IVertexBuffer<ModelVertex> CreateVertexBuffer(int count)
		{
			return context.CreateVertexBuffer<ModelVertex>(count);
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
			shader.SetTexture("DiffuseTexture", texture);
		}

		public void SetPalette(ITexture palette, ITexture colorShifts)
		{
			shader.SetTexture("Palette", palette);
		}

		public void SetView(Size sheetSize, int downscale, float depthMargin, int2 scroll)
		{
			var a = 2f / sheetSize.Width;
			var view = new[]
			{
				a, 0, 0, 0,
				0, -a, 0, 0,
				0, 0, -2 * a, 0,
				-1, 1, 0, 1
			};

			shader.SetMatrix("View", view);
		}

		public void SetDepthPreview(bool enabled, float contrast, float offset)
		{
			throw new NotImplementedException();
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
			shader.SetVec("PaletteRows", colorPaletteTextureMidIndex, normalsPaletteTextureMidIndex);
			shader.SetMatrix("TransformMatrix", transforms);
			shader.SetVec("LightDirection", lightDirection, 4);
			shader.SetVec("AmbientLight", ambientLight, 3);
			shader.SetVec("DiffuseLight", diffuseLight, 3);
		}
	}
}
