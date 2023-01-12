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

using OpenRA.Activities;
using OpenRA.Mods.Common.Activities;
using OpenRA.Primitives;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Traits
{
	// TODO: Add CurleyShuffle (TD, TS), Circle (Generals Gunship-style)
	public enum AirAttackType { Default, Hover, Strafe }

	public class AttackAircraftInfo : AttackFollowInfo, Requires<AircraftInfo>
	{
		[Desc("Attack behavior. Currently supported types are:",
			"Default: Attack while following the default movement rules.",
			"Hover: Hover, even if the Aircraft can't hover while idle.",
			"Strafe: Perform a fixed-length attack run on the target.")]
		public readonly AirAttackType AttackType = AirAttackType.Default;

		[Desc("Distance the strafing aircraft makes to a target before turning for another pass. When set to WDist.Zero this defaults to the maximum armament range.")]
		public readonly WDist StrafeRunLength = WDist.Zero;

		public override object Create(ActorInitializer init) { return new AttackAircraft(init.Self, this); }
	}

	public class AttackAircraft : AttackFollow
	{
		public new readonly AttackAircraftInfo Info;
		readonly AircraftInfo aircraftInfo;

		public AttackAircraft(Actor self, AttackAircraftInfo info)
			: base(info, self)
		{
			Info = info;
			aircraftInfo = self.Info.TraitInfo<AircraftInfo>();
		}

		public override Activity GetAttackActivity(AttackSource source, in Target newTarget, bool allowMove, bool forceAttack, Color? targetLineColor = null)
		{
			return new FlyAttack(Actor, source, newTarget, forceAttack, targetLineColor);
		}

		protected override bool CanAttack(in Target target)
		{
			// Don't fire while landed or when outside the map.
			if (Actor.World.Map.DistanceAboveTerrain(Actor.CenterPosition).Length < aircraftInfo.MinAirborneAltitude
				|| !Actor.World.Map.Contains(Actor.Location))
				return false;

			if (!base.CanAttack(target))
				return false;

			return TargetInFiringArc(target, Info.FacingTolerance);
		}
	}
}
