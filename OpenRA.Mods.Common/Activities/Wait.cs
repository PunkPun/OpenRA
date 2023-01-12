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
using OpenRA.Activities;

namespace OpenRA.Mods.Common.Activities
{
	public class Wait : Activity
	{
		int remainingTicks;

		public Wait(Actor self, int period)
			: base(self) { remainingTicks = period; }
		public Wait(Actor self, int period, bool interruptible)
			: base(self)
		{
			remainingTicks = period;
			IsInterruptible = interruptible;
		}

		public override bool Tick()
		{
			if (IsCanceling)
				return true;

			return remainingTicks-- == 0;
		}
	}

	public class WaitFor : Activity
	{
		readonly Func<bool> f;

		public WaitFor(Actor self, Func<bool> f)
			: base(self) { this.f = f; }
		public WaitFor(Actor self, Func<bool> f, bool interruptible)
			: base(self)
		{
			this.f = f;
			IsInterruptible = interruptible;
		}

		public override bool Tick()
		{
			if (IsCanceling)
				return true;

			return f == null || f();
		}
	}
}
