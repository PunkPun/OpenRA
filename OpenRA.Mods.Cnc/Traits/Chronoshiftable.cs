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

using OpenRA.Mods.Cnc.Activities;
using OpenRA.Mods.Common;
using OpenRA.Mods.Common.Activities;
using OpenRA.Mods.Common.Traits;
using OpenRA.Primitives;
using OpenRA.Traits;

namespace OpenRA.Mods.Cnc.Traits
{
	[Desc("Can be teleported via Chronoshift power.")]
	public class ChronoshiftableInfo : ConditionalTraitInfo
	{
		[Desc("Should the actor die instead of being teleported?")]
		public readonly bool ExplodeInstead = false;

		[Desc("Types of damage that this trait causes to self when 'ExplodeInstead' is true",
			"or the return-to-origin is blocked. Leave empty for no damage types.")]
		public readonly BitSet<DamageType> DamageTypes = default;

		public readonly string ChronoshiftSound = "chrono2.aud";

		[Desc("Should the actor return to its previous location after the chronoshift wore out?")]
		public readonly bool ReturnToOrigin = true;

		[Desc("The color the bar of the 'return-to-origin' logic has.")]
		public readonly Color TimeBarColor = Color.White;

		public override void RulesetLoaded(Ruleset rules, ActorInfo ai)
		{
			if (!ai.HasTraitInfo<MobileInfo>() && !ai.HasTraitInfo<HuskInfo>())
				throw new YamlException("Chronoshiftable requires actors to have the Mobile or Husk traits.");
		}

		public override object Create(ActorInitializer init) { return new Chronoshiftable(init, this); }
	}

	public class Chronoshiftable : ConditionalTrait<ChronoshiftableInfo>, ITick, ISync, ISelectionBar,
		IDeathActorInitModifier, ITransformActorInitModifier
	{
		Actor chronosphere;
		bool killCargo;
		int duration;
		IPositionable iPositionable;

		// Return-to-origin logic
		[Sync]
		public CPos Origin;

		[Sync]
		public int ReturnTicks = 0;

		public Chronoshiftable(ActorInitializer init, ChronoshiftableInfo info)
			: base(info, init.Self)
		{
			var returnInit = init.GetOrDefault<ChronoshiftReturnInit>();
			if (returnInit != null)
			{
				ReturnTicks = returnInit.Ticks;
				duration = returnInit.Duration;
				Origin = returnInit.Origin;

				// Defer to the end of tick as the lazy value may reference an actor that hasn't been created yet
				if (returnInit.Chronosphere != null)
					init.World.AddFrameEndTask(w => chronosphere = returnInit.Chronosphere.Actor(init.World).Value);
			}
		}

		void ITick.Tick()
		{
			if (IsTraitDisabled || !Info.ReturnToOrigin || ReturnTicks <= 0)
				return;

			// Return to original location
			if (--ReturnTicks == 0)
			{
				// The Move activity is not immediately cancelled, which, combined
				// with Activity.Cancel discarding NextActivity without checking the
				// IsInterruptable flag, means that a well timed order can cancel the
				// Teleport activity queued below - an exploit / cheat of the return mechanic.
				// The Teleport activity queued below is guaranteed to either complete
				// (force-resetting the actor to the middle of the target cell) or kill
				// the actor. It is therefore safe to force-erase the Move activity to
				// work around the cancellation bug.
				// HACK: this is manipulating private internal actor state
				if (Actor.CurrentActivity is Move)
					typeof(Actor).GetProperty(nameof(ConditionalTrait<ChronoshiftableInfo>.Actor.CurrentActivity)).SetValue(Actor, null);

				// The actor is killed using Info.DamageTypes if the teleport fails
				Actor.QueueActivity(false, new Teleport(Actor, chronosphere ?? Actor, Origin, null, true, killCargo, Info.ChronoshiftSound,
					false, true, Info.DamageTypes));
			}
		}

		protected override void Created()
		{
			iPositionable = Actor.TraitOrDefault<IPositionable>();
			base.Created();
		}

		// Can't be used in synced code, except with ignoreVis.
		public virtual bool CanChronoshiftTo(CPos targetLocation)
		{
			// TODO: Allow enemy units to be chronoshifted into bad terrain to kill them
			return !IsTraitDisabled && iPositionable != null && iPositionable.CanEnterCell(targetLocation);
		}

		public virtual bool Teleport(CPos targetLocation, int duration, bool killCargo, Actor chronosphere)
		{
			if (IsTraitDisabled)
				return false;

			// Some things appear chronoshiftable, but instead they just die.
			if (Info.ExplodeInstead)
			{
				Actor.World.AddFrameEndTask(w =>
				{
					// Damage is inflicted by the chronosphere
					if (!Actor.Disposed)
						Actor.Kill(chronosphere, Info.DamageTypes);
				});
				return true;
			}

			// Set up return-to-origin info
			// If this actor is already counting down to return to
			// an existing location then we shouldn't override it
			if (ReturnTicks <= 0)
			{
				Origin = Actor.Location;
				ReturnTicks = duration;
			}

			this.duration = duration;
			this.chronosphere = chronosphere;
			this.killCargo = killCargo;

			// Set up the teleport
			Actor.QueueActivity(false, new Teleport(Actor, chronosphere, targetLocation, null, killCargo, true, Info.ChronoshiftSound));

			return true;
		}

		// Show the remaining time as a bar
		float ISelectionBar.GetValue()
		{
			if (IsTraitDisabled || !Info.ReturnToOrigin)
				return 0f;

			// Otherwise an empty bar is rendered all the time
			if (ReturnTicks == 0 || !Actor.Owner.IsAlliedWith(Actor.World.RenderPlayer))
				return 0f;

			return (float)ReturnTicks / duration;
		}

		Color ISelectionBar.GetColor() { return Info.TimeBarColor; }
		bool ISelectionBar.DisplayWhenEmpty => false;

		void ModifyActorInit(TypeDictionary init)
		{
			if (IsTraitDisabled || !Info.ReturnToOrigin || ReturnTicks <= 0)
				return;

			init.Add(new ChronoshiftReturnInit(ReturnTicks, duration, Origin, chronosphere));
		}

		void IDeathActorInitModifier.ModifyDeathActorInit(TypeDictionary init) { ModifyActorInit(init); }
		void ITransformActorInitModifier.ModifyTransformActorInit(TypeDictionary init) { ModifyActorInit(init); }
	}

	public class ChronoshiftReturnInit : CompositeActorInit, ISingleInstanceInit
	{
		public readonly int Ticks;
		public readonly int Duration;
		public readonly CPos Origin;
		public readonly ActorInitActorReference Chronosphere;

		public ChronoshiftReturnInit(int ticks, int duration, CPos origin, Actor chronosphere)
		{
			Ticks = ticks;
			Duration = duration;
			Origin = origin;
			Chronosphere = chronosphere;
		}
	}
}
