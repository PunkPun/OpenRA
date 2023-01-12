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

using System.Collections.Generic;
using System.Linq;
using OpenRA.Mods.Common.Activities;
using OpenRA.Mods.Common.Orders;
using OpenRA.Primitives;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Traits
{
	[Desc("Add to a building to expose a move cursor that triggers Transforms and issues a repair order to the transformed actor.")]
	public class TransformsIntoRepairableInfo : ConditionalTraitInfo, Requires<TransformsInfo>, Requires<IHealthInfo>
	{
		[ActorReference]
		[FieldLoader.Require]
		public readonly HashSet<string> RepairActors = new HashSet<string> { };

		[VoiceReference]
		public readonly string Voice = "Action";

		[Desc("Color to use for the target line.")]
		public readonly Color TargetLineColor = Color.Green;

		[Desc("Require the force-move modifier to display the enter cursor.")]
		public readonly bool RequiresForceMove = false;

		[CursorReference]
		[Desc("Cursor to display when able to be repaired at target actor.")]
		public readonly string EnterCursor = "enter";

		[CursorReference]
		[Desc("Cursor to display when unable to be repaired at target actor.")]
		public readonly string EnterBlockedCursor = "enter-blocked";

		public override object Create(ActorInitializer init) { return new TransformsIntoRepairable(init.Self, this); }
	}

	public class TransformsIntoRepairable : ConditionalTrait<TransformsIntoRepairableInfo>, IIssueOrder, IResolveOrder, IOrderVoice
	{
		Transforms[] transforms;
		IHealth health;

		public TransformsIntoRepairable(Actor self, TransformsIntoRepairableInfo info)
			: base(info, self) { }

		protected override void Created()
		{
			transforms = Actor.TraitsImplementing<Transforms>().ToArray();
			health = Actor.Trait<IHealth>();
			base.Created();
		}

		IEnumerable<IOrderTargeter> IIssueOrder.Orders
		{
			get
			{
				if (!IsTraitDisabled)
					yield return new EnterAlliedActorTargeter<BuildingInfo>(
						Actor,
						"Repair",
						5,
						Info.EnterCursor,
						Info.EnterBlockedCursor,
						CanRepairAt,
						_ => CanRepair());
			}
		}

		bool CanRepair()
		{
			if (!(Actor.CurrentActivity is Transform || transforms.Any(t => !t.IsTraitDisabled && !t.IsTraitPaused)))
				return false;

			return health.DamageState > DamageState.Undamaged;
		}

		bool CanRepairAt(Actor target, TargetModifiers modifiers)
		{
			if (Info.RequiresForceMove && !modifiers.HasModifier(TargetModifiers.ForceMove))
				return false;

			return CanRepairAt(target);
		}

		bool CanRepairAt(Actor target)
		{
			return Info.RepairActors.Contains(target.Info.Name);
		}

		Order IIssueOrder.IssueOrder(IOrderTargeter order, in Target target, bool queued)
		{
			if (order.OrderID == "Repair")
				return new Order(order.OrderID, Actor, target, queued);

			return null;
		}

		void IResolveOrder.ResolveOrder(Order order)
		{
			if (IsTraitDisabled || order.OrderString != "Repair")
				return;

			// Repair orders are only valid for own/allied actors,
			// which are guaranteed to never be frozen.
			if (order.Target.Type != TargetType.Actor)
				return;

			if (!CanRepairAt(order.Target.Actor) || !CanRepair())
				return;

			var currentTransform = Actor.CurrentActivity as Transform;
			var transform = transforms.FirstOrDefault(t => !t.IsTraitDisabled && !t.IsTraitPaused);
			if (transform == null && currentTransform == null)
				return;

			// Manually manage the inner activity queue
			var activity = currentTransform ?? transform.GetTransformActivity();
			if (!order.Queued)
				activity.NextActivity?.Cancel();

			activity.Queue(new IssueOrderAfterTransform(Actor, order.OrderString, order.Target, Info.TargetLineColor));

			if (currentTransform == null)
				Actor.QueueActivity(order.Queued, activity);

			Actor.ShowTargetLines();
		}

		string IOrderVoice.VoicePhraseForOrder(Order order)
		{
			return order.OrderString == "Repair" && !IsTraitDisabled && CanRepair() ? Info.Voice : null;
		}
	}
}
