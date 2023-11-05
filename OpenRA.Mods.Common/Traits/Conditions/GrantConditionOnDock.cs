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

using OpenRA.Primitives;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Traits
{
	public sealed class GrantConditionOnDockInfo : TraitInfo
	{
		[Desc("Docking type. If left empty will trigger on any dock type.")]
		public readonly BitSet<DockType> Type;

		[FieldLoader.Require]
		[GrantedConditionReference]
		[Desc("The condition to grant to self")]
		public readonly string Condition = null;

		[Desc("How long condition is applied even after undock. Use -1 for infinite.")]
		public readonly int AfterDockDuration = 0;

		public override object Create(ActorInitializer init) { return new GrantConditionOnDock(this); }
	}

	public sealed class GrantConditionOnDock : INotifyActiveDock, ITick, ISync
	{
		readonly GrantConditionOnDockInfo info;
		int token;
		int delayedtoken;

		[Sync]
		public int Duration { get; private set; }

		public GrantConditionOnDock(GrantConditionOnDockInfo info)
		{
			this.info = info;
			token = Actor.InvalidConditionToken;
			delayedtoken = Actor.InvalidConditionToken;
		}

		void INotifyActiveDock.ActiveDocksChanged(Actor self, Actor other, BitSet<DockType> activeTypes)
		{
			var animPlaying = token != Actor.InvalidConditionToken;
			if (info.Condition == null || animPlaying == (activeTypes.Overlaps(info.Type) || (activeTypes != default && info.Type == default)))
				return;

			if (animPlaying)
			{
				if (info.AfterDockDuration == 0)
					token = self.RevokeCondition(token);
				else if (info.AfterDockDuration > 0)
				{
					delayedtoken = token;
					token = Actor.InvalidConditionToken;
					Duration = info.AfterDockDuration;
				}
			}
			else
			{
				if (delayedtoken == Actor.InvalidConditionToken)
					token = self.GrantCondition(info.Condition);
				else
				{
					token = delayedtoken;
					delayedtoken = Actor.InvalidConditionToken;
				}
			}
		}

		void ITick.Tick(Actor self)
		{
			if (delayedtoken != Actor.InvalidConditionToken && --Duration <= 0)
				delayedtoken = self.RevokeCondition(delayedtoken);
		}
	}
}
