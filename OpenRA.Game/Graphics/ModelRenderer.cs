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
using System.Linq;
using System.Collections.Generic;
using OpenRA.Primitives;

namespace OpenRA.Graphics
{
	public sealed class ModelRenderer : IDisposable
	{
		// Static constants
		static readonly float2 SpritePadding = new(2, 2);
		static readonly float[] ZeroVector = new float[] { 0, 0, 0, 1 };
		static readonly float[] ZVector = new float[] { 0, 0, 1, 1 };
		static readonly float[] GroundNormal = { 0, 0, 1, 1 };

		readonly Renderer renderer;
		readonly IShader shader;

		public ModelRenderer(Renderer renderer, IShader shader)
		{
			this.renderer = renderer;
			this.shader = shader;
		}

		public void SetPalette(ITexture palette)
		{
			shader.SetTexture("Palette", palette);
		}

		public void SetViewportParams(Size sheetSize, int downscale, float depthMargin, int2 scroll)
		{
			// Calculate the scale (r1) and offset (r2) that convert from OpenRA viewport pixels
			// to OpenGL normalized device coordinates (NDC). OpenGL expects coordinates to vary from [-1, 1],
			// so we rescale viewport pixels to the range [0, 2] using r1 then subtract 1 using r2.
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
			// shader.SetVec("DepthTextureScale", 128 * depth);
			shader.SetVec("Scroll", scroll.X, scroll.Y, depthMargin != 0f ? scroll.Y : 0);
			shader.SetVec("r1", width, height, -depth);
			shader.SetVec("r2", -1, -1, depthMargin != 0f ? 1 : 0);

			var a = 2f / renderer.SheetSize;
			var view = new[]
			{
				a, 0, 0, 0,
				0, -a, 0, 0,
				0, 0, -2 * a, 0,
				-1, 1, 0, 1
			};

			shader.SetMatrix("View", view);
		}

		public void RenderAsync(
			WorldRenderer wr, IEnumerable<ModelAnimation> models, in WRot camera, float scale,
			in WRot groundOrientation, in WRot lightSource, float[] lightAmbientColor, float[] lightDiffuseColor,
			PaletteReference color, PaletteReference normals, PaletteReference shadowPalette, WPos pos, float alpha, float3 tint, TintModifiers tintModifiers)
		{
			scale *= 1000;

			var location = wr.Screen3DPxPosition(pos);

			// Correct for inverted y-axis
			var scaleTransform = Util.ScaleMatrix(scale, scale, scale);

			// Correct for bogus light source definition
			var lightYaw = Util.MakeFloatMatrix(new WRot(WAngle.Zero, WAngle.Zero, -lightSource.Yaw).AsMatrix());
			var lightPitch = Util.MakeFloatMatrix(new WRot(WAngle.Zero, -lightSource.Pitch, WAngle.Zero).AsMatrix());
			var ground = Util.MakeFloatMatrix(groundOrientation.AsMatrix());
			var shadowTransform = Util.MatrixMultiply(Util.MatrixMultiply(lightPitch, lightYaw), Util.MatrixInverse(ground));

			var groundNormal = Util.MatrixVectorMultiply(ground, GroundNormal);

			var invShadowTransform = Util.MatrixInverse(shadowTransform);
			var cameraTransform = Util.MakeFloatMatrix(camera.AsMatrix());
			var invCameraTransform = Util.MatrixInverse(cameraTransform);
			if (invCameraTransform == null)
				throw new InvalidOperationException("Failed to invert the cameraTransform matrix during RenderAsync.");

			// Sprite rectangle
			var tl = new float2(float.MaxValue, float.MaxValue);
			var br = new float2(float.MinValue, float.MinValue);

			// Shadow sprite rectangle
			var stl = new float2(float.MaxValue, float.MaxValue);
			var sbr = new float2(float.MinValue, float.MinValue);

			foreach (var m in models)
			{
				// Convert screen offset back to world coords
				var offsetVec = Util.MatrixVectorMultiply(invCameraTransform, wr.ScreenVector(m.OffsetFunc()));
				var offsetTransform = Util.TranslationMatrix(offsetVec[0], offsetVec[1], offsetVec[2]);

				var worldTransform = Util.MakeFloatMatrix(m.RotationFunc().AsMatrix());
				worldTransform = Util.MatrixMultiply(scaleTransform, worldTransform);
				worldTransform = Util.MatrixMultiply(offsetTransform, worldTransform);

				var bounds = m.Model.Bounds(m.FrameFunc());
				var worldBounds = Util.MatrixAABBMultiply(worldTransform, bounds);
				var screenBounds = Util.MatrixAABBMultiply(cameraTransform, worldBounds);

				// Aggregate bounds rects
				tl = float2.Min(tl, new float2(screenBounds[0], screenBounds[1]));
				br = float2.Max(br, new float2(screenBounds[3], screenBounds[4]));
			}

			// Inflate rects to ensure rendering is within bounds
			tl -= SpritePadding;
			br += SpritePadding;
			stl -= SpritePadding;
			sbr += SpritePadding;

			// Corners of the shadow quad, in shadow-space
			var corners = new float[][]
			{
				new[] { stl.X, stl.Y, 0, 1 },
				new[] { sbr.X, sbr.Y, 0, 1 },
				new[] { sbr.X, stl.Y, 0, 1 },
				new[] { stl.X, sbr.Y, 0, 1 }
			};

			foreach (var m in models)
			{
				// Convert screen offset to world offset
				var offsetVec = Util.MatrixVectorMultiply(invCameraTransform, wr.ScreenVector(m.OffsetFunc()));
				var offsetTransform = Util.TranslationMatrix(offsetVec[0], offsetVec[1], offsetVec[2]);

				var rotations = Util.MakeFloatMatrix(m.RotationFunc().AsMatrix());
				var worldTransform = Util.MatrixMultiply(scaleTransform, rotations);
				worldTransform = Util.MatrixMultiply(offsetTransform, worldTransform);

				var transform = Util.MatrixMultiply(cameraTransform, worldTransform);
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
					shader.SetVec("PaletteRows", color.TextureMidIndex, normals.TextureMidIndex);
					shader.SetMatrix("TransformMatrix", Util.MatrixMultiply(transform, t));
					shader.SetVec("LightDirection", lightDirection, 4);
					shader.SetVec("AmbientLight", lightAmbientColor, 3);
					shader.SetVec("DiffuseLight", lightDiffuseColor, 3);
					shader.SetVec("MyLocation", location.X, location.Y, location.Z);

					shader.PrepareRender();
					renderer.DrawBatch(wr.World.ModelCache.VertexBuffer, rd.Start, rd.Count, PrimitiveType.TriangleList);
				}

				Game.Renderer.Flush();
			}

			// var wrsr = Game.Renderer.WorldRgbaSpriteRenderer;
			// if (wr.TerrainLighting != null && (tintModifiers & TintModifiers.IgnoreWorldTint) == 0)
			// 	tint *= wr.TerrainLighting.TintAt(pos);

			// // Shader interprets negative alpha as a flag to use the tint colour directly instead of multiplying the sprite colour
			// var a = alpha;
			// if ((tintModifiers & TintModifiers.ReplaceColor) != 0)
			// 	a *= -1;

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

		public void Dispose() { }
	}
}
