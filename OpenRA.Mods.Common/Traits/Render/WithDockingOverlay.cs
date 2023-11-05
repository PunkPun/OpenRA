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
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Traits.Render
{
	[Desc("Rendered on the dock host. Docking procedure stops while StartSequence and EndSequence are playing.")]
	public class WithDockingOverlayInfo : PausableConditionalTraitInfo, Requires<RenderSpritesInfo>, Requires<BodyOrientationInfo>
	{
		[SequenceReference]
		[Desc("Sequence to use upon beginning to dock.")]
		public readonly string StartSequence = null;

		[SequenceReference]
		[Desc("Sequence to play during repeatedly.")]
		public readonly string Sequence = null;

		[SequenceReference]
		[Desc("Sequence to use after docking has finished.")]
		public readonly string EndSequence = null;

		[Desc("Position relative to body")]
		public readonly WVec Offset = WVec.Zero;

		[PaletteReference(nameof(IsPlayerPalette))]
		[Desc("Custom palette name")]
		public readonly string Palette = null;

		[Desc("Custom palette is a player palette BaseName")]
		public readonly bool IsPlayerPalette = false;

		public override object Create(ActorInitializer init) { return new WithDockingOverlay(init.Self, this); }
	}

	public class WithDockingOverlay : PausableConditionalTrait<WithDockingOverlayInfo>
	{
		readonly Animation overlay;

		bool visible;

		public WithDockingOverlay(Actor self, WithDockingOverlayInfo info)
			: base(info)
		{
			var rs = self.Trait<RenderSprites>();
			var body = self.Trait<BodyOrientation>();

			overlay = new Animation(self.World, rs.GetImage(self), () => IsTraitPaused);

			var withOffset = new AnimationWithOffset(overlay,
				() => body.LocalToWorld(info.Offset.Rotate(body.QuantizeOrientation(self.Orientation))),
				() => !visible || IsTraitDisabled,
				p => RenderUtils.ZOffsetFromCenter(self, p, 1));

			rs.Add(withOffset, info.Palette, info.IsPlayerPalette);
		}

		public void Start(Actor self, Action after)
		{
			if (Info.StartSequence != null)
			{
				visible = true;
				overlay.PlayThen(RenderSprites.NormalizeSequence(overlay, self.GetDamageState(), Info.StartSequence), () =>
					{
						if (Info.Sequence != null)
							overlay.PlayRepeating(RenderSprites.NormalizeSequence(overlay, self.GetDamageState(), Info.Sequence));
						else
							visible = false;

						after();
					});
			}
			else
			{
				if (Info.Sequence != null)
				{
					visible = true;
					overlay.PlayRepeating(RenderSprites.NormalizeSequence(overlay, self.GetDamageState(), Info.Sequence));
				}

				after();
			}
		}

		public void End(Actor self, Action after)
		{
			if (Info.EndSequence != null)
			{
				visible = true;
				overlay.PlayThen(RenderSprites.NormalizeSequence(overlay, self.GetDamageState(), Info.EndSequence), () =>
					{
						visible = false;
						after();
					});
			}
			else
			{
				visible = false;
				after();
			}
		}
	}
}
