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
using OpenRA.Activities;
using OpenRA.Mods.Common.Traits;
using OpenRA.Primitives;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Activities
{
	public class PickupUnit : Activity
	{
		readonly Actor cargo;
		readonly Carryall carryall;
		readonly Carryable carryable;
		readonly IFacing carryableFacing;
		readonly BodyOrientation carryableBody;

		readonly int delay;
		readonly Color? targetLineColor;

		// TODO: Expose this to yaml
		readonly WDist targetLockRange = WDist.FromCells(4);

		enum PickupState { Intercept, LockCarryable, Pickup }
		PickupState state = PickupState.Intercept;

		public PickupUnit(Actor self, Actor cargo, int delay, Color? targetLineColor)
			: base(self)
		{
			this.cargo = cargo;
			this.delay = delay;
			this.targetLineColor = targetLineColor;
			carryable = cargo.Trait<Carryable>();
			carryableFacing = cargo.Trait<IFacing>();
			carryableBody = cargo.Trait<BodyOrientation>();

			carryall = self.Trait<Carryall>();

			ChildHasPriority = false;
		}

		protected override void OnFirstRun()
		{
			// The cargo might have become invalid while we were moving towards it.
			if (cargo.IsDead || carryable.IsTraitDisabled || !cargo.AppearsFriendlyTo(Actor))
				return;

			if (carryall.ReserveCarryable(Actor, cargo))
			{
				// Fly to the target and wait for it to be locked for pickup
				// These activities will be cancelled and replaced by Land once the target has been locked
				QueueChild(new Fly(Actor, Target.FromActor(cargo)));
				QueueChild(new FlyIdle(Actor, idleTurn: false));
			}
		}

		public override bool Tick()
		{
			if (cargo != carryall.Carryable)
				return true;

			if (IsCanceling)
			{
				if (carryall.State == Carryall.CarryallState.Reserved)
					carryall.UnreserveCarryable(Actor);

				// Make sure we run the TakeOff activity if we are / have landed
				if (Actor.Trait<Aircraft>().HasInfluence())
				{
					ChildHasPriority = true;
					IsInterruptible = false;
					QueueChild(new TakeOff(Actor));
					return false;
				}

				return true;
			}

			if (cargo.IsDead || carryable.IsTraitDisabled || !cargo.AppearsFriendlyTo(Actor))
			{
				carryall.UnreserveCarryable(Actor);
				return true;
			}

			// Wait until we are near the target before we try to lock it
			var distSq = (cargo.CenterPosition - Actor.CenterPosition).HorizontalLengthSquared;
			if (state == PickupState.Intercept && distSq <= targetLockRange.LengthSquared)
				state = PickupState.LockCarryable;

			if (state == PickupState.LockCarryable)
			{
				var lockResponse = carryable.LockForPickup(cargo, Actor);
				if (lockResponse == LockResponse.Failed)
					Cancel();
				else if (lockResponse == LockResponse.Success)
				{
					// Pickup position and facing are now known - swap the fly/wait activity with Land
					ChildActivity.Cancel();

					var localOffset = carryall.OffsetForCarryable(Actor, cargo).Rotate(carryableBody.QuantizeOrientation(cargo.Orientation));
					QueueChild(new Land(Actor, Target.FromActor(cargo), -carryableBody.LocalToWorld(localOffset), carryableFacing.Facing));

					// Pause briefly before attachment for visual effect
					if (delay > 0)
						QueueChild(new Wait(Actor, delay, false));

					// Remove our carryable from world
					QueueChild(new AttachUnit(Actor, cargo));
					QueueChild(new TakeOff(Actor));

					state = PickupState.Pickup;
				}
			}

			// We don't want to allow TakeOff to be cancelled
			if (ChildActivity is TakeOff)
				ChildHasPriority = true;

			// Return once we are in the pickup state and the pickup activities have completed
			return TickChild() && state == PickupState.Pickup;
		}

		public override IEnumerable<TargetLineNode> TargetLineNodes()
		{
			if (targetLineColor != null)
				yield return new TargetLineNode(Target.FromActor(cargo), targetLineColor.Value);
		}

		class AttachUnit : Activity
		{
			readonly Actor cargo;
			readonly Carryable carryable;
			readonly Carryall carryall;

			public AttachUnit(Actor self, Actor cargo)
				: base(self)
			{
				this.cargo = cargo;
				carryable = cargo.Trait<Carryable>();
				carryall = self.Trait<Carryall>();
			}

			protected override void OnFirstRun()
			{
				// The cargo might have become invalid while we were moving towards it.
				if (cargo == null || cargo.IsDead || carryable.IsTraitDisabled || !cargo.AppearsFriendlyTo(Actor))
					return;

				Actor.World.AddFrameEndTask(w =>
				{
					cargo.World.Remove(cargo);
					carryable.Attached(cargo);
					carryall.AttachCarryable(Actor, cargo);
				});
			}
		}
	}
}
