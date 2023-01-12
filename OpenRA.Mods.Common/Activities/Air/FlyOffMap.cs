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
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Activities
{
	public class FlyOffMap : Activity
	{
		readonly Aircraft aircraft;
		readonly Target target;
		readonly bool hasTarget;
		int endingDelay;

		public FlyOffMap(Actor self, int endingDelay = 25)
			: base(self)
		{
			aircraft = self.Trait<Aircraft>();
			ChildHasPriority = false;
			this.endingDelay = endingDelay;
		}

		public FlyOffMap(Actor self, in Target target, int endingDelay = 25)
			: this(self, endingDelay)
		{
			this.target = target;
			hasTarget = true;
		}

		protected override void OnFirstRun()
		{
			if (hasTarget)
			{
				QueueChild(new Fly(Actor, target));
				QueueChild(new FlyForward(Actor));
				return;
			}

			// VTOLs must take off first if they're not at cruise altitude
			if (aircraft.Info.VTOL && Actor.World.Map.DistanceAboveTerrain(aircraft.CenterPosition) != aircraft.Info.CruiseAltitude)
				QueueChild(new TakeOff(Actor));

			QueueChild(new FlyForward(Actor));
		}

		public override bool Tick()
		{
			// Refuse to take off if it would land immediately again.
			if (aircraft.ForceLanding)
				Cancel();

			if (IsCanceling)
				return true;

			if (!Actor.World.Map.Contains(Actor.Location) && --endingDelay < 0)
				ChildActivity.Cancel();

			return TickChild();
		}
	}
}
