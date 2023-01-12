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
	public class TakeOff : Activity
	{
		readonly Aircraft aircraft;

		public TakeOff(Actor self)
			: base(self)
		{
			aircraft = self.Trait<Aircraft>();
		}

		protected override void OnFirstRun()
		{
			if (aircraft.ForceLanding)
				return;

			if (!aircraft.HasInfluence())
				return;

			// We are taking off, so remove influence in ground cells.
			aircraft.RemoveInfluence();

			if (Actor.World.Map.DistanceAboveTerrain(aircraft.CenterPosition).Length > aircraft.Info.MinAirborneAltitude)
				return;

			if (aircraft.Info.TakeoffSounds.Length > 0)
				Game.Sound.Play(SoundType.World, aircraft.Info.TakeoffSounds, Actor.World, aircraft.CenterPosition);

			foreach (var notify in Actor.TraitsImplementing<INotifyTakeOff>())
				notify.TakeOff();
		}

		public override bool Tick()
		{
			// Refuse to take off if it would land immediately again.
			if (aircraft.ForceLanding)
			{
				Cancel();
				return true;
			}

			var dat = Actor.World.Map.DistanceAboveTerrain(aircraft.CenterPosition);
			if (dat < aircraft.Info.CruiseAltitude)
			{
				// If we're a VTOL, rise before flying forward
				if (aircraft.Info.VTOL)
				{
					Fly.VerticalTakeOffOrLandTick(aircraft, aircraft.Facing, aircraft.Info.CruiseAltitude);
					return false;
				}

				Fly.FlyTick(aircraft, aircraft.Facing, aircraft.Info.CruiseAltitude);
				return false;
			}

			return true;
		}
	}
}
