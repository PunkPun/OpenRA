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

using OpenRA.Mods.Common.Effects;
using OpenRA.Mods.Common.Traits;
using OpenRA.Primitives;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Activities
{
	class DonateCash : Enter
	{
		readonly int payload;
		readonly int playerExperience;

		public DonateCash(Actor self, in Target target, int payload, int playerExperience, Color? targetLineColor)
			: base(self, target, targetLineColor)
		{
			this.payload = payload;
			this.playerExperience = playerExperience;
		}

		protected override void OnEnterComplete(Actor targetActor)
		{
			var targetOwner = targetActor.Owner;
			var donated = targetOwner.PlayerActor.Trait<PlayerResources>().ChangeCash(payload);

			var exp = Actor.Owner.PlayerActor.TraitOrDefault<PlayerExperience>();
			if (exp != null && targetOwner != Actor.Owner)
				exp.GiveExperience(playerExperience);

			if (Actor.Owner.IsAlliedWith(Actor.World.RenderPlayer))
				Actor.World.AddFrameEndTask(w => w.Add(new FloatingText(targetActor.CenterPosition, targetOwner.Color, FloatingText.FormatCashTick(donated), 30)));

			foreach (var nct in targetActor.TraitsImplementing<INotifyCashTransfer>())
				nct.OnAcceptingCash(Actor);

			foreach (var nct in Actor.TraitsImplementing<INotifyCashTransfer>())
				nct.OnDeliveringCash(targetActor);

			Actor.Dispose();
		}
	}
}
