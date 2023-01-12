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
using OpenRA.Mods.Common.Activities;
using OpenRA.Mods.Common.Orders;
using OpenRA.Mods.Common.Widgets;
using OpenRA.Primitives;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Traits
{
	[Desc("This actor can transport Passenger actors.")]
	public class CargoInfo : TraitInfo, Requires<IOccupySpaceInfo>
	{
		[Desc("The maximum sum of Passenger.Weight that this actor can support.")]
		public readonly int MaxWeight = 0;

		[Desc("`Passenger.CargoType`s that can be loaded into this actor.")]
		public readonly HashSet<string> Types = new HashSet<string>();

		[Desc("A list of actor types that are initially spawned into this actor.")]
		public readonly string[] InitialUnits = Array.Empty<string>();

		[Desc("When this actor is sold should all of its passengers be unloaded?")]
		public readonly bool EjectOnSell = true;

		[Desc("When this actor dies should all of its passengers be unloaded?")]
		public readonly bool EjectOnDeath = false;

		[Desc("Terrain types that this actor is allowed to eject actors onto. Leave empty for all terrain types.")]
		public readonly HashSet<string> UnloadTerrainTypes = new HashSet<string>();

		[VoiceReference]
		[Desc("Voice to play when ordered to unload the passengers.")]
		public readonly string UnloadVoice = "Action";

		[Desc("Radius to search for a load/unload location if the ordered cell is blocked.")]
		public readonly WDist LoadRange = WDist.FromCells(5);

		[Desc("Which direction the passenger will face (relative to the transport) when unloading.")]
		public readonly WAngle PassengerFacing = new WAngle(512);

		[Desc("Delay (in ticks) before continuing after loading a passenger.")]
		public readonly int AfterLoadDelay = 8;

		[Desc("Delay (in ticks) before unloading the first passenger.")]
		public readonly int BeforeUnloadDelay = 8;

		[Desc("Delay (in ticks) before continuing after unloading a passenger.")]
		public readonly int AfterUnloadDelay = 25;

		[CursorReference]
		[Desc("Cursor to display when able to unload the passengers.")]
		public readonly string UnloadCursor = "deploy";

		[CursorReference]
		[Desc("Cursor to display when unable to unload the passengers.")]
		public readonly string UnloadBlockedCursor = "deploy-blocked";

		[GrantedConditionReference]
		[Desc("The condition to grant to self while waiting for cargo to load.")]
		public readonly string LoadingCondition = null;

		[GrantedConditionReference]
		[Desc("The condition to grant to self while passengers are loaded.",
			"Condition can stack with multiple passengers.")]
		public readonly string LoadedCondition = null;

		[ActorReference(dictionaryReference: LintDictionaryReference.Keys)]
		[Desc("Conditions to grant when specified actors are loaded inside the transport.",
			"A dictionary of [actor name]: [condition].")]
		public readonly Dictionary<string, string> PassengerConditions = new Dictionary<string, string>();

		[GrantedConditionReference]
		public IEnumerable<string> LinterPassengerConditions => PassengerConditions.Values;

		public override object Create(ActorInitializer init) { return new Cargo(init, this); }
	}

	public class Cargo : IIssueOrder, IResolveOrder, IOrderVoice, INotifyCreated, INotifyKilled,
		INotifyOwnerChanged, INotifySold, INotifyActorDisposing, IIssueDeployOrder,
		ITransformActorInitModifier
	{
		public readonly CargoInfo Info;
		public readonly Actor Actor;
		readonly List<Actor> cargo = new List<Actor>();
		readonly HashSet<Actor> reserves = new HashSet<Actor>();
		readonly Dictionary<string, Stack<int>> passengerTokens = new Dictionary<string, Stack<int>>();
		readonly Lazy<IFacing> facing;
		readonly bool checkTerrainType;

		int totalWeight = 0;
		int reservedWeight = 0;
		Aircraft aircraft;
		int loadingToken = Actor.InvalidConditionToken;
		readonly Stack<int> loadedTokens = new Stack<int>();
		bool takeOffAfterLoad;
		bool initialised;

		readonly CachedTransform<CPos, IEnumerable<CPos>> currentAdjacentCells;

		public IEnumerable<CPos> CurrentAdjacentCells => currentAdjacentCells.Update(Actor.Location);

		public IEnumerable<Actor> Passengers => cargo;
		public int PassengerCount => cargo.Count;

		enum State { Free, Locked }
		State state = State.Free;

		public Cargo(ActorInitializer init, CargoInfo info)
		{
			Actor = init.Self;
			Info = info;
			checkTerrainType = info.UnloadTerrainTypes.Count > 0;

			currentAdjacentCells = new CachedTransform<CPos, IEnumerable<CPos>>(loc =>
			{
				return Util.AdjacentCells(Actor.World, Target.FromActor(Actor)).Where(c => loc != c);
			});

			var runtimeCargoInit = init.GetOrDefault<RuntimeCargoInit>(info);
			var cargoInit = init.GetOrDefault<CargoInit>(info);
			if (runtimeCargoInit != null)
			{
				cargo = runtimeCargoInit.Value.ToList();
				totalWeight = cargo.Sum(c => GetWeight(c));
			}
			else if (cargoInit != null)
			{
				foreach (var u in cargoInit.Value)
				{
					var unit = Actor.World.CreateActor(false, u.ToLowerInvariant(),
						new TypeDictionary { new OwnerInit(Actor.Owner) });

					cargo.Add(unit);
				}

				totalWeight = cargo.Sum(c => GetWeight(c));
			}
			else
			{
				foreach (var u in info.InitialUnits)
				{
					var unit = Actor.World.CreateActor(false, u.ToLowerInvariant(),
						new TypeDictionary { new OwnerInit(Actor.Owner) });

					cargo.Add(unit);
				}

				totalWeight = cargo.Sum(c => GetWeight(c));
			}

			facing = Exts.Lazy(Actor.TraitOrDefault<IFacing>);
		}

		void INotifyCreated.Created()
		{
			aircraft = Actor.TraitOrDefault<Aircraft>();

			if (cargo.Count > 0)
			{
				foreach (var c in cargo)
					if (Info.PassengerConditions.TryGetValue(c.Info.Name, out var passengerCondition))
						passengerTokens.GetOrAdd(c.Info.Name).Push(Actor.GrantCondition(passengerCondition));

				if (!string.IsNullOrEmpty(Info.LoadedCondition))
					loadedTokens.Push(Actor.GrantCondition(Info.LoadedCondition));
			}

			// Defer notifications until we are certain all traits on the transport are initialised
			Actor.World.AddFrameEndTask(w =>
			{
				foreach (var c in cargo)
				{
					c.Trait<Passenger>().Transport = Actor;

					foreach (var nec in c.TraitsImplementing<INotifyEnteredCargo>())
						nec.OnEnteredCargo(Actor);

					foreach (var npe in Actor.TraitsImplementing<INotifyPassengerEntered>())
						npe.OnPassengerEntered(c);
				}

				initialised = true;
			});
		}

		static int GetWeight(Actor a) { return a.Info.TraitInfo<PassengerInfo>().Weight; }

		public IEnumerable<IOrderTargeter> Orders
		{
			get
			{
				yield return new DeployOrderTargeter(Actor, "Unload", 10,
					() => CanUnload() ? Info.UnloadCursor : Info.UnloadBlockedCursor);
			}
		}

		public Order IssueOrder(IOrderTargeter order, in Target target, bool queued)
		{
			if (order.OrderID == "Unload")
				return new Order(order.OrderID, Actor, queued);

			return null;
		}

		Order IIssueDeployOrder.IssueDeployOrder(bool queued)
		{
			return new Order("Unload", Actor, queued);
		}

		bool IIssueDeployOrder.CanIssueDeployOrder(bool queued) { return true; }

		public void ResolveOrder(Order order)
		{
			if (order.OrderString == "Unload")
			{
				if (!order.Queued && !CanUnload())
					return;

				Actor.QueueActivity(order.Queued, new UnloadCargo(Actor, Info.LoadRange));
			}
		}

		public bool CanUnload(BlockedByActor check = BlockedByActor.None)
		{
			if (checkTerrainType)
			{
				var terrainType = Actor.World.Map.GetTerrainInfo(Actor.Location).Type;

				if (!Info.UnloadTerrainTypes.Contains(terrainType))
					return false;
			}

			return !IsEmpty() && (aircraft == null || aircraft.CanLand(Actor.Location, blockedByMobile: false))
				&& CurrentAdjacentCells != null && CurrentAdjacentCells.Any(c => Passengers.Any(p => !p.IsDead && p.Trait<IPositionable>().CanEnterCell(c, null, check)));
		}

		public bool CanLoad(Actor a)
		{
			return reserves.Contains(a) || HasSpace(GetWeight(a));
		}

		internal bool ReserveSpace(Actor a)
		{
			if (reserves.Contains(a))
				return true;

			var w = GetWeight(a);
			if (!HasSpace(w))
				return false;

			if (loadingToken == Actor.InvalidConditionToken)
				loadingToken = Actor.GrantCondition(Info.LoadingCondition);

			reserves.Add(a);
			reservedWeight += w;
			LockForPickup();

			return true;
		}

		internal void UnreserveSpace(Actor a)
		{
			if (!reserves.Contains(a) || Actor.IsDead)
				return;

			reservedWeight -= GetWeight(a);
			reserves.Remove(a);
			ReleaseLock();

			if (loadingToken != Actor.InvalidConditionToken)
				loadingToken = Actor.RevokeCondition(loadingToken);
		}

		// Prepare for transport pickup
		void LockForPickup()
		{
			if (state == State.Locked)
				return;

			state = State.Locked;

			Actor.CancelActivity();

			var air = Actor.TraitOrDefault<Aircraft>();
			if (air != null && !air.AtLandAltitude)
			{
				takeOffAfterLoad = true;
				Actor.QueueActivity(new Land(Actor));
			}

			Actor.QueueActivity(new WaitFor(Actor, () => state != State.Locked, false));
		}

		void ReleaseLock()
		{
			if (reservedWeight != 0)
				return;

			state = State.Free;

			Actor.QueueActivity(new Wait(Actor, Info.AfterLoadDelay, false));
			if (takeOffAfterLoad)
				Actor.QueueActivity(new TakeOff(Actor));

			takeOffAfterLoad = false;
		}

		public string VoicePhraseForOrder(Order order)
		{
			if (order.OrderString != "Unload" || IsEmpty() || !Actor.HasVoice(Info.UnloadVoice))
				return null;

			return Info.UnloadVoice;
		}

		public bool HasSpace(int weight) { return totalWeight + reservedWeight + weight <= Info.MaxWeight; }
		public bool IsEmpty() { return cargo.Count == 0; }

		public Actor Peek() { return cargo.Last(); }

		public Actor Unload(Actor passenger = null)
		{
			passenger = passenger ?? cargo.Last();
			if (!cargo.Remove(passenger))
				throw new ArgumentException("Attempted to unload an actor that is not a passenger.");

			totalWeight -= GetWeight(passenger);

			SetPassengerFacing(passenger);

			foreach (var npe in Actor.TraitsImplementing<INotifyPassengerExited>())
				npe.OnPassengerExited(passenger);

			foreach (var nec in passenger.TraitsImplementing<INotifyExitedCargo>())
				nec.OnExitedCargo(Actor);

			var p = passenger.Trait<Passenger>();
			p.Transport = null;

			if (passengerTokens.TryGetValue(passenger.Info.Name, out var passengerToken) && passengerToken.Count > 0)
				Actor.RevokeCondition(passengerToken.Pop());

			if (loadedTokens.Count > 0)
				Actor.RevokeCondition(loadedTokens.Pop());

			return passenger;
		}

		void SetPassengerFacing(Actor passenger)
		{
			if (facing.Value == null)
				return;

			var passengerFacing = passenger.TraitOrDefault<IFacing>();
			if (passengerFacing != null)
				passengerFacing.Facing = facing.Value.Facing + Info.PassengerFacing;
		}

		public void Load(Actor a)
		{
			cargo.Add(a);
			var w = GetWeight(a);
			totalWeight += w;
			if (reserves.Contains(a))
			{
				reservedWeight -= w;
				reserves.Remove(a);
				ReleaseLock();

				if (loadingToken != Actor.InvalidConditionToken)
					loadingToken = Actor.RevokeCondition(loadingToken);
			}

			// Don't initialise (effectively twice) if this runs before the FrameEndTask from Created
			if (initialised)
			{
				a.Trait<Passenger>().Transport = Actor;

				foreach (var nec in a.TraitsImplementing<INotifyEnteredCargo>())
					nec.OnEnteredCargo(Actor);

				foreach (var npe in Actor.TraitsImplementing<INotifyPassengerEntered>())
					npe.OnPassengerEntered(a);
			}

			if (Info.PassengerConditions.TryGetValue(a.Info.Name, out var passengerCondition))
				passengerTokens.GetOrAdd(a.Info.Name).Push(Actor.GrantCondition(passengerCondition));

			if (!string.IsNullOrEmpty(Info.LoadedCondition))
				loadedTokens.Push(Actor.GrantCondition(Info.LoadedCondition));
		}

		void INotifyKilled.Killed(AttackInfo e)
		{
			if (Info.EjectOnDeath)
				while (!IsEmpty() && CanUnload(BlockedByActor.All))
				{
					var passenger = Unload(Actor);
					var cp = Actor.CenterPosition;
					var inAir = Actor.World.Map.DistanceAboveTerrain(cp).Length != 0;
					var positionable = passenger.Trait<IPositionable>();
					positionable.SetPosition(Actor.Location);

					if (!inAir && positionable.CanEnterCell(Actor.Location, Actor, BlockedByActor.None))
					{
						Actor.World.AddFrameEndTask(w => w.Add(passenger));
						var nbms = passenger.TraitsImplementing<INotifyBlockingMove>();
						foreach (var nbm in nbms)
							nbm.OnNotifyBlockingMove(passenger);
					}
					else
						passenger.Kill(e.Attacker);
				}

			foreach (var c in cargo)
				c.Kill(e.Attacker);

			cargo.Clear();
		}

		void INotifyActorDisposing.Disposing()
		{
			foreach (var c in cargo)
				c.Dispose();

			cargo.Clear();
		}

		void INotifySold.Selling() { }
		void INotifySold.Sold()
		{
			if (!Info.EjectOnSell || cargo == null)
				return;

			while (!IsEmpty())
				SpawnPassenger(Unload());
		}

		void SpawnPassenger(Actor passenger)
		{
			Actor.World.AddFrameEndTask(w =>
			{
				w.Add(passenger);
				passenger.Trait<IPositionable>().SetPosition(Actor.Location);

				// TODO: this won't work well for >1 actor as they should move towards the next enterable (sub) cell instead
			});
		}

		void INotifyOwnerChanged.OnOwnerChanged(Player oldOwner, Player newOwner)
		{
			if (cargo == null)
				return;

			foreach (var p in Passengers)
				p.ChangeOwner(newOwner);
		}

		void ITransformActorInitModifier.ModifyTransformActorInit(TypeDictionary init)
		{
			init.Add(new RuntimeCargoInit(Info, Passengers.ToArray()));
		}
	}

	public class RuntimeCargoInit : ValueActorInit<Actor[]>, ISuppressInitExport
	{
		public RuntimeCargoInit(TraitInfo info, Actor[] value)
			: base(info, value) { }
	}

	public class CargoInit : ValueActorInit<string[]>
	{
		public CargoInit(TraitInfo info, string[] value)
			: base(info, value) { }
	}
}
