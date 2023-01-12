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
using System.Linq;
using OpenRA.Mods.Common.Orders;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Traits
{
	static class PrimaryExts
	{
		public static bool IsPrimaryBuilding(this Actor a)
		{
			var pb = a.TraitOrDefault<PrimaryBuilding>();
			return pb != null && pb.IsPrimary;
		}
	}

	[Desc("Used together with ClassicProductionQueue.")]
	public class PrimaryBuildingInfo : ConditionalTraitInfo
	{
		[GrantedConditionReference]
		[Desc("The condition to grant to self while this is the primary building.")]
		public readonly string PrimaryCondition = null;

		[NotificationReference("Speech")]
		[Desc("Speech notification to play when selecting a primary building.")]
		public readonly string SelectionNotification = null;

		[Desc("Text notification to display when selecting a primary building.")]
		public readonly string SelectionTextNotification = null;

		[Desc("List of production queues for which the primary flag should be set.",
			"If empty, the list given in the `Produces` property of the `" + nameof(Production) + "` trait will be used.")]
		public readonly string[] ProductionQueues = Array.Empty<string>();

		[CursorReference]
		[Desc("Cursor to display when setting the primary building.")]
		public readonly string Cursor = "deploy";

		public override object Create(ActorInitializer init) { return new PrimaryBuilding(init.Self, this); }
	}

	public class PrimaryBuilding : ConditionalTrait<PrimaryBuildingInfo>, IIssueOrder, IResolveOrder
	{
		const string OrderID = "PrimaryProducer";

		int primaryToken = Actor.InvalidConditionToken;

		public bool IsPrimary { get; private set; }

		public PrimaryBuilding(Actor self, PrimaryBuildingInfo info)
			: base(info, self) { }

		IEnumerable<IOrderTargeter> IIssueOrder.Orders
		{
			get
			{
				if (IsTraitDisabled)
					yield break;

				yield return new DeployOrderTargeter(Actor, OrderID, 1, () => Info.Cursor);
			}
		}

		Order IIssueOrder.IssueOrder(IOrderTargeter order, in Target target, bool queued)
		{
			if (order.OrderID == OrderID)
				return new Order(order.OrderID, Actor, false);

			return null;
		}

		void IResolveOrder.ResolveOrder(Order order)
		{
			if (order.OrderString == OrderID)
				SetPrimaryProducer(!IsPrimary);

			if (RallyPoint.IsForceSet(order) && !IsPrimary)
				SetPrimaryProducer(true);
		}

		public void SetPrimaryProducer(bool isPrimary)
		{
			IsPrimary = isPrimary;

			if (isPrimary)
			{
				// Cancel existing primaries
				// TODO: THIS IS SHIT
				var queues = Info.ProductionQueues.Length == 0 ? Actor.TraitsImplementing<Production>()
					.Where(t => !t.IsTraitDisabled).SelectMany(pi => pi.Info.Produces) : Info.ProductionQueues;
				foreach (var q in queues)
				{
					foreach (var b in Actor.World
							.ActorsWithTrait<PrimaryBuilding>()
							.Where(a =>
								a.Actor != Actor &&
								a.Actor.Owner == Actor.Owner &&
								a.Trait.IsPrimary &&
								a.Actor.TraitsImplementing<Production>().Where(p => !p.IsTraitDisabled).Any(pi => pi.Info.Produces.Contains(q))))
						b.Trait.SetPrimaryProducer(false);
				}

				if (primaryToken == Actor.InvalidConditionToken)
					primaryToken = Actor.GrantCondition(Info.PrimaryCondition);

				Game.Sound.PlayNotification(Actor.World.Map.Rules, Actor.Owner, "Speech", Info.SelectionNotification, Actor.Owner.Faction.InternalName);
				TextNotificationsManager.AddTransientLine(Info.SelectionTextNotification, Actor.Owner);
			}
			else if (primaryToken != Actor.InvalidConditionToken)
				primaryToken = Actor.RevokeCondition(primaryToken);
		}

		protected override void TraitEnabled() { }

		protected override void TraitDisabled()
		{
			if (IsPrimary)
				SetPrimaryProducer(!IsPrimary);
		}
	}
}
