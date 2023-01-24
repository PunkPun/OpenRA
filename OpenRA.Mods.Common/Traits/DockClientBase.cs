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
	public abstract class DockClientBaseInfo : ConditionalTraitInfo, IDockClientInfo, Requires<DockClientManagerInfo> { }

	public abstract class DockClientBase<InfoType> : ConditionalTrait<InfoType>, IDockClient, INotifyCreated where InfoType : DockClientBaseInfo
	{
		readonly Actor self;
		readonly DockClientManager dockManager;

		public abstract BitSet<DockType> GetDockType { get; }

		public DockClientBase(Actor self, InfoType info)
			: base(info)
		{
			this.self = self;
			dockManager = self.Trait<DockClientManager>();
		}

		public bool IsEnabledAndInWorld => !IsTraitDisabled && !self.IsDead && self.IsInWorld;
		public DockClientManager DockClientManager => dockManager;

		protected virtual bool CanDock()
		{
			return !IsTraitDisabled;
		}

		public virtual bool DockingPossible(BitSet<DockType> type)
		{
			return CanDock() && GetDockType.Overlaps(type);
		}

		public virtual bool CanDockAt(Actor hostActor, DockHost host, bool allowedToForceEnter)
		{
			return DockingPossible(host.GetDockType) && host.CanDock(self, this, allowedToForceEnter);
		}

		public virtual void DockStarted(Actor self, Actor hostActor, DockHost host) { }

		public virtual bool DockTick(Actor self, Actor hostActor, DockHost host) { return false; }

		public virtual void DockCompleted(Actor self, Actor hostActor, DockHost host) { }
	}
}
