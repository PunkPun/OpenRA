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
using OpenRA.Activities;
using OpenRA.Mods.Common.Activities;
using OpenRA.Mods.Common.Effects;
using OpenRA.Mods.Common.Traits.Render;
using OpenRA.Primitives;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Traits
{
	public class RefineryInfo : TraitInfo, Requires<WithSpriteBodyInfo>, IAcceptResourcesInfo
	{
		[Desc("Actual harvester facing when docking.")]
		public readonly WAngle DockAngle = WAngle.Zero;

		[Desc("Docking cell relative to top-left cell.")]
		public readonly CVec DockOffset = CVec.Zero;

		[Desc("Does the refinery require the harvester to be dragged in?")]
		public readonly bool IsDragRequired = false;

		[Desc("Vector by which the harvester will be dragged when docking.")]
		public readonly WVec DragOffset = WVec.Zero;

		[Desc("In how many steps to perform the dragging?")]
		public readonly int DragLength = 0;

		[Desc("Store resources in silos. Adds cash directly without storing if set to false.")]
		public readonly bool UseStorage = true;

		[Desc("Discard resources once silo capacity has been reached.")]
		public readonly bool DiscardExcessResources = false;

		public readonly bool ShowTicks = true;
		public readonly int TickLifetime = 30;
		public readonly int TickVelocity = 2;
		public readonly int TickRate = 10;

		public override object Create(ActorInitializer init) { return new Refinery(init.Self, this); }
	}

	public class Refinery : INotifyCreated, ITick, IAcceptResources, INotifySold, INotifyCapture,
		INotifyOwnerChanged, ISync, INotifyActorDisposing
	{
		public readonly Actor Actor;
		readonly RefineryInfo info;
		PlayerResources playerResources;
		IEnumerable<int> resourceValueModifiers;

		int currentDisplayTick = 0;
		int currentDisplayValue = 0;

		[Sync]
		Actor dockedHarv = null;

		[Sync]
		bool preventDock = false;

		public bool AllowDocking => !preventDock;
		public CVec DeliveryOffset => info.DockOffset;
		public WAngle DeliveryAngle => info.DockAngle;
		public bool IsDragRequired => info.IsDragRequired;
		public WVec DragOffset => info.DragOffset;
		public int DragLength => info.DragLength;

		public Refinery(Actor self, RefineryInfo info)
		{
			Actor = self;
			this.info = info;
			playerResources = self.Owner.PlayerActor.Trait<PlayerResources>();
			currentDisplayTick = info.TickRate;
		}

		void INotifyCreated.Created()
		{
			resourceValueModifiers = Actor.TraitsImplementing<IResourceValueModifier>().ToArray().Select(m => m.GetResourceValueModifier());
		}

		public virtual Activity DockSequence(Actor harv)
		{
			return new SpriteHarvesterDockSequence(harv, Actor, DeliveryAngle, IsDragRequired, DragOffset, DragLength);
		}

		public IEnumerable<TraitPair<Harvester>> GetLinkedHarvesters()
		{
			return Actor.World.ActorsWithTrait<Harvester>()
				.Where(a => a.Trait.LinkedProc == Actor);
		}

		int IAcceptResources.AcceptResources(string resourceType, int count)
		{
			if (!playerResources.Info.ResourceValues.TryGetValue(resourceType, out var resourceValue))
				return 0;

			var value = Util.ApplyPercentageModifiers(count * resourceValue, resourceValueModifiers);

			if (info.UseStorage)
			{
				var storageLimit = Math.Max(playerResources.ResourceCapacity - playerResources.Resources, 0);
				if (!info.DiscardExcessResources)
				{
					// Reduce amount if needed until it will fit the available storage
					while (value > storageLimit)
						value = Util.ApplyPercentageModifiers(--count * resourceValue, resourceValueModifiers);
				}
				else
					value = Math.Min(value, playerResources.ResourceCapacity - playerResources.Resources);

				playerResources.GiveResources(value);
			}
			else
				value = playerResources.ChangeCash(value);

			foreach (var notify in Actor.World.ActorsWithTrait<INotifyResourceAccepted>())
			{
				if (notify.Actor.Owner != Actor.Owner)
					continue;

				notify.Trait.OnResourceAccepted(Actor, resourceType, count, value);
			}

			if (info.ShowTicks)
				currentDisplayValue += value;

			return count;
		}

		void CancelDock()
		{
			preventDock = true;
		}

		void ITick.Tick()
		{
			// Harvester was killed while unloading
			if (dockedHarv != null && dockedHarv.IsDead)
				dockedHarv = null;

			if (info.ShowTicks && currentDisplayValue > 0 && --currentDisplayTick <= 0)
			{
				var temp = currentDisplayValue;
				if (Actor.Owner.IsAlliedWith(Actor.World.RenderPlayer))
					Actor.World.AddFrameEndTask(w => w.Add(new FloatingText(Actor.CenterPosition, Actor.Owner.Color, FloatingText.FormatCashTick(temp), 30)));
				currentDisplayTick = info.TickRate;
				currentDisplayValue = 0;
			}
		}

		void INotifyActorDisposing.Disposing()
		{
			CancelDock();
			foreach (var harv in GetLinkedHarvesters())
				harv.Trait.UnlinkProc(harv.Actor, Actor);
		}

		public void OnDock(Actor harv, DeliverResources dockOrder)
		{
			if (!preventDock)
			{
				dockOrder.QueueChild(new CallFunc(Actor, () => dockedHarv = harv, false));
				dockOrder.QueueChild(DockSequence(harv));
				dockOrder.QueueChild(new CallFunc(Actor, () => dockedHarv = null, false));
			}
		}

		void INotifyOwnerChanged.OnOwnerChanged(Player oldOwner, Player newOwner)
		{
			// Unlink any harvesters
			foreach (var harv in GetLinkedHarvesters())
				harv.Trait.UnlinkProc(harv.Actor, Actor);

			playerResources = newOwner.PlayerActor.Trait<PlayerResources>();
		}

		void INotifyCapture.OnCapture(Actor captor, Player oldOwner, Player newOwner, BitSet<CaptureType> captureTypes)
		{
			// Steal any docked harv too
			if (dockedHarv != null)
			{
				dockedHarv.ChangeOwner(newOwner);

				// Relink to this refinery
				dockedHarv.Trait<Harvester>().LinkProc(Actor);
			}
		}

		void INotifySold.Selling() { CancelDock(); }
		void INotifySold.Sold()
		{
			foreach (var harv in GetLinkedHarvesters())
				harv.Trait.UnlinkProc(harv.Actor, Actor);
		}
	}
}
