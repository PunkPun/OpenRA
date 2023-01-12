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

namespace OpenRA.Activities
{
	public class CallFunc : Activity
	{
		public CallFunc(Actor self, Action a)
			: base(self) { this.a = a; }
		public CallFunc(Actor self, Action a, bool interruptible)
			: base(self)
		{
			this.a = a;
			IsInterruptible = interruptible;
		}

		readonly Action a;

		public override bool Tick()
		{
			a.Invoke();
			return true;
		}
	}
}
