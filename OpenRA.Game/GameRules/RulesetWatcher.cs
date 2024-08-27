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
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace OpenRA.GameRules
{
	public sealed class RulesetWatcher : IDisposable
	{
		static readonly StringComparer FileNameComparer = Platform.CurrentPlatform == PlatformType.Windows
			? StringComparer.OrdinalIgnoreCase
			: StringComparer.Ordinal;

		static readonly TimeSpan DebounceInterval = TimeSpan.FromMilliseconds(100);

		readonly object syncLock = new();

		readonly World world;
		readonly ModData modData;
		readonly IReadOnlyDictionary<string, string> watchFiles;
		readonly HashSet<string> fileQueue = new(FileNameComparer);
		readonly FileSystemWatcher watcher;
		bool isDisposed;

		CancellationTokenSource debounceCts;

		public RulesetWatcher(World world, ModData modData)
		{
			this.world = world;
			this.modData = modData;

			var dict = new Dictionary<string, string>(FileNameComparer);
			foreach (var file in modData.Manifest.Rules
				.Concat(modData.Manifest.Weapons)
				.Concat(modData.Manifest.Sequences))
			{
				string filename;
				using (var stream = modData.DefaultFileSystem.Open(file))
					filename = (stream as FileStream)?.Name;

				if (string.IsNullOrEmpty(filename))
					continue;

				var fullPath = Path.GetFullPath(filename);
				dict.Add(fullPath, file);
			}

			watchFiles = dict.ToImmutableDictionary(FileNameComparer);

			watcher = new FileSystemWatcher(modData.Manifest.Package.Name)
			{
				IncludeSubdirectories = true
			};
			watcher.Changed += FileChanged;

			foreach (var file in watchFiles.Keys)
				watcher.Filters.Add(Path.GetFileName(file));

			watcher.EnableRaisingEvents = true;
		}

		void FileChanged(object sender, FileSystemEventArgs e)
		{
			if (!watchFiles.ContainsKey(e.FullPath))
				return;

			lock (syncLock)
			{
				if (isDisposed)
					return;

				fileQueue.Add(e.FullPath);

				if (debounceCts != null)
				{
					debounceCts.Cancel();
					debounceCts.Dispose();
				}

				// Since CancellationTokenSource is already thread-safe, let's leverage that for a debounce mechanism:
				// Taking a local copy of CancellationToken from current CancellationTokenSource makes sure that current Task can be cancelled
				// by the next thread. Also, we cannot store CancellationTokenSource, since after it is disposed, CancellationToken cannot be accessed anymore.
				// Superfluous CancellationTokenSource will be disposed in ToggleWatching() or Dispose().
				debounceCts = new CancellationTokenSource();
				var localToken = debounceCts.Token;

				Task.Run(async () =>
				{
					await Task.Delay(DebounceInterval, localToken);

					List<string> changedFiles;
					lock (syncLock)
					{
						changedFiles = fileQueue.ToList();
						fileQueue.Clear();
					}

					Game.RunAfterTick(() => DoRulesetReload(changedFiles));
				});
			}
		}

		void DoRulesetReload(ICollection<string> files)
		{
			lock (syncLock)
			{
				if (isDisposed || files.Count == 0)
					return;
			}

			var modFsFilenames = files.Select(f => watchFiles[f]).ToHashSet(FileNameComparer);

			var sequenceFiles = FindModFiles(modData.Manifest.Sequences, modFsFilenames).ToArray();
			var rulesFiles = FindModFiles(modData.Manifest.Rules, modFsFilenames).ToArray();
			var weaponFiles = FindModFiles(modData.Manifest.Weapons, modFsFilenames).ToArray();

			if (sequenceFiles.Length == 0 && rulesFiles.Length == 0 && weaponFiles.Length == 0)
				return;

			var defaultRules = world.Map.Rules;
			Dictionary<string, MiniYamlNode> unresolvedRulesYaml = null;
			if (rulesFiles.Length != 0)
			{
				try
				{
					unresolvedRulesYaml = defaultRules.GetUnresolvedRulesYaml(modData, rulesFiles);
				}
				catch (Exception ex)
				{
					TextNotificationsManager.Debug($"Loading the YAML weapon files raised the exception: {ex.GetType().FullName} : {ex.Message}. Aborting.");
					return;
				}

				TextNotificationsManager.Debug($"Reloading rules files: {string.Join(", ", rulesFiles)}");
			}

			Dictionary<string, MiniYamlNode> unresolvedWeaponsYaml = null;
			if (weaponFiles.Length != 0)
			{
				try
				{
					unresolvedWeaponsYaml = defaultRules.GetUnresolvedWeaponsYaml(modData, weaponFiles);
				}
				catch (Exception ex)
				{
					TextNotificationsManager.Debug($"Loading the YAML weapon files raised the exception: {ex.GetType().FullName} : {ex.Message}. Aborting.");
					return;
				}

				TextNotificationsManager.Debug($"Reloading weapon files: {string.Join(", ", weaponFiles)}");
			}

			if (sequenceFiles.Length != 0)
			{
				TextNotificationsManager.Debug($"Reloading sequence files: {string.Join(", ", sequenceFiles)}");
				try
				{
					world.Map.Sequences.ReloadSequenceSetFromFiles(modData.DefaultFileSystem, sequenceFiles);
				}
				catch (Exception e)
				{
					TextNotificationsManager.Debug($"Reloading failed: {e.Message}");
					return;
				}
			}

			if (unresolvedRulesYaml != null || unresolvedWeaponsYaml != null)
				defaultRules.ReloadRules(world, modData, unresolvedRulesYaml, unresolvedWeaponsYaml);

			static IEnumerable<string> FindModFiles(IEnumerable<string> allFiles, ISet<string> findFiles)
				=> allFiles.Where(findFiles.Contains);
		}

		public void Dispose()
		{
			if (isDisposed)
				return;

			lock (syncLock)
			{
				if (isDisposed)
					return;

				debounceCts?.Dispose();
				fileQueue.Clear();

				isDisposed = true;

				watcher.Changed -= FileChanged;
				watcher.Dispose();
			}
		}
	}
}
