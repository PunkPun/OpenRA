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
using System.Linq;
using OpenRA.Activities;
using OpenRA.Mods.Cnc.Traits;
using OpenRA.Mods.Common.Traits;
using OpenRA.Mods.Common.Traits.Render;
using OpenRA.Primitives;
using OpenRA.Traits;

namespace OpenRA.Mods.Cnc.Activities
{
	public class Teleport : Activity
	{
		readonly Actor teleporter;
		readonly int? maximumDistance;
		readonly bool killOnFailure;
		readonly BitSet<DamageType> killDamageTypes;
		CPos destination;
		readonly bool killCargo;
		readonly bool screenFlash;
		readonly string sound;

		public Teleport(Actor self, Actor teleporter, CPos destination, int? maximumDistance,
			bool killCargo, bool screenFlash, string sound, bool interruptable = true,
			bool killOnFailure = false, BitSet<DamageType> killDamageTypes = default)
			: base(self)
		{
			var max = teleporter.World.Map.Grid.MaximumTileSearchRange;
			if (maximumDistance > max)
				throw new InvalidOperationException($"Teleport distance cannot exceed the value of MaximumTileSearchRange ({max}).");

			this.teleporter = teleporter;
			this.destination = destination;
			this.maximumDistance = maximumDistance;
			this.killCargo = killCargo;
			this.screenFlash = screenFlash;
			this.sound = sound;
			this.killOnFailure = killOnFailure;
			this.killDamageTypes = killDamageTypes;

			if (!interruptable)
				IsInterruptible = false;
		}

		public override bool Tick()
		{
			var pc = Actor.TraitOrDefault<PortableChrono>();
			if (teleporter == Actor && pc != null && (!pc.CanTeleport || IsCanceling))
			{
				if (killOnFailure)
					Actor.Kill(teleporter, killDamageTypes);

				return true;
			}

			var bestCell = ChooseBestDestinationCell(destination);
			if (bestCell == null)
			{
				if (killOnFailure)
					Actor.Kill(teleporter, killDamageTypes);

				return true;
			}

			destination = bestCell.Value;

			Game.Sound.Play(SoundType.World, sound, Actor.CenterPosition);
			Game.Sound.Play(SoundType.World, sound, Actor.World.Map.CenterOfCell(destination));

			Actor.Trait<IPositionable>().SetPosition(destination);
			Actor.Generation++;

			if (killCargo)
			{
				var cargo = Actor.TraitOrDefault<Cargo>();
				if (cargo != null && teleporter != null)
				{
					while (!cargo.IsEmpty())
					{
						var a = cargo.Unload();

						// Kill all the units that are unloaded into the void
						// Kill() handles kill and death statistics
						a.Kill(teleporter);
					}
				}
			}

			// Consume teleport charges if this wasn't triggered via chronosphere
			if (teleporter == Actor)
				pc?.ResetChargeTime();

			// Trigger screen desaturate effect
			if (screenFlash)
				foreach (var a in Actor.World.ActorsWithTrait<ChronoshiftPaletteEffect>())
					a.Trait.Enable();

			if (teleporter != null && Actor != teleporter && !teleporter.Disposed)
			{
				var building = teleporter.TraitOrDefault<WithSpriteBody>();
				if (building != null && building.DefaultAnimation.HasSequence("active"))
					building.PlayCustomAnimation("active");
			}

			return true;
		}

		CPos? ChooseBestDestinationCell(CPos destination)
		{
			if (teleporter == null)
				return null;

			var restrictTo = maximumDistance == null ? null : Actor.World.Map.FindTilesInCircle(Actor.Location, maximumDistance.Value);

			if (maximumDistance != null)
				destination = restrictTo.MinBy(x => (x - destination).LengthSquared);

			var pos = Actor.Trait<IPositionable>();
			if (pos.CanEnterCell(destination) && teleporter.Owner.Shroud.IsExplored(destination))
				return destination;

			var max = maximumDistance != null ? maximumDistance.Value : teleporter.World.Map.Grid.MaximumTileSearchRange;
			foreach (var tile in Actor.World.Map.FindTilesInCircle(destination, max))
			{
				if (teleporter.Owner.Shroud.IsExplored(tile)
					&& (restrictTo == null || restrictTo.Contains(tile))
					&& pos.CanEnterCell(tile))
					return tile;
			}

			return null;
		}
	}
}
