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
using OpenRA.Mods.Common.Traits;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Activities
{
	public class UnloadCargo : Activity
	{
		readonly Cargo cargo;
		readonly INotifyUnload[] notifiers;
		readonly bool unloadAll;
		readonly Aircraft aircraft;
		readonly Mobile mobile;
		readonly bool assignTargetOnFirstRun;
		readonly WDist unloadRange;

		Target destination;
		bool takeOffAfterUnload;

		public UnloadCargo(Actor self, WDist unloadRange, bool unloadAll = true)
			: this(self, Target.Invalid, unloadRange, unloadAll)
		{
			assignTargetOnFirstRun = true;
		}

		public UnloadCargo(Actor self, in Target destination, WDist unloadRange, bool unloadAll = true)
			: base(self)
		{
			cargo = self.Trait<Cargo>();
			notifiers = self.TraitsImplementing<INotifyUnload>().ToArray();
			this.unloadAll = unloadAll;
			aircraft = self.TraitOrDefault<Aircraft>();
			mobile = self.TraitOrDefault<Mobile>();
			this.destination = destination;
			this.unloadRange = unloadRange;
		}

		public (CPos Cell, SubCell SubCell)? ChooseExitSubCell(Actor passenger)
		{
			var pos = passenger.Trait<IPositionable>();

			return cargo.CurrentAdjacentCells
				.Shuffle(Actor.World.SharedRandom)
				.Select(c => (c, pos.GetAvailableSubCell(c)))
				.Cast<(CPos, SubCell SubCell)?>()
				.FirstOrDefault(s => s.Value.SubCell != SubCell.Invalid);
		}

		IEnumerable<CPos> BlockedExitCells(Actor passenger)
		{
			var pos = passenger.Trait<IPositionable>();

			// Find the cells that are blocked by transient actors
			return cargo.CurrentAdjacentCells
				.Where(c => pos.CanEnterCell(c, null, BlockedByActor.All) != pos.CanEnterCell(c, null, BlockedByActor.None));
		}

		protected override void OnFirstRun()
		{
			if (assignTargetOnFirstRun)
				destination = Target.FromCell(Actor.World, Actor.Location);

			// Move to the target destination
			if (aircraft != null)
			{
				// Queue the activity even if already landed in case self.Location != destination
				QueueChild(new Land(Actor, destination, unloadRange));
				takeOffAfterUnload = !aircraft.AtLandAltitude;
			}
			else if (mobile != null)
			{
				var cell = Actor.World.Map.Clamp(Actor.World.Map.CellContaining(destination.CenterPosition));
				QueueChild(new Move(Actor, cell, unloadRange));
			}

			QueueChild(new Wait(Actor, cargo.Info.BeforeUnloadDelay));
		}

		public override bool Tick()
		{
			if (IsCanceling || cargo.IsEmpty())
				return true;

			if (cargo.CanUnload())
			{
				foreach (var inu in notifiers)
					inu.Unloading();

				var actor = cargo.Peek();
				var spawn = Actor.CenterPosition;

				var exitSubCell = ChooseExitSubCell(actor);
				if (exitSubCell == null)
				{
					Actor.NotifyBlocker(BlockedExitCells(actor));
					QueueChild(new Wait(Actor, 10));
					return false;
				}

				cargo.Unload();
				Actor.World.AddFrameEndTask(w =>
				{
					if (actor.Disposed)
						return;

					var move = actor.Trait<IMove>();
					var pos = actor.Trait<IPositionable>();

					pos.SetPosition(exitSubCell.Value.Cell, exitSubCell.Value.SubCell);
					pos.SetCenterPosition(spawn);

					actor.CancelActivity();
					w.Add(actor);
				});
			}

			if (!unloadAll || !cargo.CanUnload())
			{
				if (cargo.Info.AfterUnloadDelay > 0)
					QueueChild(new Wait(Actor, cargo.Info.AfterUnloadDelay, false));

				if (takeOffAfterUnload)
					QueueChild(new TakeOff(Actor));

				return true;
			}

			return false;
		}
	}
}
