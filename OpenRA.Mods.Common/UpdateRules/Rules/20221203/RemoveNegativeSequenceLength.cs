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

		static MiniYamlNode GetNode(string key, MiniYamlNode node, MiniYamlNode defaultNode)
		{
			var searchNode = node.LastChildMatching(key, includeRemovals: false);
			if (searchNode == null && defaultNode != null)
				searchNode = defaultNode.LastChildMatching("Start", includeRemovals: false);

			return searchNode;
		}

		public override IEnumerable<string> UpdateSequenceNode(ModData modData, MiniYamlNode imageNode)
		{
			foreach (var node in imageNode.Value.Nodes)
			{
				var lengthNode = GetNode("Length", node, null);
				if (lengthNode == null || lengthNode.Value.Value == "*")
					continue;

				var length = FieldLoader.GetValue<int>(lengthNode.Key, lengthNode.Value.Value);
				if (length >= 0)
					continue;

				var defaultNode = node.Value.Value == "Defaults" ? null : imageNode.LastChildMatching("Defaults");
				var resolvedImage = resolvedImagesNodes.First(n => n.Key == imageNode.Key);
				var resolvedNode = resolvedImage.LastChildMatching(node.Key);
				var resolvedDefaultNode = resolvedNode.Value.Value == "Defaults" ? null : resolvedImage.LastChildMatching("Defaults");

				var startNode = GetNode("Start", node, defaultNode) ?? GetNode("Start", resolvedNode, resolvedDefaultNode);
				if (startNode == null)
					continue;

				var facingsNode = GetNode("Facings", node, defaultNode) ?? GetNode("Facings", resolvedNode, resolvedDefaultNode);
				var transposeNode = GetNode("Transpose", node, defaultNode) ?? GetNode("Transpose", resolvedNode, resolvedDefaultNode);

				var start = FieldLoader.GetValue<int>(startNode.Key, startNode.Value.Value);
				var facings = facingsNode == null ? 1 : FieldLoader.GetValue<int>(facingsNode.Key, facingsNode.Value.Value);
				var transpose = transposeNode != null && FieldLoader.GetValue<bool>(transposeNode.Key, transposeNode.Value.Value);

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

			while (actionQueue.Count > 0)
    			actionQueue.Dequeue().Invoke();

			yield break;
		}
	}
}
