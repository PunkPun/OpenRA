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
using OpenRA.Activities;
using OpenRA.Mods.Cnc.Traits;
using OpenRA.Mods.Common.Activities;
using OpenRA.Mods.Common.Traits;
using OpenRA.Primitives;
using OpenRA.Traits;

namespace OpenRA.Mods.Cnc.Activities
{
	// Assumes you have Minelayer on that unit
	public class LayMines : Activity
	{
		readonly Minelayer minelayer;
		readonly AmmoPool[] ammoPools;
		readonly IMove movement;
		readonly IMoveInfo moveInfo;
		readonly RearmableInfo rearmableInfo;

		List<CPos> minefield;
		bool returnToBase;
		Actor rearmTarget;

		public LayMines(Actor self, List<CPos> minefield = null)
			: base(self)
		{
			minelayer = self.Trait<Minelayer>();
			ammoPools = self.TraitsImplementing<AmmoPool>().ToArray();
			movement = self.Trait<IMove>();
			moveInfo = self.Info.TraitInfo<IMoveInfo>();
			rearmableInfo = self.Info.TraitInfoOrDefault<RearmableInfo>();
			this.minefield = minefield;
		}

		protected override void OnFirstRun()
		{
			if (minefield == null)
				minefield = new List<CPos> { Actor.Location };
		}

		CPos? NextValidCell()
		{
			if (minefield != null)
				foreach (var c in minefield)
					if (CanLayMine(c))
						return c;

			return null;
		}

		public override bool Tick()
		{
			returnToBase = false;

			if (IsCanceling)
				return true;

			if ((minefield == null || minefield.Contains(Actor.Location)) && CanLayMine(Actor.Location))
			{
				if (rearmableInfo != null && ammoPools.Any(p => p.Info.Name == minelayer.Info.AmmoPoolName && !p.HasAmmo))
				{
					// Rearm (and possibly repair) at rearm building, then back out here to refill the minefield some more
					rearmTarget = Actor.World.Actors.Where(a => Actor.Owner.RelationshipWith(a.Owner) == PlayerRelationship.Ally && rearmableInfo.RearmActors.Contains(a.Info.Name))
						.ClosestTo(Actor);

					if (rearmTarget == null)
						return true;

					// Add a CloseEnough range of 512 to the Rearm/Repair activities in order to ensure that we're at the host actor
					QueueChild(new MoveAdjacentTo(Actor, Target.FromActor(rearmTarget)));
					QueueChild(movement.MoveTo(Actor.World.Map.CellContaining(rearmTarget.CenterPosition), ignoreActor: rearmTarget));
					QueueChild(new Resupply(Actor, rearmTarget, new WDist(512)));
					returnToBase = true;
					return false;
				}

				LayMine();
				QueueChild(new Wait(Actor, 20)); // A little wait after placing each mine, for show
				minefield.Remove(Actor.Location);
				return false;
			}

			var nextCell = NextValidCell();
			if (nextCell != null)
			{
				QueueChild(movement.MoveTo(nextCell.Value, 0));
				return false;
			}

			// TODO: Return somewhere likely to be safe (near rearm building) so we're not sitting out in the minefield.
			return true;
		}

		public void CleanMineField()
		{
			// Remove cells that have already been mined
			// or that are revealed to be unmineable.
			if (minefield != null)
			{
				var positionable = (IPositionable)movement;
				var mobile = positionable as Mobile;
				minefield.RemoveAll(c => Actor.World.ActorMap.GetActorsAt(c)
					.Any(a => a.Info.Name == minelayer.Info.Mine.ToLowerInvariant() && a.CanBeViewedByPlayer(Actor.Owner)) ||
						((!positionable.CanEnterCell(c, null, BlockedByActor.Immovable) || (mobile != null && !mobile.CanStayInCell(c)))
						&& Actor.Owner.Shroud.IsVisible(c)));
			}
		}

		public override IEnumerable<TargetLineNode> TargetLineNodes()
		{
			if (returnToBase)
				yield return new TargetLineNode(Target.FromActor(rearmTarget), moveInfo.GetTargetLineColor());

			if (minefield == null || minefield.Count == 0)
				yield break;

			var nextCell = NextValidCell();
			if (nextCell != null)
				yield return new TargetLineNode(Target.FromCell(Actor.World, nextCell.Value), minelayer.Info.TargetLineColor);

			foreach (var c in minefield)
				yield return new TargetLineNode(Target.FromCell(Actor.World, c), minelayer.Info.TargetLineColor, tile: minelayer.Tile);
		}

		bool CanLayMine(CPos p)
		{
			// If there is no unit (other than me) here, we can place a mine here
			return Actor.World.ActorMap.GetActorsAt(p).All(a => a == Actor);
		}

		void LayMine()
		{
			if (ammoPools != null)
			{
				var pool = ammoPools.FirstOrDefault(x => x.Info.Name == minelayer.Info.AmmoPoolName);
				if (pool == null)
					return;

				pool.TakeAmmo(minelayer.Info.AmmoUsage);
			}

			Actor.World.AddFrameEndTask(w => w.CreateActor(minelayer.Info.Mine, new TypeDictionary
			{
				new LocationInit(Actor.Location),
				new OwnerInit(Actor.Owner),
			}));
		}
	}
}
