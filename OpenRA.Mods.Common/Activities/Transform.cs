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
using OpenRA.Activities;
using OpenRA.Mods.Common.Traits;
using OpenRA.Mods.Common.Traits.Render;
using OpenRA.Primitives;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Activities
{
	public class Transform : Activity
	{
		public readonly string ToActor;
		public CVec Offset = CVec.Zero;
		public WAngle Facing = new WAngle(384);
		public string[] Sounds = Array.Empty<string>();
		public string Notification = null;
		public string TextNotification = null;
		public int ForceHealthPercentage = 0;
		public bool SkipMakeAnims = false;
		public string Faction = null;

		public Transform(Actor self, string toActor)
			: base(self)
		{
			ToActor = toActor;
		}

		protected override void OnFirstRun()
		{
			if (Actor.Info.HasTraitInfo<IFacingInfo>())
				QueueChild(new Turn(Actor, Facing));

			if (Actor.Info.HasTraitInfo<AircraftInfo>())
				QueueChild(new Land(Actor));
		}

		public override bool Tick()
		{
			if (IsCanceling)
				return true;

			// Prevent deployment in bogus locations
			var transforms = Actor.TraitOrDefault<Transforms>();
			if (transforms != null && !transforms.CanDeploy())
				return true;

			foreach (var nt in Actor.TraitsImplementing<INotifyTransform>())
				nt.BeforeTransform();

			var makeAnimation = Actor.TraitOrDefault<WithMakeAnimation>();
			if (!SkipMakeAnims && makeAnimation != null)
			{
				// Once the make animation starts the activity must not be stopped anymore.
				IsInterruptible = false;

				// Wait forever
				QueueChild(new WaitFor(Actor, () => false));
				makeAnimation.Reverse(Actor, () => DoTransform());
				return false;
			}

			DoTransform();
			return true;
		}

		void DoTransform()
		{
			// This activity may be buried as a child within one or more parents
			// We need to consider the top-level activities when transferring orders to the new actor!
			var currentActivity = Actor.CurrentActivity;

			Actor.World.AddFrameEndTask(w =>
			{
				if (Actor.IsDead || Actor.WillDispose)
					return;

				foreach (var nt in Actor.TraitsImplementing<INotifyTransform>())
					nt.OnTransform();

				var selected = w.Selection.Contains(Actor);
				var controlgroup = w.ControlGroups.GetControlGroupForActor(Actor);

				Actor.Dispose();
				foreach (var s in Sounds)
					Game.Sound.PlayToPlayer(SoundType.World, Actor.Owner, s, Actor.CenterPosition);

				Game.Sound.PlayNotification(Actor.World.Map.Rules, Actor.Owner, "Speech", Notification, Actor.Owner.Faction.InternalName);
				TextNotificationsManager.AddTransientLine(TextNotification, Actor.Owner);

				var init = new TypeDictionary
				{
					new LocationInit(Actor.Location + Offset),
					new OwnerInit(Actor.Owner),
					new FacingInit(Facing),
				};

				if (SkipMakeAnims)
					init.Add(new SkipMakeAnimsInit());

				if (Faction != null)
					init.Add(new FactionInit(Faction));

				var health = Actor.TraitOrDefault<IHealth>();
				if (health != null)
				{
					// Cast to long to avoid overflow when multiplying by the health
					var newHP = ForceHealthPercentage > 0 ? ForceHealthPercentage : (int)(health.HP * 100L / health.MaxHP);
					init.Add(new HealthInit(newHP));
				}

				foreach (var modifier in Actor.TraitsImplementing<ITransformActorInitModifier>())
					modifier.ModifyTransformActorInit(init);

				var a = w.CreateActor(ToActor, init);
				foreach (var nt in Actor.TraitsImplementing<INotifyTransform>())
					nt.AfterTransform(a);

				// Use self.CurrentActivity to capture the parent activity if Transform is a child
				foreach (var transfer in currentActivity.ActivitiesImplementing<IssueOrderAfterTransform>(false))
				{
					if (transfer.IsCanceling)
						continue;

					var order = transfer.IssueOrderForTransformedActor(a);
					foreach (var t in a.TraitsImplementing<IResolveOrder>())
						t.ResolveOrder(order);
				}

				Actor.ReplacedByActor = a;

				if (selected)
					w.Selection.Add(a);

				if (controlgroup.HasValue)
					w.ControlGroups.AddToControlGroup(a, controlgroup.Value);
			});
		}
	}

	class IssueOrderAfterTransform : Activity
	{
		readonly string orderString;
		readonly Target target;
		readonly Color? targetLineColor;

		public IssueOrderAfterTransform(Actor self, string orderString, in Target target, Color? targetLineColor = null)
			: base(self)
		{
			this.orderString = orderString;
			this.target = target;
			this.targetLineColor = targetLineColor;
		}

		public Order IssueOrderForTransformedActor(Actor newActor)
		{
			return new Order(orderString, newActor, target, true);
		}

		public override IEnumerable<TargetLineNode> TargetLineNodes()
		{
			if (targetLineColor != null)
				yield return new TargetLineNode(target, targetLineColor.Value);
		}
	}
}
