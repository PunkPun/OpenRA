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
	[Desc("Add to a building to expose a move cursor that triggers Transforms and issues an EnterTransport order to the transformed actor.")]
	public class TransformsIntoPassengerInfo : ConditionalTraitInfo, Requires<TransformsInfo>
	{
		public readonly string CargoType = null;
		public readonly int Weight = 1;

		[VoiceReference]
		public readonly string Voice = "Action";

		[Desc("Color to use for the target line.")]
		public readonly Color TargetLineColor = Color.Green;

		[Desc("Require the force-move modifier to display the enter cursor.")]
		public readonly bool RequiresForceMove = false;

		[CursorReference]
		[Desc("Cursor to display when able to enter target actor.")]
		public readonly string EnterCursor = "enter";

		[CursorReference]
		[Desc("Cursor to display when unable to enter target actor.")]
		public readonly string EnterBlockedCursor = "enter-blocked";

		public override object Create(ActorInitializer init) { return new TransformsIntoPassenger(init.Self, this); }
	}

	public class TransformsIntoPassenger : ConditionalTrait<TransformsIntoPassengerInfo>, IIssueOrder, IResolveOrder, IOrderVoice
	{
		Transforms[] transforms;

		public TransformsIntoPassenger(Actor self, TransformsIntoPassengerInfo info)
			: base(info, self) { }

		protected override void Created()
		{
			transforms = Actor.TraitsImplementing<Transforms>().ToArray();
			base.Created();
		}

		IEnumerable<IOrderTargeter> IIssueOrder.Orders
		{
			get
			{
				if (!IsTraitDisabled)
					yield return new EnterAlliedActorTargeter<CargoInfo>(
						Actor,
						"EnterTransport",
						5,
						Info.EnterCursor,
						Info.EnterBlockedCursor,
						IsCorrectCargoType,
						CanEnter);
			}
		}

		Order IIssueOrder.IssueOrder(IOrderTargeter order, in Target target, bool queued)
		{
			if (order.OrderID == "EnterTransport")
				return new Order(order.OrderID, Actor, target, queued);

			return null;
		}

		bool IsCorrectCargoType(Actor target, TargetModifiers modifiers)
		{
			if (Info.RequiresForceMove && !modifiers.HasModifier(TargetModifiers.ForceMove))
				return false;

			return IsCorrectCargoType(target);
		}

		bool IsCorrectCargoType(Actor target)
		{
			var ci = target.Info.TraitInfo<CargoInfo>();
			return ci.Types.Contains(Info.CargoType);
		}

		bool CanEnter(Actor target)
		{
			if (!(Actor.CurrentActivity is Transform || transforms.Any(t => !t.IsTraitDisabled && !t.IsTraitPaused)))
				return false;

			var cargo = target.TraitOrDefault<Cargo>();
			return cargo != null && cargo.HasSpace(Info.Weight);
		}

		void IResolveOrder.ResolveOrder(Order order)
		{
			if (IsTraitDisabled)
				return;

			if (order.OrderString != "EnterTransport")
				return;

			// Enter orders are only valid for own/allied actors,
			// which are guaranteed to never be frozen.
			if (order.Target.Type != TargetType.Actor)
				return;

			var targetActor = order.Target.Actor;
			if (!CanEnter(targetActor))
				return;

			if (!IsCorrectCargoType(targetActor))
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
			if (IsTraitDisabled)
				return null;

			if (order.OrderString != "EnterTransport")
				return null;

			if (order.Target.Type != TargetType.Actor || !CanEnter(order.Target.Actor))
				return null;

			return Info.Voice;
		}
	}
}
