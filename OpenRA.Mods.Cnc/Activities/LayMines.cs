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
		readonly Rearmable rearmable;

		List<CPos> minefield;

		public LayMines(Actor self, List<CPos> minefield = null)
		{
			minelayer = self.Trait<Minelayer>();
			ammoPools = self.TraitsImplementing<AmmoPool>().ToArray();
			movement = self.Trait<IMove>();
			rearmable = self.TraitOrDefault<Rearmable>();
			this.minefield = minefield;
		}

		protected override void OnFirstRun(Actor self)
		{
			if (minefield == null)
				minefield = new List<CPos> { self.Location };
		}

		CPos? NextValidCell(Actor self)
		{
			if (minefield != null)
				foreach (var c in minefield)
					if (CanLayMine(self, c))
						return c;

			return null;
		}

		public override bool Tick(Actor self)
		{
			if (IsCanceling)
				return true;

			if ((minefield == null || minefield.Contains(self.Location)) && CanLayMine(self, self.Location))
			{
				if (rearmable != null && ammoPools.Any(p => p.Info.Name == minelayer.Info.AmmoPoolName && !p.HasAmmo))
				{
					QueueChild(new DockActivity(self.Trait<DockManager>(), rearmable, null));
					return false;
				}

				LayMine(self);
				QueueChild(new Wait(20)); // A little wait after placing each mine, for show
				minefield.Remove(self.Location);
				return false;
			}

			var nextCell = NextValidCell(self);
			if (nextCell != null)
			{
				QueueChild(movement.MoveTo(nextCell.Value, 0));
				return false;
			}

			// TODO: Return somewhere likely to be safe (near rearm building) so we're not sitting out in the minefield.
			return true;
		}

		public void CleanMineField(Actor self)
		{
			// Remove cells that have already been mined
			// or that are revealed to be unmineable.
			if (minefield != null)
			{
				var positionable = (IPositionable)movement;
				var mobile = positionable as Mobile;
				minefield.RemoveAll(c => self.World.ActorMap.GetActorsAt(c)
					.Any(a => a.Info.Name == minelayer.Info.Mine.ToLowerInvariant() && a.CanBeViewedByPlayer(self.Owner)) ||
						((!positionable.CanEnterCell(c, null, BlockedByActor.Immovable) || (mobile != null && !mobile.CanStayInCell(c)))
						&& self.Owner.Shroud.IsVisible(c)));
			}
		}

		public override IEnumerable<TargetLineNode> TargetLineNodes(Actor self)
		{
			if (minefield == null || minefield.Count == 0)
				yield break;

			var nextCell = NextValidCell(self);
			if (nextCell != null)
				yield return new TargetLineNode(Target.FromCell(self.World, nextCell.Value), minelayer.Info.TargetLineColor);

			foreach (var c in minefield)
				yield return new TargetLineNode(Target.FromCell(self.World, c), minelayer.Info.TargetLineColor, tile: minelayer.Tile);
		}

		static bool CanLayMine(Actor self, CPos p)
		{
			// If there is no unit (other than me) here, we can place a mine here
			return self.World.ActorMap.GetActorsAt(p).All(a => a == self);
		}

		void LayMine(Actor self)
		{
			if (ammoPools != null)
			{
				var pool = ammoPools.FirstOrDefault(x => x.Info.Name == minelayer.Info.AmmoPoolName);
				if (pool == null)
					return;

				pool.TakeAmmo(self, minelayer.Info.AmmoUsage);
			}

			self.World.AddFrameEndTask(w => w.CreateActor(minelayer.Info.Mine, new TypeDictionary
			{
				new LocationInit(self.Location),
				new OwnerInit(self.Owner),
			}));
		}
	}
}
