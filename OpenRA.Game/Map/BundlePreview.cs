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
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using OpenRA.FileFormats;
using OpenRA.FileSystem;
using OpenRA.Graphics;
using OpenRA.Primitives;
using OpenRA.Support;

namespace OpenRA
{
	public enum BundleStatus { Available, Unavailable, Searching, DownloadAvailable, Downloading, DownloadError }

	public class MapBundle : IReadOnlyFileSystem, IDisposable
	{
		public const int SupportedBundleFormat = 1;

		/// <summary>Wrapper that enables bundle data to be replaced in an atomic fashion</summary>
		class InnerData
		{
			public int BundleFormat;
			public string Title;
			public string[] Categories;
			public string Author;
			public DateTime ModifiedDate;
			public BundleStatus Status;

			public MiniYaml RuleDefinitions;
			public MiniYaml TranslationDefinitions;
			public MiniYaml WeaponDefinitions;
			public MiniYaml VoiceDefinitions;
			public MiniYaml MusicDefinitions;
			public MiniYaml NotificationDefinitions;
			public MiniYaml SequenceDefinitions;
			public MiniYaml ModelSequenceDefinitions;

			public ActorInfo WorldActorInfo { get; private set; }
			public ActorInfo PlayerActorInfo { get; private set; }

			static MiniYaml LoadRuleSection(Dictionary<string, MiniYaml> yaml, string section)
			{
				if (!yaml.TryGetValue(section, out var node))
					return null;

				return node;
			}

			static bool IsLoadableRuleDefinition(MiniYamlNode n)
			{
				if (n.Key[0] == '^')
					return true;

				var key = n.Key.ToLowerInvariant();
				return key == "world" || key == "player";
			}

			public void SetCustomRules(ModData modData, IReadOnlyFileSystem fileSystem, Dictionary<string, MiniYaml> yaml)
			{
				RuleDefinitions = LoadRuleSection(yaml, "Rules");
				TranslationDefinitions = LoadRuleSection(yaml, "Translations");
				WeaponDefinitions = LoadRuleSection(yaml, "Weapons");
				VoiceDefinitions = LoadRuleSection(yaml, "Voices");
				MusicDefinitions = LoadRuleSection(yaml, "Music");
				NotificationDefinitions = LoadRuleSection(yaml, "Notifications");
				SequenceDefinitions = LoadRuleSection(yaml, "Sequences");
				ModelSequenceDefinitions = LoadRuleSection(yaml, "ModelSequences");

				try
				{
					// PERF: Implement a minimal custom loader for custom world and player actors to minimize loading time
					// This assumes/enforces that these actor types can only inherit abstract definitions (starting with ^)
					if (RuleDefinitions != null)
					{
						var files = modData.Manifest.Rules.AsEnumerable();
						if (RuleDefinitions.Value != null)
						{
							var bundleFiles = FieldLoader.GetValue<string[]>("value", RuleDefinitions.Value);
							files = files.Append(bundleFiles);
						}

						var sources = files.Select(s => MiniYaml.FromStream(fileSystem.Open(s), s).Where(IsLoadableRuleDefinition).ToList());
						if (RuleDefinitions.Nodes.Count > 0)
							sources = sources.Append(RuleDefinitions.Nodes.Where(IsLoadableRuleDefinition).ToList());

						var yamlNodes = MiniYaml.Merge(sources);
						WorldActorInfo = new ActorInfo(modData.ObjectCreator, "world", yamlNodes.First(n => string.Equals(n.Key, "world", StringComparison.InvariantCultureIgnoreCase)).Value);
						PlayerActorInfo = new ActorInfo(modData.ObjectCreator, "player", yamlNodes.First(n => string.Equals(n.Key, "player", StringComparison.InvariantCultureIgnoreCase)).Value);
						return;
					}
				}
				catch (Exception e)
				{
					Log.Write("debug", $"Failed to load rules for `{Title}` with error: {e.Message}");
				}

				WorldActorInfo = modData.DefaultRules.Actors[SystemActors.World];
				PlayerActorInfo = modData.DefaultRules.Actors[SystemActors.Player];
			}

			public InnerData Clone()
			{
				return (InnerData)MemberwiseClone();
			}
		}

		readonly BundleCache cache;
		readonly ModData modData;

		volatile InnerData innerData;

		public int BundleFormat => innerData.BundleFormat;
		public string Title => innerData.Title;
		public string[] Categories => innerData.Categories;
		public string Author => innerData.Author;
		public BundleStatus Status => innerData.Status;

		public MiniYaml RuleDefinitions => innerData.RuleDefinitions;
		public MiniYaml TranslationDefinitions => innerData.TranslationDefinitions;
		public MiniYaml WeaponDefinitions => innerData.WeaponDefinitions;
		public MiniYaml VoiceDefinitions => innerData.VoiceDefinitions;
		public MiniYaml NotificationDefinitions => innerData.NotificationDefinitions;
		public MiniYaml MusicDefinitions => innerData.MusicDefinitions;
		public MiniYaml SequenceDefinitions => innerData.SequenceDefinitions;
		public MiniYaml ModelSequenceDefinitions => innerData.ModelSequenceDefinitions;

		public ActorInfo WorldActorInfo => innerData.WorldActorInfo;
		public ActorInfo PlayerActorInfo => innerData.PlayerActorInfo;
		public DateTime ModifiedDate => innerData.ModifiedDate;

		public IReadOnlyPackage Package { get; private set; }
		IReadOnlyPackage parentPackage;

		public readonly string Uid;

		public MapBundle(ModData modData, string uid, BundleCache cache)
		{
			this.cache = cache;
			this.modData = modData;

			Uid = uid;

			innerData = new InnerData
			{
				BundleFormat = 0,
				Title = "Unknown Bundle",
				Categories = new[] { "Unknown" },
				Author = "Unknown Author",
				Status = BundleStatus.Unavailable,
			};
		}

		public void UpdateFromBundle(IReadOnlyPackage p, IReadOnlyPackage parent, string[] bundleCompatibility)
		{
			Dictionary<string, MiniYaml> yaml;
			using (var yamlStream = p.GetStream("bundle.yaml"))
			{
				if (yamlStream == null)
					throw new FileNotFoundException("Required file bundle.yaml not present in this bundle");

				yaml = new MiniYaml(null, MiniYaml.FromStream(yamlStream, "bundle.yaml", stringPool: cache.StringPool)).ToDictionary();
			}

			Package = p;
			parentPackage = parent;

			var newData = innerData.Clone();

			if (yaml.TryGetValue("BundleFormat", out var temp))
			{
				var format = FieldLoader.GetValue<int>("BundleFormat", temp.Value);
				if (format < SupportedBundleFormat)
					throw new InvalidDataException($"Bundle format {format} is not supported.");
			}

			if (yaml.TryGetValue("Title", out temp))
				newData.Title = temp.Value;

			if (yaml.TryGetValue("Categories", out temp))
				newData.Categories = FieldLoader.GetValue<string[]>("Categories", temp.Value);

			if (yaml.TryGetValue("Author", out temp))
				newData.Author = temp.Value;

			var requiresMod = string.Empty;
			if (yaml.TryGetValue("RequiresMod", out temp))
				requiresMod = temp.Value;

			if (yaml.TryGetValue("BundleFormat", out temp))
				newData.BundleFormat = FieldLoader.GetValue<int>("BundleFormat", temp.Value);

			newData.Status = bundleCompatibility == null || bundleCompatibility.Contains(requiresMod) ?
				BundleStatus.Available : BundleStatus.Unavailable;

			newData.SetCustomRules(modData, this, yaml);

			newData.ModifiedDate = File.GetLastWriteTime(p.Name);

			// Assign the new data atomically
			innerData = newData;
		}

		Stream IReadOnlyFileSystem.Open(string filename)
		{
			// Explicit package paths never refer to a bundle
			if (!filename.Contains('|') && Package.Contains(filename))
				return Package.GetStream(filename);

			return modData.DefaultFileSystem.Open(filename);
		}

		bool IReadOnlyFileSystem.TryGetPackageContaining(string path, out IReadOnlyPackage package, out string filename)
		{
			// Packages aren't supported inside bundles
			return modData.DefaultFileSystem.TryGetPackageContaining(path, out package, out filename);
		}

		bool IReadOnlyFileSystem.TryOpen(string filename, out Stream s)
		{
			// Explicit package paths never refer to a bundle
			if (!filename.Contains('|'))
			{
				s = Package.GetStream(filename);
				if (s != null)
					return true;
			}

			return modData.DefaultFileSystem.TryOpen(filename, out s);
		}

		bool IReadOnlyFileSystem.Exists(string filename)
		{
			// Explicit package paths never refer to a bundle
			if (!filename.Contains('|') && Package.Contains(filename))
				return true;

			return modData.DefaultFileSystem.Exists(filename);
		}

		bool IReadOnlyFileSystem.IsExternalModFile(string filename)
		{
			// Explicit package paths never refer to a bundle
			if (filename.Contains('|'))
				return modData.DefaultFileSystem.IsExternalModFile(filename);

			return false;
		}

		public static string ComputeUID(IReadOnlyPackage package)
		{
			// UID is calculated by taking an SHA1 of the yaml and binary data
			var requiredFiles = new[] { "bundle.yaml" };
			var contents = package.Contents.ToList();
			foreach (var required in requiredFiles)
				if (!contents.Contains(required))
					throw new FileNotFoundException($"Required file {required} not present in this map");

			var streams = new List<Stream>();
			try
			{
				foreach (var filename in contents)
					if (filename.EndsWith(".yaml") || filename.EndsWith(".lua"))
						streams.Add(package.GetStream(filename));

				// Take the SHA1
				if (streams.Count == 0)
					return CryptoUtil.SHA1Hash(Array.Empty<byte>());

				var merged = streams[0];
				for (var i = 1; i < streams.Count; i++)
					merged = new MergedStream(merged, streams[i]);

				return CryptoUtil.SHA1Hash(merged);
			}
			finally
			{
				foreach (var stream in streams)
					stream.Dispose();
			}
		}

		public void Dispose()
		{
			if (Package != null)
			{
				Package.Dispose();
				Package = null;
			}
		}
	}
}
