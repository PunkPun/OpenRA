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
using OpenRA.Primitives;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Traits
{
	public class RearmableClientInfo : DockClientBaseInfo
	{
		[Desc("Docking type")]
		public readonly BitSet<DockType> Type = new("Rearm");

		[Desc("Name(s) of AmmoPool(s) that use this trait to rearm.")]
		public readonly HashSet<string> AmmoPools = new() { "primary" };

		public override object Create(ActorInitializer init) { return new RearmableClient(init.Self, this); }
	}

	public class RearmableClient : DockClientBase<RearmableClientInfo>, INotifyCreated
	{
		public override BitSet<DockType> GetDockType => Info.Type;

		public RearmableClient(Actor self, RearmableClientInfo info)
			: base(self, info) { }

		public AmmoPool[] RearmableAmmoPools { get; private set; }

		void INotifyCreated.Created(Actor self)
		{
			RearmableAmmoPools = self.TraitsImplementing<AmmoPool>().Where(p => Info.AmmoPools.Contains(p.Info.Name)).ToArray();
		}

		protected override bool CanDock()
		{
			return RearmableAmmoPools.Any(p => !p.HasFullAmmo);
		}

		public override bool OnDockTick(Actor self, Actor hostActor, IDockHost host)
		{
			var rearmComplete = true;
			foreach (var ammoPool in RearmableAmmoPools)
			{
				if (!ammoPool.HasFullAmmo)
				{
					if (--ammoPool.RemainingTicks <= 0)
					{
						ammoPool.RemainingTicks = ammoPool.Info.ReloadDelay;
						if (!string.IsNullOrEmpty(ammoPool.Info.RearmSound))
							Game.Sound.PlayToPlayer(SoundType.World, self.Owner, ammoPool.Info.RearmSound, self.CenterPosition);

						ammoPool.GiveAmmo(self, ammoPool.Info.ReloadCount);
					}

					rearmComplete = false;
				}
			}

			return rearmComplete;
		}

		public override void OnDockCompleted(Actor self, Actor hostActor, IDockHost host)
		{
			// Reset the ReloadDelay to avoid any issues with early cancellation
			// from previous reload attempts (explicit order, host building died, etc).
			foreach (var pool in RearmableAmmoPools)
				pool.RemainingTicks = pool.Info.ReloadDelay;
		}
	}
}
