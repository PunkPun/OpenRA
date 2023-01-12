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

using OpenRA.Mods.Common;
using OpenRA.Mods.Common.Activities;
using OpenRA.Mods.Common.Traits;
using OpenRA.Primitives;
using OpenRA.Traits;

namespace OpenRA.Mods.Cnc.Traits
{
	[Desc("Transform into a different actor type.")]
	class InfiltrateForTransformInfo : TraitInfo
	{
		[ActorReference]
		[FieldLoader.Require]
		public readonly string IntoActor = null;

		public readonly int ForceHealthPercentage = 0;

		public readonly bool SkipMakeAnims = true;

		[Desc("The `TargetTypes` from `Targetable` that are allowed to enter.")]
		public readonly BitSet<TargetableType> Types = default;

		public override object Create(ActorInitializer init) { return new InfiltrateForTransform(init, this); }
	}

	class InfiltrateForTransform : INotifyInfiltrated
	{
		readonly InfiltrateForTransformInfo info;
		readonly string faction;
		public readonly Actor Actor;

		public InfiltrateForTransform(ActorInitializer init, InfiltrateForTransformInfo info)
		{
			this.info = info;
			Actor = init.Self;
			faction = init.GetValue<FactionInit, string>(init.Self.Owner.Faction.InternalName);
		}

		void INotifyInfiltrated.Infiltrated(Actor infiltrator, BitSet<TargetableType> types)
		{
			if (!info.Types.Overlaps(types))
				return;

			var transform = new Transform(Actor, info.IntoActor)
			{
				ForceHealthPercentage = info.ForceHealthPercentage,
				Faction = faction,
				SkipMakeAnims = info.SkipMakeAnims
			};

			var facing = Actor.TraitOrDefault<IFacing>();
			if (facing != null)
				transform.Facing = facing.Facing;

			Actor.QueueActivity(false, transform);
		}
	}
}
