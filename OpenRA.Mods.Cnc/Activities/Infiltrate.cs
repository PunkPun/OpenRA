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

using System.Linq;
using OpenRA.Mods.Cnc.Traits;
using OpenRA.Mods.Common.Activities;
using OpenRA.Mods.Common.Traits;
using OpenRA.Primitives;
using OpenRA.Traits;

namespace OpenRA.Mods.Cnc.Activities
{
	class Infiltrate : Enter
	{
		readonly Infiltrates infiltrates;
		readonly INotifyInfiltration[] notifiers;
		Actor enterActor;

		public Infiltrate(Actor self, in Target target, Infiltrates infiltrates, Color? targetLineColor)
			: base(self, target, targetLineColor)
		{
			this.infiltrates = infiltrates;
			notifiers = self.TraitsImplementing<INotifyInfiltration>().ToArray();
		}

		protected override void TickInner(in Target target, bool targetIsDeadOrHiddenActor)
		{
			if (infiltrates.IsTraitDisabled)
				Cancel(true);
		}

		protected override bool TryStartEnter(Actor targetActor)
		{
			// Make sure we can still demolish the target before entering
			// (but not before, because this may stop the actor in the middle of nowhere)
			if (!infiltrates.CanInfiltrateTarget(Target.FromActor(targetActor)))
			{
				Cancel(true);
				return false;
			}

			enterActor = targetActor;
			return true;
		}

		protected override void OnEnterComplete(Actor targetActor)
		{
			// Make sure the target hasn't changed while entering
			// OnEnterComplete is only called if targetActor is alive
			if (targetActor != enterActor || !infiltrates.CanInfiltrateTarget(Target.FromActor(targetActor)))
				return;

			foreach (var ini in notifiers)
				ini.Infiltrating();

			foreach (var t in targetActor.TraitsImplementing<INotifyInfiltrated>())
				t.Infiltrated(Actor, infiltrates.Info.Types);

			var exp = Actor.Owner.PlayerActor.TraitOrDefault<PlayerExperience>();
			exp?.GiveExperience(infiltrates.Info.PlayerExperience);

			if (!string.IsNullOrEmpty(infiltrates.Info.Notification))
				Game.Sound.PlayNotification(Actor.World.Map.Rules, Actor.Owner, "Speech",
					infiltrates.Info.Notification, Actor.Owner.Faction.InternalName);

			TextNotificationsManager.AddTransientLine(infiltrates.Info.TextNotification, Actor.Owner);

			if (infiltrates.Info.EnterBehaviour == EnterBehaviour.Dispose)
				Actor.Dispose();
			else if (infiltrates.Info.EnterBehaviour == EnterBehaviour.Suicide)
				Actor.Kill(Actor);
		}
	}
}
