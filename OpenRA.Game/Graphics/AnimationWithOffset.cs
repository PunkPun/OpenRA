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
	public class AnimationWithOffset
	{
		public readonly Actor Actor;
		public readonly Animation Animation;
		public readonly Func<WVec> OffsetFunc;
		public readonly Func<bool> DisableFunc;
		public readonly Func<WPos, int> ZOffset;

		public AnimationWithOffset(Actor self, Animation a, Func<WVec> offset, Func<bool> disable)
			: this(self, a, offset, disable, null) { }

		public AnimationWithOffset(Actor self, Animation a, Func<WVec> offset, Func<bool> disable, int zOffset)
			: this(self, a, offset, disable, _ => zOffset) { }

		public AnimationWithOffset(Actor self, Animation a, Func<WVec> offset, Func<bool> disable, Func<WPos, int> zOffset)
		{
			Actor = self;
			Animation = a;
			OffsetFunc = offset;
			DisableFunc = disable;
			ZOffset = zOffset;
		}

		public IRenderable[] Render(PaletteReference pal)
		{
			var center = Actor.CenterPosition;
			var offset = OffsetFunc?.Invoke() ?? WVec.Zero;

			var z = ZOffset?.Invoke(center + offset) ?? 0;
			return Animation.Render(center, offset, z, pal);
		}

		public Rectangle ScreenBounds(WorldRenderer wr)
		{
			var center = Actor.CenterPosition;
			var offset = OffsetFunc?.Invoke() ?? WVec.Zero;

			return Animation.ScreenBounds(wr, center, offset);
		}

		public static implicit operator AnimationWithOffset(Actor self, Animation a)
		{
			return new AnimationWithOffset(self, a, null, null, null);
		}
	}
}
