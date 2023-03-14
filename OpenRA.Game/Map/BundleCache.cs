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
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using OpenRA.FileSystem;
using OpenRA.Graphics;
using OpenRA.Primitives;
using OpenRA.Support;

namespace OpenRA
{
	public sealed class BundleCache : IEnumerable<MapBundle>, IDisposable
	{
		public IReadOnlyList<IReadOnlyPackage> BundleLocations => bundleLocations;
		readonly List<IReadOnlyPackage> bundleLocations = new();
		readonly Cache<string, MapBundle> previews;
		readonly ModData modData;

		public Dictionary<string, string> StringPool { get; } = new();

		public BundleCache(ModData modData)
		{
			this.modData = modData;

			previews = new Cache<string, MapBundle>(uid => new MapBundle(modData, uid, this));
		}

		public void LoadBundles()
		{
			// Enumerate map directories
			foreach (var kv in modData.Manifest.BundleFolders)
			{
				var name = kv.Key;

				IReadOnlyPackage package;
				var optional = name.StartsWith("~", StringComparison.Ordinal);
				if (optional)
					name = name[1..];

				try
				{
					// HACK: If the path is inside the support directory then we may need to create it
					// Assume that the path is a directory if there is not an existing file with the same name
					var resolved = Platform.ResolvePath(name);
					if (resolved.StartsWith(Platform.SupportDir) && !File.Exists(resolved))
						Directory.CreateDirectory(resolved);

					package = modData.ModFiles.OpenPackage(name);
				}
				catch
				{
					if (optional)
						continue;

					throw;
				}

				bundleLocations.Add(package);
			}

			foreach (var kv in BundleLocations)
				foreach (var map in kv.Contents)
					LoadBundle(map, kv);
		}

		public void LoadBundle(string bundle, IReadOnlyPackage package)
		{
			IReadOnlyPackage bundlePackage = null;
			try
			{
				using (new PerfTimer(bundle))
				{
					bundlePackage = package.OpenPackage(bundle, modData.ModFiles);
					if (bundlePackage != null)
					{
						var uid = MapBundle.ComputeUID(bundlePackage);
						previews[uid].UpdateFromBundle(bundlePackage, package, modData.Manifest.MapCompatibility);
					}
				}
			}
			catch (Exception e)
			{
				bundlePackage?.Dispose();
				Console.WriteLine($"Failed to load map: {bundle}");
				Console.WriteLine($"Details: {e}");
				Log.Write("debug", $"Failed to load map: {bundle}");
				Log.Write("debug", $"Details: {e}");
			}
		}

		public MapBundle this[string key] => previews[key];

		public IEnumerator<MapBundle> GetEnumerator() => previews.Values.GetEnumerator();

		IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

		public MapBundle[] GetBundles(string[] bundleUIDs)
		{
			if (bundleUIDs == null)
				return null;

			var bundles = new List<MapBundle>();
			foreach (var uid in bundleUIDs)
			{
				if (!previews.ContainsKey(uid))
					throw new InvalidDataException($"Requested {uid} bundle is missing.");

				bundles.Add(previews[uid]);
			}

			return bundles.Count == 0 ? null : bundles.ToArray();
		}

		public string[] ValidateBundles(string[] bundleUIDs)
		{
			if (bundleUIDs == null)
				return null;

			var bundles = new List<string>();
			foreach (var uid in bundleUIDs)
				if (previews.ContainsKey(uid))
					bundles.Add(uid);

			return bundles.Count == 0 ? null : bundles.ToArray();
		}

		public void Dispose()
		{
			foreach (var p in previews.Values)
				p.Dispose();
		}
	}
}
