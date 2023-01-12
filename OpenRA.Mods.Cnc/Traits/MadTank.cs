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
using OpenRA.GameRules;
using OpenRA.Mods.Common.Activities;
using OpenRA.Mods.Common.Orders;
using OpenRA.Mods.Common.Traits;
using OpenRA.Mods.Common.Traits.Render;
using OpenRA.Primitives;
using OpenRA.Traits;

namespace OpenRA.Mods.Cnc.Traits
{
	class MadTankInfo : TraitInfo, IRulesetLoaded, Requires<ExplodesInfo>, Requires<WithFacingSpriteBodyInfo>
	{
		[SequenceReference]
		public readonly string ThumpSequence = "piston";

		public readonly int ThumpInterval = 8;

		[WeaponReference]
		public readonly string ThumpDamageWeapon = "MADTankThump";

		[Desc("Measured in ticks.")]
		public readonly int ChargeDelay = 96;

		public readonly string ChargeSound = "madchrg2.aud";

		[Desc("Measured in ticks.")]
		public readonly int DetonationDelay = 42;

		public readonly string DetonationSound = "madexplo.aud";

		[WeaponReference]
		public readonly string DetonationWeapon = "MADTankDetonate";

		[ActorReference]
		public readonly string DriverActor = "e1";

		[VoiceReference]
		public readonly string Voice = "Action";

		[GrantedConditionReference]
		[Desc("The condition to grant to self while deployed.")]
		public readonly string DeployedCondition = null;

		public WeaponInfo ThumpDamageWeaponInfo { get; private set; }

		public WeaponInfo DetonationWeaponInfo { get; private set; }

		[Desc("Types of damage that this trait causes to self while self-destructing. Leave empty for no damage types.")]
		public readonly BitSet<DamageType> DamageTypes = default;

		[CursorReference]
		[Desc("Cursor to display when targeting.")]
		public readonly string AttackCursor = "attack";

		[CursorReference]
		[Desc("Cursor to display when able to set up the detonation sequence.")]
		public readonly string DeployCursor = "deploy";

		public override object Create(ActorInitializer init) { return new MadTank(this, init.Self); }

		public void RulesetLoaded(Ruleset rules, ActorInfo ai)
		{
			var thumpDamageWeaponToLower = (ThumpDamageWeapon ?? string.Empty).ToLowerInvariant();
			var detonationWeaponToLower = (DetonationWeapon ?? string.Empty).ToLowerInvariant();

			if (!rules.Weapons.TryGetValue(thumpDamageWeaponToLower, out var thumpDamageWeapon))
				throw new YamlException($"Weapons Ruleset does not contain an entry '{thumpDamageWeaponToLower}'");

			if (!rules.Weapons.TryGetValue(detonationWeaponToLower, out var detonationWeapon))
				throw new YamlException($"Weapons Ruleset does not contain an entry '{detonationWeaponToLower}'");

			ThumpDamageWeaponInfo = thumpDamageWeapon;
			DetonationWeaponInfo = detonationWeapon;
		}
	}

	class MadTank : IIssueOrder, IResolveOrder, IOrderVoice, IIssueDeployOrder
	{
		public readonly Actor Actor;
		readonly MadTankInfo info;

		bool initiated;

		public MadTank(MadTankInfo info, Actor self)
		{
			Actor = self;
			this.info = info;
		}

		public IEnumerable<IOrderTargeter> Orders
		{
			get
			{
				yield return new TargetTypeOrderTargeter(Actor, new BitSet<TargetableType>("DetonateAttack"), "DetonateAttack", 5, info.AttackCursor, true, false) { ForceAttack = false };

				if (!initiated)
					yield return new DeployOrderTargeter(Actor, "Detonate", 5, () => info.DeployCursor);
			}
		}

		Order IIssueOrder.IssueOrder(IOrderTargeter order, in Target target, bool queued)
		{
			if (order.OrderID != "DetonateAttack" && order.OrderID != "Detonate")
				return null;

			return new Order(order.OrderID, Actor, target, queued);
		}

		Order IIssueDeployOrder.IssueDeployOrder(bool queued)
		{
			return new Order("Detonate", Actor, queued);
		}

		bool IIssueDeployOrder.CanIssueDeployOrder(bool queued) { return true; }

		string IOrderVoice.VoicePhraseForOrder(Order order)
		{
			if (order.OrderString != "DetonateAttack" && order.OrderString != "Detonate")
				return null;

			return info.Voice;
		}

		void IResolveOrder.ResolveOrder(Order order)
		{
			if (order.OrderString == "DetonateAttack")
			{
				Actor.QueueActivity(order.Queued, new DetonationSequence(Actor, this, order.Target));
				Actor.ShowTargetLines();
			}
			else if (order.OrderString == "Detonate")
				Actor.QueueActivity(order.Queued, new DetonationSequence(Actor, this));
		}

		class DetonationSequence : Activity
		{
			readonly MadTank mad;
			readonly IMove move;
			readonly WithFacingSpriteBody wfsb;
			readonly bool assignTargetOnFirstRun;

			int ticks;
			Target target;

			public DetonationSequence(Actor self, MadTank mad)
				: this(self, mad, Target.Invalid)
			{
				assignTargetOnFirstRun = true;
			}

			public DetonationSequence(Actor self, MadTank mad, in Target target)
				: base(self)
			{
				this.mad = mad;
				this.target = target;

				move = self.Trait<IMove>();
				wfsb = self.Trait<WithFacingSpriteBody>();
			}

			protected override void OnFirstRun()
			{
				if (assignTargetOnFirstRun)
					target = Target.FromCell(Actor.World, Actor.Location);
			}

			public override bool Tick()
			{
				if (IsCanceling)
					return true;

				if (target.Type != TargetType.Invalid && !move.CanEnterTargetNow(target))
				{
					QueueChild(new MoveAdjacentTo(Actor, target, targetLineColor: Color.Red));
					return false;
				}

				if (!mad.initiated)
				{
					// If the target has died while we were moving, we should abort detonation.
					if (target.Type == TargetType.Invalid)
						return true;

					Actor.GrantCondition(mad.info.DeployedCondition);

					Actor.World.AddFrameEndTask(w => EjectDriver());
					if (mad.info.ThumpSequence != null)
						wfsb.PlayCustomAnimationRepeating(mad.info.ThumpSequence);

					IsInterruptible = false;
					mad.initiated = true;
				}

				if (++ticks % mad.info.ThumpInterval == 0)
				{
					if (mad.info.ThumpDamageWeapon != null)
					{
						// Use .FromPos since this weapon needs to affect more than just the MadTank actor
						mad.info.ThumpDamageWeaponInfo.Impact(Target.FromPos(Actor.CenterPosition), Actor);
					}
				}

				if (ticks == mad.info.ChargeDelay)
					Game.Sound.Play(SoundType.World, mad.info.ChargeSound, Actor.CenterPosition);

				return ticks == mad.info.ChargeDelay + mad.info.DetonationDelay;
			}

			protected override void OnLastRun()
			{
				if (!mad.initiated)
					return;

				Game.Sound.Play(SoundType.World, mad.info.DetonationSound, Actor.CenterPosition);

				Actor.World.AddFrameEndTask(w =>
				{
					if (mad.info.DetonationWeapon != null)
					{
						// Use .FromPos since this actor is killed. Cannot use Target.FromActor
						mad.info.DetonationWeaponInfo.Impact(Target.FromPos(Actor.CenterPosition), Actor);
					}

					Actor.Kill(Actor, mad.info.DamageTypes);
				});
			}

			public override IEnumerable<TargetLineNode> TargetLineNodes()
			{
				yield return new TargetLineNode(target, Color.Crimson);
			}

			void EjectDriver()
			{
				var driver = Actor.World.CreateActor(mad.info.DriverActor.ToLowerInvariant(), new TypeDictionary
				{
					new LocationInit(Actor.Location),
					new OwnerInit(Actor.Owner)
				});
				driver.TraitOrDefault<Mobile>()?.Nudge(driver);
			}
		}
	}
}
