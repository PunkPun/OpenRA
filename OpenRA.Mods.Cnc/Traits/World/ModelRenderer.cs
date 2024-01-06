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
using OpenRA.Traits;

namespace OpenRA.Mods.Cnc.Traits
{
	[TraitLocation(SystemActors.World | SystemActors.EditorWorld)]
	[Desc("Render voxels")]
	public class ModelRendererInfo : TraitInfo, Requires<IModelCacheInfo>
	{
		public override object Create(ActorInitializer init) { return new ModelRenderer(this, init.Self); }
	}

	public sealed class ModelRenderer : IDisposable, IRenderer, INotifyActorDisposing
	{
		// Static constants
		static readonly float[] ZeroVector = new float[] { 0, 0, 0, 1 };
		static readonly float[] ZVector = new float[] { 0, 0, 1, 1 };
		static readonly float[] OrthographicViewMatrix = new float[]
		{
			1, 0, 0, 0,
			0, 1, 0, 0,
			0, 0, 2, 0,
			-1, -1, 0, 1
		};

		readonly Renderer renderer;
		readonly IShader shader;
		public readonly IModelCache ModelCache;

		public void SetPalette(HardwarePalette palette)
		{
			shader.SetTexture("Palette", palette.Texture);
			shader.SetVec("PaletteRows", palette.Height);
		}

		public ModelRenderer(ModelRendererInfo info, Actor self)
		{
			renderer = Game.Renderer;
			shader = renderer.CreateShader(new ModelShaderBindings());
			renderer.WorldRenderers = renderer.WorldRenderers.Append(this).ToArray();

			ModelCache = self.Trait<IModelCache>();
		}

		float[] incompletePVMatric;

		public void SetViewportParams(Size sheetSize, int downscale, float depthMargin, int2 scroll)
		{
			var depth = depthMargin != 0f ? 2f / (downscale * (sheetSize.Height + depthMargin)) : 0;

			var width = 2f / (downscale * sheetSize.Width);
			var height = 2f / (downscale * sheetSize.Height);
			var offset = Util.TranslationMatrix(-scroll.X * width - 1, -scroll.Y * height - 1, depthMargin != 0f ? scroll.Y * depth + 1 : 0);
			incompletePVMatric = Util.MatrixMultiply(offset, Util.ScaleMatrix(width, height, -depth));
		}

		public void Render(
			WorldRenderer wr, IEnumerable<ModelAnimation> models, in WRot camera, float scale,
			in WRot groundOrientation, in WRot lightSource, float[] lightAmbientColor, float[] lightDiffuseColor,
			PaletteReference color, PaletteReference normals, PaletteReference shadowPalette, float3 pos, float alpha, float3 tint)
		{
			// TODO: tint should be applied per vertex rather than as a uniform.
			var vTint = new float[] { tint.X, tint.Y, tint.Z, alpha };

			var incompleteMVPMatrix = Util.MatrixMultiply(incompletePVMatric, Util.TranslationMatrix(pos.X, pos.Y, pos.Z));
			incompleteMVPMatrix = Util.MatrixMultiply(incompleteMVPMatrix, OrthographicViewMatrix);

			var scaleTransform = Util.ScaleMatrix(scale, scale, scale);

			// Correct for bogus light source definition
			var lightYaw = Util.MakeFloatMatrix(new WRot(WAngle.Zero, WAngle.Zero, -lightSource.Yaw).AsMatrix());
			var lightPitch = Util.MakeFloatMatrix(new WRot(WAngle.Zero, -lightSource.Pitch, WAngle.Zero).AsMatrix());
			var ground = Util.MakeFloatMatrix(groundOrientation.AsMatrix());
			var shadowTransform = Util.MatrixMultiply(Util.MatrixMultiply(lightPitch, lightYaw), Util.MatrixInverse(ground));

			var invShadowTransform = Util.MatrixInverse(shadowTransform);
			var cameraTransform = Util.MakeFloatMatrix(camera.AsMatrix());
			var invCameraTransform = Util.MatrixInverse(cameraTransform);
			if (invCameraTransform == null)
				throw new InvalidOperationException("Failed to invert the cameraTransform matrix during RenderAsync.");

			var shadowScreenTransform = Util.MatrixMultiply(cameraTransform, invShadowTransform);

			foreach (var m in models)
			{
				// Convert screen offset to world offset
				var offsetVec = Util.MatrixVectorMultiply(invCameraTransform, wr.ScreenVector(m.OffsetFunc()));
				var offsetTransform = Util.TranslationMatrix(offsetVec[0], offsetVec[1], offsetVec[2]);

				var rotations = Util.MakeFloatMatrix(m.RotationFunc().AsMatrix());
				var worldTransform = Util.MatrixMultiply(scaleTransform, rotations);
				worldTransform = Util.MatrixMultiply(offsetTransform, worldTransform);

				var transform = Util.MatrixMultiply(cameraTransform, worldTransform);
				transform = Util.MatrixMultiply(incompleteMVPMatrix, transform);

				var lightTransform = Util.MatrixMultiply(Util.MatrixInverse(rotations), invShadowTransform);

				var frame = m.FrameFunc();
				for (uint i = 0; i < m.Model.Sections; i++)
				{
					var rd = m.Model.RenderData(i);
					var t = m.Model.TransformationMatrix(i, frame);
					var it = Util.MatrixInverse(t);
					if (it == null)
						throw new InvalidOperationException($"Failed to invert the transformed matrix of frame {i} during RenderAsync.");

					// Transform light vector from shadow -> world -> limb coords
					var lightDirection = ExtractRotationVector(Util.MatrixMultiply(it, lightTransform));

					shader.SetTexture("DiffuseTexture", rd.Sheet.GetTexture());
					shader.SetVec("Palettes", color.TextureIndex, normals.TextureIndex);
					shader.SetMatrix("MVP", Util.MatrixMultiply(transform, t));
					shader.SetVec("LightDirection", lightDirection, 4);
					shader.SetVec("AmbientLight", lightAmbientColor, 3);
					shader.SetVec("DiffuseLight", lightDiffuseColor, 3);
					shader.SetVec("Tint", vTint, 4);

					shader.PrepareRender();
					renderer.DrawBatch(ModelCache.VertexBuffer, shader, rd.Start, rd.Count, PrimitiveType.TriangleList);
				}

				Game.Renderer.Flush();
			}

			// wrsr.DrawSprite(sprite, pxOrigin - 0.5f * sprite.Size, 1f, tint, a);
		}

		static float[] ExtractRotationVector(float[] mtx)
		{
			var tVec = Util.MatrixVectorMultiply(mtx, ZVector);
			var tOrigin = Util.MatrixVectorMultiply(mtx, ZeroVector);
			tVec[0] -= tOrigin[0] * tVec[3] / tOrigin[3];
			tVec[1] -= tOrigin[1] * tVec[3] / tOrigin[3];
			tVec[2] -= tOrigin[2] * tVec[3] / tOrigin[3];

			// Renormalize
			var w = (float)Math.Sqrt(tVec[0] * tVec[0] + tVec[1] * tVec[1] + tVec[2] * tVec[2]);
			tVec[0] /= w;
			tVec[1] /= w;
			tVec[2] /= w;
			tVec[3] = 1f;

			return tVec;
		}

		public void Dispose()
		{
			renderer.WorldRenderers = renderer.WorldRenderers.Where(r => r != this).ToArray();
		}

		void INotifyActorDisposing.Disposing(Actor a)
		{
			Dispose();
		}
	}
}
