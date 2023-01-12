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
using OpenRA.Graphics;
using OpenRA.Mods.Common.Graphics;
using OpenRA.Primitives;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Traits.Render
{
	[Desc("Default trait for rendering sprite-based actors.")]
	public class WithSpriteBodyInfo : PausableConditionalTraitInfo, IRenderActorPreviewSpritesInfo, Requires<RenderSpritesInfo>
	{
		[SequenceReference]
		[Desc("Animation to play when the actor is created.")]
		public readonly string StartSequence = null;

		[SequenceReference]
		[Desc("Animation to play when the actor is idle.")]
		public readonly string Sequence = "idle";

		[Desc("Identifier used to assign modifying traits to this sprite body.")]
		public readonly string Name = "body";

		[Desc("Forces sprite body to be rendered on ground regardless of actor altitude (for example for custom shadow sprites).")]
		public readonly bool ForceToGround = false;

		[PaletteReference(nameof(IsPlayerPalette))]
		[Desc("Custom palette name.")]
		public readonly string Palette = null;

		[Desc("Palette is a player palette BaseName.")]
		public readonly bool IsPlayerPalette = false;

		public override object Create(ActorInitializer init) { return new WithSpriteBody(init, this); }

		public virtual IEnumerable<IActorPreview> RenderPreviewSprites(ActorPreviewInitializer init, string image, int facings, PaletteReference p)
		{
			if (!EnabledByDefault)
				yield break;

			if (IsPlayerPalette)
				p = init.WorldRenderer.Palette(Palette + init.Get<OwnerInit>().InternalName);
			else if (Palette != null)
				p = init.WorldRenderer.Palette(Palette);

			var anim = new Animation(init.World, image);
			anim.PlayRepeating(RenderSprites.NormalizeSequence(anim, init.GetDamageState(), Sequence));

			yield return new SpriteActorPreview(anim, () => WVec.Zero, () => 0, p);
		}
	}

	public class WithSpriteBody : PausableConditionalTrait<WithSpriteBodyInfo>, INotifyDamageStateChanged, IAutoMouseBounds
	{
		public readonly Animation DefaultAnimation;
		readonly RenderSprites rs;
		readonly Animation boundsAnimation;

		public WithSpriteBody(ActorInitializer init, WithSpriteBodyInfo info)
			: this(init, info, () => WAngle.Zero) { }

		protected WithSpriteBody(ActorInitializer init, WithSpriteBodyInfo info, Func<WAngle> baseFacing)
			: base(info, init.Self)
		{
			rs = Actor.Trait<RenderSprites>();

			Func<bool> paused = () => IsTraitPaused &&
				DefaultAnimation.CurrentSequence.Name == NormalizeSequence(Info.Sequence);

			Func<WVec> subtractDAT = null;
			if (info.ForceToGround)
				subtractDAT = () => new WVec(0, 0, -Actor.World.Map.DistanceAboveTerrain(Actor.CenterPosition).Length);

			DefaultAnimation = new Animation(init.World, rs.GetImage(Actor), baseFacing, paused);
			rs.Add(new AnimationWithOffset(Actor, DefaultAnimation, subtractDAT, () => IsTraitDisabled), info.Palette, info.IsPlayerPalette);

			// Cache the bounds from the default sequence to avoid flickering when the animation changes
			boundsAnimation = new Animation(init.World, rs.GetImage(Actor), baseFacing, paused);
			boundsAnimation.PlayRepeating(info.Sequence);
		}

		public string NormalizeSequence(string sequence)
		{
			return RenderSprites.NormalizeSequence(DefaultAnimation, Actor.GetDamageState(), sequence);
		}

		protected override void TraitEnabled()
		{
			if (Info.StartSequence != null)
				PlayCustomAnimation(Info.StartSequence,
					() => DefaultAnimation.PlayRepeating(NormalizeSequence(Info.Sequence)));
			else
				DefaultAnimation.PlayRepeating(NormalizeSequence(Info.Sequence));
		}

		public virtual void PlayCustomAnimation(string name, Action after = null)
		{
			DefaultAnimation.PlayThen(NormalizeSequence(name), () =>
			{
				CancelCustomAnimation();
				after?.Invoke();
			});
		}

		public virtual void PlayCustomAnimationRepeating(string name)
		{
			DefaultAnimation.PlayRepeating(NormalizeSequence(name));
		}

		public virtual void PlayCustomAnimationBackwards(string name, Action after = null)
		{
			DefaultAnimation.PlayBackwardsThen(NormalizeSequence(name), () =>
			{
				CancelCustomAnimation();
				after?.Invoke();
			});
		}

		public virtual void CancelCustomAnimation()
		{
			DefaultAnimation.PlayRepeating(NormalizeSequence(Info.Sequence));
		}

		protected virtual void DamageStateChanged()
		{
			if (DefaultAnimation.CurrentSequence != null)
				DefaultAnimation.ReplaceAnim(NormalizeSequence(DefaultAnimation.CurrentSequence.Name));
		}

		void INotifyDamageStateChanged.DamageStateChanged(AttackInfo e)
		{
			DamageStateChanged();
		}

		Rectangle IAutoMouseBounds.AutoMouseoverBounds(WorldRenderer wr)
		{
			return boundsAnimation.ScreenBounds(wr, Actor.CenterPosition, WVec.Zero);
		}
	}
}
