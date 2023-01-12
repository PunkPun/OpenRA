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
	[Desc("Add to a building to expose a move cursor that triggers Transforms and issues a move order to the transformed actor.")]
	public class TransformsIntoAircraftInfo : ConditionalTraitInfo, Requires<TransformsInfo>
	{
		[Desc("Can the actor be ordered to move in to shroud?")]
		public readonly bool MoveIntoShroud = true;

		[ActorReference]
		[FieldLoader.Require]
		public readonly HashSet<string> DockActors = new HashSet<string> { };

		[VoiceReference]
		public readonly string Voice = "Action";

		[Desc("Require the force-move modifier to display the move cursor.")]
		public readonly bool RequiresForceMove = false;

		[CursorReference]
		[Desc("Cursor to display when a move order can be issued at target location.")]
		public readonly string Cursor = "move";

		[CursorReference]
		[Desc("Cursor to display when a move order cannot be issued at target location.")]
		public readonly string BlockedCursor = "move-blocked";

		[CursorReference]
		[Desc("Cursor to display when able to land at target building.")]
		public readonly string EnterCursor = "enter";

		[CursorReference]
		[Desc("Cursor to display when unable to land at target building.")]
		public readonly string EnterBlockedCursor = "enter-blocked";

		[Desc("Color to use for the target line for regular move orders.")]
		public readonly Color TargetLineColor = Color.Green;

		public override object Create(ActorInitializer init) { return new TransformsIntoAircraft(init, this); }
	}

	public class TransformsIntoAircraft : ConditionalTrait<TransformsIntoAircraftInfo>, IIssueOrder, IResolveOrder, IOrderVoice
	{
		Transforms[] transforms;

		public TransformsIntoAircraft(ActorInitializer init, TransformsIntoAircraftInfo info)
			: base(info, init.Self) { }

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
				{
					yield return new EnterAlliedActorTargeter<BuildingInfo>(
						Actor,
						"Enter",
						5,
						Info.EnterCursor,
						Info.EnterBlockedCursor,
						AircraftCanEnter,
						target => Reservable.IsAvailableFor(target, Actor));

					yield return new AircraftMoveOrderTargeter(this);
				}
			}
		}

		public bool AircraftCanEnter(Actor a, TargetModifiers modifiers)
		{
			if (Info.RequiresForceMove && !modifiers.HasModifier(TargetModifiers.ForceMove))
				return false;

			return AircraftCanEnter(a);
		}

		public bool AircraftCanEnter(Actor a)
		{
			return !Actor.AppearsHostileTo(a) && Info.DockActors.Contains(a.Info.Name);
		}

		// Note: Returns a valid order even if the unit can't move to the target
		Order IIssueOrder.IssueOrder(IOrderTargeter order, in Target target, bool queued)
		{
			if (order.OrderID == "Enter" || order.OrderID == "Move")
				return new Order(order.OrderID, Actor, target, queued);

			return null;
		}

		void IResolveOrder.ResolveOrder(Order order)
		{
			if (IsTraitDisabled)
				return;

			if (order.OrderString == "Move")
			{
				var cell = Actor.World.Map.Clamp(Actor.World.Map.CellContaining(order.Target.CenterPosition));
				if (!Info.MoveIntoShroud && !Actor.Owner.Shroud.IsExplored(cell))
					return;
			}
			else if (order.OrderString == "Enter")
			{
				// Enter and Repair orders are only valid for own/allied actors,
				// which are guaranteed to never be frozen.
				if (order.Target.Type != TargetType.Actor)
					return;

				if (!AircraftCanEnter(order.Target.Actor))
					return;
			}
			else
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

			switch (order.OrderString)
			{
				case "Move":
					if (!Info.MoveIntoShroud && order.Target.Type != TargetType.Invalid)
					{
						var cell = Actor.World.Map.CellContaining(order.Target.CenterPosition);
						if (!Actor.Owner.Shroud.IsExplored(cell))
							return null;
					}

					return Info.Voice;
				case "Enter":
					return Info.Voice;
				default: return null;
			}
		}

		class AircraftMoveOrderTargeter : IOrderTargeter
		{
			public readonly Actor Actor;
			readonly TransformsIntoAircraft aircraft;

			public bool TargetOverridesSelection(in Target target, List<Actor> actorsAt, CPos xy, TargetModifiers modifiers)
			{
				// Always prioritise orders over selecting other peoples actors or own actors that are already selected
				if (target.Type == TargetType.Actor && (target.Actor.Owner != Actor.Owner || Actor.World.Selection.Contains(target.Actor)))
					return true;

				return modifiers.HasModifier(TargetModifiers.ForceMove);
			}

			public AircraftMoveOrderTargeter(TransformsIntoAircraft aircraft)
			{
				Actor = aircraft.Actor;
				this.aircraft = aircraft;
			}

			public string OrderID => "Move";
			public int OrderPriority => 4;
			public bool IsQueued { get; protected set; }

			public bool CanTarget(in Target target, ref TargetModifiers modifiers, ref string cursor)
			{
				if (target.Type != TargetType.Terrain || (aircraft.Info.RequiresForceMove && !modifiers.HasModifier(TargetModifiers.ForceMove)))
					return false;

				var location = Actor.World.Map.CellContaining(target.CenterPosition);
				var explored = Actor.Owner.Shroud.IsExplored(location);
				cursor = Actor.World.Map.Contains(location) ? aircraft.Info.Cursor : aircraft.Info.BlockedCursor;

				IsQueued = modifiers.HasModifier(TargetModifiers.ForceQueue);

				if (!(Actor.CurrentActivity is Transform || aircraft.transforms.Any(t => !t.IsTraitDisabled && !t.IsTraitPaused))
					|| (!explored && !aircraft.Info.MoveIntoShroud))
					cursor = aircraft.Info.BlockedCursor;

				return true;
			}
		}
	}
}
