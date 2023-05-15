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

namespace OpenRA.Mods.Common.UpdateRules.Rules
{
	public class RemoveNegativeSequenceLength : UpdateRule
	{
		public override string Name => "Negative sequence length is no longer allowed.";

		public override string Description => "Negative sequence length is no longer allowed, define individual frames in reverse instead.";

		List<MiniYamlNode> resolvedImagesNodes;

		public override IEnumerable<string> BeforeUpdateSequences(ModData modData, List<MiniYamlNode> resolvedImagesNodes)
		{
			this.resolvedImagesNodes = resolvedImagesNodes;
			yield break;
		}

		readonly Queue<Action> actionQueue = new();

		public override IEnumerable<string> UpdateSequenceNode(ModData modData, MiniYamlNode imageNode)
		{
			foreach (var node in imageNode.Value.Nodes)
			{
				var lengthNode = node.LastChildMatching("Length", includeRemovals: false);
				if (lengthNode == null || lengthNode.Value.Value == "*")
					continue;

				var length = FieldLoader.GetValue<int>(lengthNode.Key, lengthNode.Value.Value);
				if (length >= 0)
					continue;

				var startNode = node.LastChildMatching("Start", includeRemovals: false);
				if (startNode == null && node.Value.Value != "Defaults")
					startNode = imageNode.LastChildMatching("Defaults")?.LastChildMatching("Start", includeRemovals: false);

				if (startNode == null)
				{
					var resolvedImage = resolvedImagesNodes.First(n => n.Key == imageNode.Key);
					startNode = resolvedImage.LastChildMatching(node.Key)?.LastChildMatching("Start", includeRemovals: false);

					if (startNode == null && node.Value.Value != "Defaults")
						startNode = resolvedImage.LastChildMatching("Defaults")?.LastChildMatching("Start", includeRemovals: false);
				}

				if (startNode != null)
				{
					var start = FieldLoader.GetValue<int>(startNode.Key, startNode.Value.Value);
					if (start > 0)
					{
						length *= -1;
						var frames = new int[Math.Min(length, start)];
						start -= 1;
						for (var i = 0; i < frames.Length; i++)
							frames[i] = start - i;

						actionQueue.Enqueue(() =>
						{
							node.RemoveNodes("Start");
							node.RemoveNodes("Length");
							node.AddNode("Frames", frames);
						});
					}
				}
			}

			while (actionQueue.Count > 0)
    			actionQueue.Dequeue().Invoke();

			yield break;
		}
	}
}
