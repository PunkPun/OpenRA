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
using OpenRA.Mods.Common.Activities;
using OpenRA.Mods.Common.Orders;
using OpenRA.Primitives;
using OpenRA.Support;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Traits
{
	[Desc("This actor can enter Cargo actors.")]
	public class PassengerInfo : TraitInfo, IObservesVariablesInfo
	{
		public readonly string CargoType = null;

		[Desc("If defined, use a custom pip type defined on the transport's WithCargoPipsDecoration.CustomPipSequences list.")]
		public readonly string CustomPipType = null;

		public readonly int Weight = 1;

		[GrantedConditionReference]
		[Desc("The condition to grant to when this actor is loaded inside any transport.")]
		public readonly string CargoCondition = null;

		[ActorReference(dictionaryReference: LintDictionaryReference.Keys)]
		[Desc("Conditions to grant when this actor is loaded inside specified transport.",
			"A dictionary of [actor name]: [condition].")]
		public readonly Dictionary<string, string> CargoConditions = new Dictionary<string, string>();

		[GrantedConditionReference]
		public IEnumerable<string> LinterCargoConditions => CargoConditions.Values;

		[VoiceReference]
		public readonly string Voice = "Action";

		[Desc("Color to use for the target line.")]
		public readonly Color TargetLineColor = Color.Green;

		[ConsumedConditionReference]
		[Desc("Boolean expression defining the condition under which the regular (non-force) enter cursor is disabled.")]
		public readonly BooleanExpression RequireForceMoveCondition = null;

		[CursorReference]
		[Desc("Cursor to display when able to enter target actor.")]
		public readonly string EnterCursor = "enter";

		[CursorReference]
		[Desc("Cursor to display when unable to enter target actor.")]
		public readonly string EnterBlockedCursor = "enter-blocked";

		public override object Create(ActorInitializer init) { return new Passenger(this, init.Self); }
	}

	public class Passenger : IIssueOrder, IResolveOrder, IOrderVoice, INotifyRemovedFromWorld, INotifyEnteredCargo, INotifyExitedCargo, INotifyKilled, IObservesVariables
	{
		public readonly Actor Actor;
		public readonly PassengerInfo Info;
		public Actor Transport;
		bool requireForceMove;

		int anyCargoToken = Actor.InvalidConditionToken;
		int specificCargoToken = Actor.InvalidConditionToken;

		public Passenger(PassengerInfo info, Actor self)
		{
			Actor = self;
			Info = info;
		}

		public Cargo ReservedCargo { get; private set; }

		IEnumerable<IOrderTargeter> IIssueOrder.Orders
		{
			get
			{
				yield return new EnterAlliedActorTargeter<CargoInfo>(
					"EnterTransport",
					5,
					Info.EnterCursor,
					Info.EnterBlockedCursor,
					IsCorrectCargoType,
					CanEnter);
			}
		}

		public Order IssueOrder(IOrderTargeter order, in Target target, bool queued)
		{
			if (order.OrderID == "EnterTransport")
				return new Order(order.OrderID, Actor, target, queued);

			return null;
		}

		bool IsCorrectCargoType(Actor target, TargetModifiers modifiers)
		{
			if (requireForceMove && !modifiers.HasModifier(TargetModifiers.ForceMove))
				return false;

			return IsCorrectCargoType(target);
		}

		bool IsCorrectCargoType(Actor target)
		{
			var ci = target.Info.TraitInfo<CargoInfo>();
			return ci.Types.Contains(Info.CargoType);
		}

		bool CanEnter(Cargo cargo)
		{
			return cargo != null && cargo.HasSpace(Info.Weight);
		}

		bool CanEnter(Actor target)
		{
			return CanEnter(target.TraitOrDefault<Cargo>());
		}

		public string VoicePhraseForOrder(Order order)
		{
			if (order.OrderString != "EnterTransport")
				return null;

			if (order.Target.Type != TargetType.Actor || !CanEnter(order.Target.Actor))
				return null;

			return Info.Voice;
		}

		void INotifyEnteredCargo.OnEnteredCargo(Actor cargo)
		{
			if (anyCargoToken == Actor.InvalidConditionToken)
				anyCargoToken = Actor.GrantCondition(Info.CargoCondition);

			if (specificCargoToken == Actor.InvalidConditionToken && Info.CargoConditions.TryGetValue(cargo.Info.Name, out var specificCargoCondition))
				specificCargoToken = Actor.GrantCondition(specificCargoCondition);

			// Allow scripted / initial actors to move from the unload point back into the cell grid on unload
			// This is handled by the RideTransport activity for player-loaded cargo
			if (Actor.IsIdle)
			{
				// IMove is not used anywhere else in this trait, there is no benefit to caching it from Created.
				var move = Actor.TraitOrDefault<IMove>();
				if (move != null)
					Actor.QueueActivity(move.ReturnToCell());
			}
		}

		void INotifyExitedCargo.OnExitedCargo(Actor cargo)
		{
			if (anyCargoToken != Actor.InvalidConditionToken)
				anyCargoToken = Actor.RevokeCondition(anyCargoToken);

			if (specificCargoToken != Actor.InvalidConditionToken)
				specificCargoToken = Actor.RevokeCondition(specificCargoToken);
		}

		void IResolveOrder.ResolveOrder(Order order)
		{
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

			Actor.QueueActivity(order.Queued, new RideTransport(Actor, order.Target, Info.TargetLineColor));
			Actor.ShowTargetLines();
		}

		public bool Reserve(Actor self, Cargo cargo)
		{
			if (cargo == ReservedCargo)
				return true;

			Unreserve();
			if (!cargo.ReserveSpace(self))
				return false;

			ReservedCargo = cargo;
			return true;
		}

		void INotifyRemovedFromWorld.RemovedFromWorld() { Unreserve(); }

		public void Unreserve()
		{
			if (ReservedCargo == null)
				return;

			ReservedCargo.UnreserveSpace(Actor);
			ReservedCargo = null;
		}

		void INotifyKilled.Killed(AttackInfo e)
		{
			if (Transport == null)
				return;

			// Something killed us, but it wasn't our transport blowing up. Remove us from the cargo.
			if (!Transport.IsDead)
				Transport.Trait<Cargo>().Unload(Actor);
		}

		IEnumerable<VariableObserver> IObservesVariables.GetVariableObservers()
		{
			if (Info.RequireForceMoveCondition != null)
				yield return new VariableObserver(RequireForceMoveConditionChanged, Info.RequireForceMoveCondition.Variables);
		}

		void RequireForceMoveConditionChanged(IReadOnlyDictionary<string, int> conditions)
		{
			requireForceMove = Info.RequireForceMoveCondition.Evaluate(conditions);
		}
	}
}
