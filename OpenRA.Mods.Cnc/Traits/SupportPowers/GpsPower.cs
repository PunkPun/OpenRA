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
using OpenRA.Mods.Cnc.Effects;
using OpenRA.Mods.Common.Traits;
using OpenRA.Mods.Common.Traits.Radar;
using OpenRA.Traits;

namespace OpenRA.Mods.Cnc.Traits
{
	[Desc("Requires `GpsWatcher` on the player actor.")]
	class GpsPowerInfo : SupportPowerInfo
	{
		[Desc("Delay in ticks between launching and revealing the map.")]
		public readonly int RevealDelay = 0;

		public readonly string DoorImage = "atek";

		[SequenceReference(nameof(DoorImage))]
		public readonly string DoorSequence = "active";

		[PaletteReference(nameof(DoorPaletteIsPlayerPalette))]
		[Desc("Palette to use for rendering the launch animation")]
		public readonly string DoorPalette = "player";

		[Desc("Custom palette is a player palette BaseName")]
		public readonly bool DoorPaletteIsPlayerPalette = true;

		public readonly string SatelliteImage = "sputnik";

		[SequenceReference(nameof(SatelliteImage))]
		public readonly string SatelliteSequence = "idle";

		[PaletteReference(nameof(SatellitePaletteIsPlayerPalette))]
		[Desc("Palette to use for rendering the satellite projectile")]
		public readonly string SatellitePalette = "player";

		[Desc("Custom palette is a player palette BaseName")]
		public readonly bool SatellitePaletteIsPlayerPalette = true;

		[Desc("Requires an actor with an online `ProvidesRadar` to show GPS dots.")]
		public readonly bool RequiresActiveRadar = true;

		public override object Create(ActorInitializer init) { return new GpsPower(init.Self, this); }
	}

	class GpsPower : SupportPower, INotifyKilled, INotifySold, INotifyOwnerChanged, ITick
	{
		readonly GpsPowerInfo info;
		GpsWatcher owner;

		public GpsPower(Actor self, GpsPowerInfo info)
			: base(self, info)
		{
			this.info = info;
			owner = self.Owner.PlayerActor.Trait<GpsWatcher>();
			owner.GpsAdd(self);
		}

		public override void Charged(string key)
		{
			Actor.Owner.PlayerActor.Trait<SupportPowerManager>().Powers[key].Activate(new Order());
		}

		public override void Activate(Order order, SupportPowerManager manager)
		{
			base.Activate(order, manager);

			Actor.World.AddFrameEndTask(w =>
			{
				PlayLaunchSounds();

				w.Add(new SatelliteLaunch(Actor, info));
			});
		}

		void INotifyKilled.Killed(AttackInfo e) { RemoveGps(); }

		void INotifySold.Selling() { }
		void INotifySold.Sold() { RemoveGps(); }

		void RemoveGps()
		{
			// Extra function just in case something needs to be added later
			owner.GpsRemove(Actor);
		}

		void INotifyOwnerChanged.OnOwnerChanged(Player oldOwner, Player newOwner)
		{
			RemoveGps();
			owner = newOwner.PlayerActor.Trait<GpsWatcher>();
			owner.GpsAdd(Actor);
		}

		bool NoActiveRadar { get { return !Actor.World.ActorsHavingTrait<ProvidesRadar>(r => !r.IsTraitDisabled).Any(a => a.Owner == Actor.Owner); } }
		bool wasPaused;

		void ITick.Tick()
		{
			if (!wasPaused && (IsTraitPaused || (info.RequiresActiveRadar && NoActiveRadar)))
			{
				wasPaused = true;
				RemoveGps();
			}
			else if (wasPaused && !IsTraitPaused && !(info.RequiresActiveRadar && NoActiveRadar))
			{
				wasPaused = false;
				owner.GpsAdd(Actor);
			}
		}
	}
}
