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
using OpenRA.Mods.Common.Traits;
using OpenRA.Primitives;
using OpenRA.Traits;

namespace OpenRA.Mods.Cnc.Traits
{
	[Desc("Overrides the default Tooltip when this actor is disguised (aids in deceiving enemy players).")]
	class DisguiseTooltipInfo : TooltipInfo, Requires<DisguiseInfo>
	{
		public override object Create(ActorInitializer init) { return new DisguiseTooltip(init.Self, this); }
	}

	class DisguiseTooltip : ConditionalTrait<DisguiseTooltipInfo>, ITooltip
	{
		readonly Disguise disguise;

		public DisguiseTooltip(Actor self, DisguiseTooltipInfo info)
			: base(info, self)
		{
			disguise = self.Trait<Disguise>();
		}

		public ITooltipInfo TooltipInfo => disguise.Disguised ? disguise.AsTooltipInfo : Info;

		public Player Owner
		{
			get
			{
				if (!disguise.Disguised || Actor.Owner.IsAlliedWith(Actor.World.RenderPlayer))
					return Actor.Owner;

				return disguise.AsPlayer;
			}
		}
	}

	[Flags]
	public enum RevealDisguiseType
	{
		None = 0,
		Attack = 1,
		Damaged = 2,
		Unload = 4,
		Infiltrate = 8,
		Demolish = 16,
		Move = 32
	}

	[Desc("Provides access to the disguise command, which makes the actor appear to be another player's actor.")]
	class DisguiseInfo : TraitInfo
	{
		[VoiceReference]
		public readonly string Voice = "Action";

		[GrantedConditionReference]
		[Desc("The condition to grant to self while disguised.")]
		public readonly string DisguisedCondition = null;

		[Desc("Player relationships the owner of the disguise target needs.")]
		public readonly PlayerRelationship ValidRelationships = PlayerRelationship.Ally | PlayerRelationship.Neutral | PlayerRelationship.Enemy;

		[Desc("Target types of actors that this actor disguise as.")]
		public readonly BitSet<TargetableType> TargetTypes = new BitSet<TargetableType>("Disguise");

		[Desc("Triggers which cause the actor to drop it's disguise. Possible values: None, Attack, Damaged,",
			"Unload, Infiltrate, Demolish, Move.")]
		public readonly RevealDisguiseType RevealDisguiseOn = RevealDisguiseType.Attack;

		[ActorReference(dictionaryReference: LintDictionaryReference.Keys)]
		[Desc("Conditions to grant when disguised as specified actor.",
			"A dictionary of [actor id]: [condition].")]
		public readonly Dictionary<string, string> DisguisedAsConditions = new Dictionary<string, string>();

		[CursorReference]
		[Desc("Cursor to display when hovering over a valid actor to disguise as.")]
		public readonly string Cursor = "ability";

		[GrantedConditionReference]
		public IEnumerable<string> LinterConditions => DisguisedAsConditions.Values;

		public override object Create(ActorInitializer init) { return new Disguise(init.Self, this); }
	}

	class Disguise : IEffectiveOwner, IIssueOrder, IResolveOrder, IOrderVoice, IRadarColorModifier, INotifyAttack,
		INotifyDamage, INotifyUnload, INotifyDemolition, INotifyInfiltration, ITick
	{
		public ActorInfo AsActor { get; private set; }
		public Player AsPlayer { get; private set; }
		public ITooltipInfo AsTooltipInfo { get; private set; }

		public bool Disguised => AsPlayer != null;
		public Player Owner => AsPlayer;

		public readonly Actor Actor;
		readonly DisguiseInfo info;

		int disguisedToken = Actor.InvalidConditionToken;
		int disguisedAsToken = Actor.InvalidConditionToken;
		CPos? lastPos;

		public Disguise(Actor self, DisguiseInfo info)
		{
			Actor = self;
			this.info = info;

			AsActor = self.Info;
		}

		IEnumerable<IOrderTargeter> IIssueOrder.Orders
		{
			get
			{
				yield return new DisguiseOrderTargeter(Actor, info);
			}
		}

		Order IIssueOrder.IssueOrder(IOrderTargeter order, in Target target, bool queued)
		{
			if (order.OrderID == "Disguise")
				return new Order(order.OrderID, Actor, target, queued);

			return null;
		}

		void IResolveOrder.ResolveOrder(Order order)
		{
			if (order.OrderString == "Disguise")
			{
				var target = order.Target;
				if (target.Type == TargetType.Actor)
					DisguiseAs((target.Actor != Actor && target.Actor.IsInWorld) ? target.Actor : null);

				if (target.Type == TargetType.FrozenActor)
					DisguiseAs(target.FrozenActor.Info, target.FrozenActor.Owner);
			}
		}

		string IOrderVoice.VoicePhraseForOrder(Order order)
		{
			return order.OrderString == "Disguise" ? info.Voice : null;
		}

		Color IRadarColorModifier.RadarColorOverride(Color color)
		{
			if (!Disguised || Actor.Owner.IsAlliedWith(Actor.World.RenderPlayer))
				return color;

			return Game.Settings.Game.UsePlayerStanceColors ? AsPlayer.PlayerRelationshipColor(Actor) : AsPlayer.Color;
		}

		public void DisguiseAs(Actor target)
		{
			var oldEffectiveActor = AsActor;
			var oldEffectiveOwner = AsPlayer;
			var oldDisguiseSetting = Disguised;

			if (target != null)
			{
				// Take the image of the target's disguise, if it exists.
				// E.g., SpyA is disguised as a rifle infantry. SpyB then targets SpyA. We should use the rifle infantry image.
				var targetDisguise = target.TraitOrDefault<Disguise>();
				if (targetDisguise != null && targetDisguise.Disguised)
				{
					AsPlayer = targetDisguise.AsPlayer;
					AsActor = targetDisguise.AsActor;
					AsTooltipInfo = targetDisguise.AsTooltipInfo;
				}
				else
				{
					var tooltip = target.TraitsImplementing<ITooltip>().FirstEnabledTraitOrDefault();
					if (tooltip == null)
						throw new ArgumentNullException("tooltip", "Missing tooltip or invalid target.");

					AsPlayer = tooltip.Owner;
					AsActor = target.Info;
					AsTooltipInfo = tooltip.TooltipInfo;
				}
			}
			else
			{
				AsTooltipInfo = null;
				AsPlayer = null;
				AsActor = Actor.Info;
			}

			HandleDisguise(oldEffectiveActor, oldEffectiveOwner, oldDisguiseSetting);
		}

		public void DisguiseAs(ActorInfo actorInfo, Player newOwner)
		{
			var oldEffectiveActor = AsActor;
			var oldEffectiveOwner = AsPlayer;
			var oldDisguiseSetting = Disguised;

			AsPlayer = newOwner;
			AsActor = actorInfo;
			AsTooltipInfo = actorInfo.TraitInfos<TooltipInfo>().FirstOrDefault(info => info.EnabledByDefault);

			HandleDisguise(oldEffectiveActor, oldEffectiveOwner, oldDisguiseSetting);
		}

		void HandleDisguise(ActorInfo oldEffectiveActor, Player oldEffectiveOwner, bool oldDisguiseSetting)
		{
			foreach (var t in Actor.TraitsImplementing<INotifyEffectiveOwnerChanged>())
				t.OnEffectiveOwnerChanged(oldEffectiveOwner, AsPlayer);

			if (Disguised != oldDisguiseSetting)
			{
				if (Disguised && disguisedToken == Actor.InvalidConditionToken)
					disguisedToken = Actor.GrantCondition(info.DisguisedCondition);
				else if (!Disguised && disguisedToken != Actor.InvalidConditionToken)
					disguisedToken = Actor.RevokeCondition(disguisedToken);
			}

			if (AsActor != oldEffectiveActor)
			{
				if (disguisedAsToken != Actor.InvalidConditionToken)
					disguisedAsToken = Actor.RevokeCondition(disguisedAsToken);

				if (info.DisguisedAsConditions.TryGetValue(AsActor.Name, out var disguisedAsCondition))
					disguisedAsToken = Actor.GrantCondition(disguisedAsCondition);
			}
		}

		void INotifyAttack.PreparingAttack(in Target target, Armament a, Barrel barrel) { }

		void INotifyAttack.Attacking(in Target target, Armament a, Barrel barrel)
		{
			if (info.RevealDisguiseOn.HasFlag(RevealDisguiseType.Attack))
				DisguiseAs(null);
		}

		void INotifyDamage.Damaged(AttackInfo e)
		{
			if (info.RevealDisguiseOn.HasFlag(RevealDisguiseType.Damaged) && e.Damage.Value > 0)
				DisguiseAs(null);
		}

		void INotifyUnload.Unloading()
		{
			if (info.RevealDisguiseOn.HasFlag(RevealDisguiseType.Unload))
				DisguiseAs(null);
		}

		void INotifyDemolition.Demolishing()
		{
			if (info.RevealDisguiseOn.HasFlag(RevealDisguiseType.Demolish))
				DisguiseAs(null);
		}

		void INotifyInfiltration.Infiltrating()
		{
			if (info.RevealDisguiseOn.HasFlag(RevealDisguiseType.Infiltrate))
				DisguiseAs(null);
		}

		void ITick.Tick()
		{
			if (info.RevealDisguiseOn.HasFlag(RevealDisguiseType.Move) && lastPos != null && lastPos.Value != Actor.Location)
				DisguiseAs(null);

			lastPos = Actor.Location;
		}
	}

	class DisguiseOrderTargeter : UnitOrderTargeter
	{
		readonly DisguiseInfo info;

		public DisguiseOrderTargeter(Actor self, DisguiseInfo info)
			: base(self, "Disguise", 7, info.Cursor, true, true)
		{
			this.info = info;
			ForceAttack = false;
		}

		public override bool CanTargetActor(Actor target, TargetModifiers modifiers, ref string cursor)
		{
			var relationship = Actor.Owner.RelationshipWith(target.Owner);
			if (!info.ValidRelationships.HasRelationship(relationship))
				return false;

			return info.TargetTypes.Overlaps(target.GetAllTargetTypes());
		}

		public override bool CanTargetFrozenActor(FrozenActor target, TargetModifiers modifiers, ref string cursor)
		{
			var relationship = Actor.Owner.RelationshipWith(target.Owner);
			if (!info.ValidRelationships.HasRelationship(relationship))
				return false;

			return info.TargetTypes.Overlaps(target.Info.GetAllTargetTypes());
		}
	}
}
