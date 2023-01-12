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
using OpenRA.Primitives;
using OpenRA.Traits;

namespace OpenRA.Mods.Cnc.Traits
{
	class MineInfo : TraitInfo
	{
		public readonly BitSet<CrushClass> CrushClasses = default;
		public readonly bool AvoidFriendly = true;
		public readonly bool BlockFriendly = true;
		public readonly BitSet<CrushClass> DetonateClasses = default;

		public override object Create(ActorInitializer init) { return new Mine(this, init.Self); }
	}

	class Mine : ICrushable, INotifyCrushed
	{
		public readonly Actor Actor;
		readonly MineInfo info;

		public Mine(MineInfo info, Actor self)
		{
			Actor = self;
			this.info = info;
		}

		void INotifyCrushed.WarnCrush(Actor crusher, BitSet<CrushClass> crushClasses) { }

		void INotifyCrushed.OnCrush(Actor crusher, BitSet<CrushClass> crushClasses)
		{
			if (!info.CrushClasses.Overlaps(crushClasses))
				return;

			if (crusher.Info.HasTraitInfo<MineImmuneInfo>() || (Actor.Owner.RelationshipWith(crusher.Owner) == PlayerRelationship.Ally && info.AvoidFriendly))
				return;

			var mobile = crusher.TraitOrDefault<Mobile>();
			if (mobile != null && !info.DetonateClasses.Overlaps(mobile.Info.LocomotorInfo.Crushes))
				return;

			Actor.Kill(crusher, mobile != null ? mobile.Info.LocomotorInfo.CrushDamageTypes : default);
		}

		bool ICrushable.CrushableBy(Actor crusher, BitSet<CrushClass> crushClasses)
		{
			if (info.BlockFriendly && !crusher.Info.HasTraitInfo<MineImmuneInfo>() && Actor.Owner.RelationshipWith(crusher.Owner) == PlayerRelationship.Ally)
				return false;

			return info.CrushClasses.Overlaps(crushClasses);
		}

		LongBitSet<PlayerBitMask> ICrushable.CrushableBy(BitSet<CrushClass> crushClasses)
		{
			if (!info.CrushClasses.Overlaps(crushClasses))
				return Actor.World.NoPlayersMask;

			// Friendly units should move around!
			return info.BlockFriendly ? Actor.World.AllPlayersMask.Except(Actor.Owner.AlliedPlayersMask) : Actor.World.AllPlayersMask;
		}
	}

	[Desc("Tag trait for stuff that should not trigger mines.")]
	class MineImmuneInfo : TraitInfo<MineImmune> { }
	class MineImmune { }
}
