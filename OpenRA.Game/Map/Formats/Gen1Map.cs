#region Copyright & License Information
/*
 * Copyright (c) The OpenRA Developers (see AUTHORS)
 * This file is part of OpenRA, which is free software. It is made
 * available to you under the terms of the GNU General Public License
 * as published by the Free Software Foundation, either version 3 of
 * the License, or (at your option) any later version. For more
 * information, see COPYING.
 */
#endregion

using OpenRA.FileSystem;

namespace OpenRA
{
	public class Gen1MapLoader : IMapLoader
	{
		public virtual Map Load(ModData modData, IReadOnlyPackage package)
		{
			return new Gen1Map(modData, package);
		}

		public virtual Map Create(ModData modData, ITerrainInfo terrainInfo, int width, int height)
		{
			return new Gen1Map(modData, terrainInfo, width, height);
		}
	}

	public class Gen1Map : Map
	{
		public Gen1Map(ModData modData, ITerrainInfo terrainInfo, int width, int height)
			: base(modData, terrainInfo, width, height) { }

		public Gen1Map(ModData modData, IReadOnlyPackage package)
			: base(modData, package) { }
	}
}
