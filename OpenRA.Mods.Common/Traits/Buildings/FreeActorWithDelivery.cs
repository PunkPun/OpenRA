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

using OpenRA.Mods.Common.Activities;
using OpenRA.Primitives;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Traits
{
	[Desc("Player receives a unit for free once the building is placed.",
		"If you want more than one unit to be delivered, copy this section and assign IDs like FreeActorWithDelivery@2, ...")]
	public class FreeActorWithDeliveryInfo : FreeActorInfo
	{
		[ActorReference]
		[FieldLoader.Require]
		[Desc("Name of the delivering actor. This actor must have the `" + nameof(Carryall) + "` trait")]
		public readonly string DeliveringActor = null;

		[Desc("Cell coordinates for spawning the delivering actor. If left blank, the closest edge cell will be chosen.")]
		public readonly CPos SpawnLocation = CPos.Zero;

		[Desc("Offset relative to the top-left cell of the building.")]
		public readonly CVec DeliveryOffset = CVec.Zero;

		[Desc("Range to search for an alternative delivery location if the DeliveryOffset cell is blocked.")]
		public readonly WDist DeliveryRange = WDist.Zero;

		public override object Create(ActorInitializer init) { return new FreeActorWithDelivery(init, this); }
	}

	public class FreeActorWithDelivery : FreeActor
	{
		readonly FreeActorWithDeliveryInfo info;

		public FreeActorWithDelivery(ActorInitializer init, FreeActorWithDeliveryInfo info)
			: base(init, info)
		{
			this.info = info;
		}

		protected override void TraitEnabled()
		{
			if (!allowSpawn)
				return;

			allowSpawn = info.AllowRespawn;

			DoDelivery(Actor.Location + info.DeliveryOffset, Info.Actor, info.DeliveringActor);
		}

		public void DoDelivery(CPos location, string actorName, string carrierActorName)
		{
			CreateActors(actorName, carrierActorName, out var cargo, out var carrier);

			var carryable = cargo.Trait<Carryable>();
			carryable.Reserve(cargo, carrier);

			var carryall = carrier.Trait<Carryall>();
			carryall.AttachCarryable(carrier, cargo);
			carrier.QueueActivity(new DeliverUnit(carrier, Target.FromCell(Actor.World, location), info.DeliveryRange, carryall.Info.TargetLineColor));
			carrier.QueueActivity(new Fly(carrier, Target.FromCell(Actor.World, Actor.World.Map.ChooseRandomEdgeCell(Actor.World.SharedRandom))));
			carrier.QueueActivity(new RemoveSelf(Actor));

			Actor.World.AddFrameEndTask(w => Actor.World.Add(carrier));
		}

		void CreateActors(string actorName, string deliveringActorName, out Actor cargo, out Actor carrier)
		{
			// Get a carryall spawn location
			var location = info.SpawnLocation;
			if (location == CPos.Zero)
				location = Actor.World.Map.ChooseClosestEdgeCell(Actor.Location);

			var spawn = Actor.World.Map.CenterOfCell(location);

			var initialFacing = Actor.World.Map.FacingBetween(location, Actor.Location, WAngle.Zero);

			// If aircraft, spawn at cruise altitude
			var aircraftInfo = Actor.World.Map.Rules.Actors[deliveringActorName.ToLowerInvariant()].TraitInfoOrDefault<AircraftInfo>();
			if (aircraftInfo != null)
				spawn += new WVec(0, 0, aircraftInfo.CruiseAltitude.Length);

			// Create delivery actor
			carrier = Actor.World.CreateActor(false, deliveringActorName, new TypeDictionary
			{
				new LocationInit(location),
				new CenterPositionInit(spawn),
				new OwnerInit(Actor.Owner),
				new FacingInit(initialFacing)
			});

			// Create delivered actor
			cargo = Actor.World.CreateActor(false, actorName, new TypeDictionary
			{
				new OwnerInit(Actor.Owner),
			});
		}
	}
}
