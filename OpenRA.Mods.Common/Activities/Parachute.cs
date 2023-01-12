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
using OpenRA.Mods.Common.Traits;

namespace OpenRA.Mods.Common.Activities
{
	public class Parachute : Activity
	{
		readonly IPositionable pos;
		readonly WVec fallVector;

		int groundLevel;

		public Parachute(Actor self)
			: base(self)
		{
			pos = self.TraitOrDefault<IPositionable>();

			fallVector = new WVec(0, 0, self.Info.TraitInfo<ParachutableInfo>().FallRate);
			IsInterruptible = false;
		}

		protected override void OnFirstRun()
		{
			groundLevel = Actor.World.Map.CenterOfCell(Actor.Location).Z;
			foreach (var np in Actor.TraitsImplementing<INotifyParachute>())
				np.OnParachute();
		}

		public override bool Tick()
		{
			var nextPosition = Actor.CenterPosition - fallVector;
			if (nextPosition.Z < groundLevel)
				return true;

			pos.SetCenterPosition(nextPosition);

			return false;
		}

		protected override void OnLastRun()
		{
			var centerPosition = Actor.CenterPosition;
			pos.SetPosition(centerPosition + new WVec(0, 0, groundLevel - centerPosition.Z));

			foreach (var np in Actor.TraitsImplementing<INotifyParachute>())
				np.OnLanded();
		}
	}
}
