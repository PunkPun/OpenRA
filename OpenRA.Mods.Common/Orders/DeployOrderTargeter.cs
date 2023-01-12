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
using System.Collections.Generic;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Orders
{
	public class DeployOrderTargeter : IOrderTargeter
	{
		public readonly Actor Actor;
		readonly Func<string> cursor;

		public DeployOrderTargeter(Actor self, string order, int priority, Func<string> cursor)
		{
			Actor = self;
			OrderID = order;
			OrderPriority = priority;
			this.cursor = cursor;
		}

		public string OrderID { get; }
		public int OrderPriority { get; }
		public bool TargetOverridesSelection(in Target target, List<Actor> actorsAt, CPos xy, TargetModifiers modifiers) { return true; }

		public bool CanTarget(in Target target, ref TargetModifiers modifiers, ref string cursor)
		{
			if (target.Type != TargetType.Actor)
				return false;

			IsQueued = modifiers.HasModifier(TargetModifiers.ForceQueue);
			cursor = this.cursor();

			return Actor == target.Actor;
		}

		public bool IsQueued { get; protected set; }
	}
}
