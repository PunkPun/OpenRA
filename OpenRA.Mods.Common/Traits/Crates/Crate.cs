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
using OpenRA.Mods.Common.Activities;
using OpenRA.Mods.Common.Traits.Render;
using OpenRA.Primitives;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Traits
{
	public class CrateInfo : TraitInfo, IPositionableInfo, Requires<RenderSpritesInfo>
	{
		[Desc("Length of time (in ticks) until the crate gets removed automatically. " +
			"A value of zero disables auto-removal.")]
		public readonly int Duration = 0;

		[Desc("Allowed to land on.")]
		public readonly HashSet<string> TerrainTypes = new HashSet<string>();

		[Desc("Define actors that can collect crates by setting this into the Crushes field from the Mobile trait.")]
		public readonly string CrushClass = "crate";

		public override object Create(ActorInitializer init) { return new Crate(init, this); }

		public IReadOnlyDictionary<CPos, SubCell> OccupiedCells(ActorInfo info, CPos location, SubCell subCell = SubCell.Any)
		{
			return new Dictionary<CPos, SubCell>() { { location, SubCell.FullCell } };
		}

		bool IOccupySpaceInfo.SharesCell => false;

		public bool CanEnterCell(World world, CPos cell, SubCell subCell = SubCell.FullCell, Actor ignoreActor = null, BlockedByActor check = BlockedByActor.All)
		{
			// Since crates don't share cells and GetAvailableSubCell only returns SubCell.Full or SubCell.Invalid, we ignore the subCell parameter
			return GetAvailableSubCell(world, cell, ignoreActor, check) != SubCell.Invalid;
		}

		public bool CanExistInCell(World world, CPos cell)
		{
			if (!world.Map.Contains(cell))
				return false;

			var type = world.Map.GetTerrainInfo(cell).Type;
			if (!TerrainTypes.Contains(type))
				return false;

			return true;
		}

		public SubCell GetAvailableSubCell(World world, CPos cell, Actor ignoreActor = null, BlockedByActor check = BlockedByActor.All)
		{
			if (!CanExistInCell(world, cell))
				return SubCell.Invalid;

			if (check == BlockedByActor.None)
				return SubCell.FullCell;

			return !world.ActorMap.GetActorsAt(cell).Any(x => x != ignoreActor)
				? SubCell.FullCell : SubCell.Invalid;
		}
	}

	public class Crate : ITick, IPositionable, ICrushable, ISync, INotifyCreated,
		INotifyParachute, INotifyAddedToWorld, INotifyRemovedFromWorld, INotifyCrushed
	{
		public readonly Actor Actor;
		readonly CrateInfo info;
		bool collected;
		INotifyCenterPositionChanged[] notifyCenterPositionChanged;

		[Sync]
		int ticks;

		[Sync]
		public CPos Location;

		public Crate(ActorInitializer init, CrateInfo info)
		{
			Actor = init.Self;
			this.info = info;

			var locationInit = init.GetOrDefault<LocationInit>();
			if (locationInit != null)
				SetPosition(locationInit.Value);
		}

		void INotifyCreated.Created()
		{
			notifyCenterPositionChanged = Actor.TraitsImplementing<INotifyCenterPositionChanged>().ToArray();
		}

		void INotifyCrushed.WarnCrush(Actor crusher, BitSet<CrushClass> crushClasses) { }

		void INotifyCrushed.OnCrush(Actor crusher, BitSet<CrushClass> crushClasses)
		{
			if (!crushClasses.Contains(info.CrushClass))
				return;

			OnCrushInner(crusher);
		}

		void INotifyParachute.OnParachute() { }
		void INotifyParachute.OnLanded()
		{
			// Check whether the crate landed on anything
			var landedOn = Actor.World.ActorMap.GetActorsAt(Actor.Location)
				.Where(a => a != Actor);

			if (!landedOn.Any())
				return;

			var collector = landedOn.FirstOrDefault(a =>
			{
				// Mobile is (currently) the only trait that supports crushing
				var mi = a.Info.TraitInfoOrDefault<MobileInfo>();
				if (mi == null)
					return false;

				// Make sure that the actor can collect this crate type
				// Crate can only be crushed if it is not in the air.
				return Actor.IsAtGroundLevel() && mi.LocomotorInfo.Crushes.Contains(info.CrushClass);
			});

			// Destroy the crate if none of the units in the cell are valid collectors
			if (collector != null)
				OnCrushInner(collector);
			else
				Actor.Dispose();
		}

		void OnCrushInner(Actor crusher)
		{
			if (collected)
				return;

			var crateActions = Actor.TraitsImplementing<CrateAction>();

			Actor.Dispose();
			collected = true;

			if (crateActions.Any())
			{
				var shares = crateActions.Select(a => (Action: a, Shares: a.GetSelectionSharesOuter(crusher)));

				var totalShares = shares.Sum(a => a.Shares);
				var n = Actor.World.SharedRandom.Next(totalShares);

				foreach (var s in shares)
				{
					if (n < s.Shares)
					{
						s.Action.Activate(crusher);
						return;
					}

					n -= s.Shares;
				}
			}
		}

		void ITick.Tick()
		{
			if (info.Duration != 0 && Actor.IsInWorld && ++ticks >= info.Duration)
				Actor.Dispose();
		}

		public CPos TopLeft => Location;
		public (CPos, SubCell)[] OccupiedCells() { return new[] { (Location, SubCell.FullCell) }; }

		public WPos CenterPosition { get; private set; }

		// Sets the location (Location) and position (CenterPosition)
		public void SetPosition(WPos pos)
		{
			var cell = Actor.World.Map.CellContaining(pos);
			SetLocation(cell);
			SetCenterPosition(Actor.World.Map.CenterOfCell(cell) + new WVec(WDist.Zero, WDist.Zero, Actor.World.Map.DistanceAboveTerrain(pos)));
		}

		// Sets the location (Location) and position (CenterPosition)
		public void SetPosition(CPos cell, SubCell subCell = SubCell.Any)
		{
			SetLocation(cell);
			SetCenterPosition(Actor.World.Map.CenterOfCell(cell));
		}

		// Sets only the CenterPosition
		public void SetCenterPosition(WPos pos)
		{
			CenterPosition = pos;
			Actor.World.UpdateMaps(Actor, this);

			// This can be called from the constructor before notifyCenterPositionChanged is assigned.
			if (notifyCenterPositionChanged != null)
				foreach (var n in notifyCenterPositionChanged)
					n.CenterPositionChanged(0, 0);
		}

		// Sets only the location (Location)
		void SetLocation(CPos cell)
		{
			Actor.World.ActorMap.RemoveInfluence(this);
			Location = cell;
			Actor.World.ActorMap.AddInfluence(this);
		}

		public bool IsLeavingCell(CPos location, SubCell subCell = SubCell.Any) { return Actor.Location == location && ticks + 1 == info.Duration; }
		public SubCell GetValidSubCell(SubCell preferred = SubCell.Any) { return SubCell.FullCell; }
		public SubCell GetAvailableSubCell(CPos cell, SubCell preferredSubCell = SubCell.Any, Actor ignoreActor = null, BlockedByActor check = BlockedByActor.All)
		{
			return info.GetAvailableSubCell(Actor.World, cell, ignoreActor, check);
		}

		public bool CanExistInCell(CPos cell) { return info.CanExistInCell(Actor.World, cell); }

		public bool CanEnterCell(CPos a, Actor ignoreActor = null, BlockedByActor check = BlockedByActor.All)
		{
			return GetAvailableSubCell(a, SubCell.Any, ignoreActor, check) != SubCell.Invalid;
		}

		bool ICrushable.CrushableBy(Actor crusher, BitSet<CrushClass> crushClasses)
		{
			return crushClasses.Contains(info.CrushClass);
		}

		LongBitSet<PlayerBitMask> ICrushable.CrushableBy(BitSet<CrushClass> crushClasses)
		{
			return crushClasses.Contains(info.CrushClass) ? Actor.World.AllPlayersMask : Actor.World.NoPlayersMask;
		}

		void INotifyAddedToWorld.AddedToWorld()
		{
			Actor.World.AddToMaps(Actor, this);

			Actor.World.WorldActor.TraitOrDefault<CrateSpawner>()?.IncrementCrates();

			if (Actor.World.Map.DistanceAboveTerrain(CenterPosition) > WDist.Zero && Actor.TraitOrDefault<Parachutable>() != null)
				Actor.QueueActivity(new Parachute(Actor));
		}

		void INotifyRemovedFromWorld.RemovedFromWorld()
		{
			Actor.World.RemoveFromMaps(Actor, this);

			Actor.World.WorldActor.TraitOrDefault<CrateSpawner>()?.DecrementCrates();
		}
	}
}
