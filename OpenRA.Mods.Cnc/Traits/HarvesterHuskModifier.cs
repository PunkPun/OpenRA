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

using OpenRA.Mods.Common.Traits;
using OpenRA.Traits;

namespace OpenRA.Mods.Cnc.Traits
{
	public class HarvesterHuskModifierInfo : TraitInfo, Requires<HarvesterInfo>
	{
		[ActorReference]
		public readonly string FullHuskActor = null;
		public readonly int FullnessThreshold = 50;

		public override object Create(ActorInitializer init) { return new HarvesterHuskModifier(this, init.Self); }
	}

	public class HarvesterHuskModifier : IHuskModifier
	{
		public readonly Actor Actor;
		readonly HarvesterHuskModifierInfo info;
		public HarvesterHuskModifier(HarvesterHuskModifierInfo info, Actor self)
		{
			Actor = self;
			this.info = info;
		}

		public string HuskActor()
		{
			return Actor.Trait<Harvester>().Fullness > info.FullnessThreshold ? info.FullHuskActor : null;
		}
	}
}
