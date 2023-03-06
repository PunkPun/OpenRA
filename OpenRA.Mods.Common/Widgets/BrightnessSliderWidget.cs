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

using OpenRA.Graphics;
using OpenRA.Widgets;

namespace OpenRA.Mods.Common.Widgets
{
	public class BrightnessSliderWidget : SliderWidget
	{
		Sprite pickerSprite;

		public BrightnessSliderWidget() { }

		public BrightnessSliderWidget(BrightnessSliderWidget other)
			: base(other)
			{ }

		public override void Initialize(WidgetArgs args)
		{
			base.Initialize(args);

			pickerSprite = ChromeProvider.GetImage("lobby-bits", "huepicker");
		}

		public override void Draw()
		{
			if (!IsVisible())
				return;

			var rb = RenderBounds;

			var pos = RenderOrigin + new int2(PxFromValue(Value).Clamp(0, rb.Width - 1) - (int)pickerSprite.Size.X / 2, (rb.Height - (int)pickerSprite.Size.Y) / 2);
			Game.Renderer.RgbaSpriteRenderer.DrawSprite(pickerSprite, pos);
		}
	}
}
