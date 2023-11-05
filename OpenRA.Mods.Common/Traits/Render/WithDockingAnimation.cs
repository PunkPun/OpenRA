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

using System.Linq;
using OpenRA.Primitives;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Traits.Render
{
	[Desc("Replaces the default animation when actor resupplies a unit.")]
	public class WithDockingAnimationInfo : ConditionalTraitInfo, Requires<WithSpriteBodyInfo>
	{
		[Desc("Docking type. If left empty will trigger on any dock type.")]
		public readonly BitSet<DockType> Type;

		[SequenceReference]
		[Desc("Sequence name to use")]
		public readonly string Sequence = "active";

		[Desc("Which sprite body to play the animation on.")]
		public readonly string Body = "body";

		public override object Create(ActorInitializer init) { return new WithDockingAnimation(init.Self, this); }
	}

	public class WithDockingAnimation : ConditionalTrait<WithDockingAnimationInfo>, INotifyActiveDock
	{
		readonly WithSpriteBody wsb;
		bool animPlaying;

		public WithDockingAnimation(Actor self, WithDockingAnimationInfo info)
			: base(info)
		{
			wsb = self.TraitsImplementing<WithSpriteBody>().Single(w => w.Info.Name == Info.Body);
		}

		void INotifyActiveDock.ActiveDocksChanged(Actor self, Actor other, BitSet<DockType> activeTypes)
		{
			if (IsTraitDisabled || animPlaying == (activeTypes.Overlaps(Info.Type) || (activeTypes != default && Info.Type == default)))
				return;

			if (animPlaying)
			{
				animPlaying = false;
				wsb.CancelCustomAnimation(self);
			}
			else
			{
				animPlaying = true;
				wsb.PlayCustomAnimationRepeating(self, Info.Sequence);
			}
		}

		protected override void TraitDisabled(Actor self)
		{
			if (animPlaying)
			{
				animPlaying = false;
				wsb.CancelCustomAnimation(self);
			}
		}
	}
}
