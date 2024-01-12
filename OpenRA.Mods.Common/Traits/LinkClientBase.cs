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
	public abstract class LinkClientBaseInfo : ConditionalTraitInfo, ILinkClientInfo, Requires<ILinkClientManagerInfo> { }

	public abstract class LinkClientBase<InfoType> : ConditionalTrait<InfoType>, ILinkClient, INotifyCreated where InfoType : LinkClientBaseInfo
	{
		readonly Actor self;

		public abstract BitSet<LinkType> LinkType { get; }
		public LinkClientManager LinkClientManager { get; }

		protected LinkClientBase(Actor self, InfoType info)
			: base(info)
		{
			this.self = self;
			LinkClientManager = self.TraitOrDefault<LinkClientManager>();
		}

		public virtual bool CanLink(BitSet<LinkType> type, bool forceEnter = false)
		{
			return !IsTraitDisabled && LinkType.Overlaps(type);
		}

		public virtual bool CanLinkTo(Actor hostActor, ILinkHost host, bool forceEnter = false, bool ignoreOccupancy = false)
		{
			return CanLink(host.GetLinkType, forceEnter)
				&& host.CanLink(self, this, ignoreOccupancy);
		}

		public virtual void OnLinkCreated(Actor self, Actor hostActor, ILinkHost host) { }

		public virtual bool OnLinkTick(Actor self, Actor hostActor, ILinkHost host) { return false; }

		public virtual void OnLinkRemoved(Actor self, Actor hostActor, ILinkHost host) { }
	}
}
