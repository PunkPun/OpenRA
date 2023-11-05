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
using OpenRA.Primitives;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Traits.Render
{
	public class WithDockClientAnimationInfo : TraitInfo, Requires<WithSpriteBodyInfo>, Requires<HarvesterInfo>
	{
		[Desc("Docking type")]
		public readonly BitSet<DockType> Type;

		[SequenceReference]
		[Desc("Displayed when docking to refinery.")]
		public readonly string DockSequence = "dock";

		[Desc("Which sprite body to play the animation on.")]
		public readonly string Body = "body";

		[SequenceReference]
		[Desc("Looped while unloading at refinery.")]
		public readonly string DockLoopSequence = "dock-loop";

		public override object Create(ActorInitializer init) { return new WithDockClientAnimation(init.Self, this); }
	}

	public class WithDockClientAnimation : IDockClientBody
	{
		readonly WithDockClientAnimationInfo info;
		readonly WithSpriteBody wsb;

		public WithDockClientAnimation(Actor self, WithDockClientAnimationInfo info)
		{
			this.info = info;
			wsb = self.TraitsImplementing<WithSpriteBody>().Single(w => w.Info.Name == info.Body);
		}

		BitSet<DockType> IDockClientBody.DockType => info.Type;

		void IDockClientBody.PlayDockAnimation(Actor self, Action after)
		{
			wsb.PlayCustomAnimation(self, info.DockSequence, () => { wsb.PlayCustomAnimationRepeating(self, info.DockLoopSequence); after(); });
		}

		void IDockClientBody.PlayReverseDockAnimation(Actor self, Action after)
		{
			wsb.PlayCustomAnimationBackwards(self, info.DockSequence, () => after());
		}
	}
}
