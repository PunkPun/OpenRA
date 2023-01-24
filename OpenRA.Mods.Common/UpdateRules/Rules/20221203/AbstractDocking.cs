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

namespace OpenRA.Mods.Common.UpdateRules.Rules
{
	public class AbstractDocking : UpdateRule
	{
		readonly string[] refineyValues = { "DockAngle", "IsDragRequired", "DragOffset", "DragLength" };
		readonly string[] harvesterValues = { "EnterCursor", "EnterBlockedCursor" };

		readonly Dictionary<string, List<MiniYamlNode>> refineryNodes = new Dictionary<string, List<MiniYamlNode>>();
		public override string Name => "Docking was abstracted from Refinery & Harvester.";

		public override string Description =>
			"Fields moved from Refinery to new trait DockHost, fields moved from Harvester to new trait DockClientManager and to DockHost";

		public override IEnumerable<string> BeforeUpdateActors(ModData modData, List<MiniYamlNode> resolvedActors)
		{
			var harvesters = new Dictionary<string, HashSet<string>>();
			var refineries = new List<string>();
			foreach (var actorNode in resolvedActors)
			{
				var harvesterNode = actorNode.ChildrenMatching("Harvester", includeRemovals: false).FirstOrDefault();
				if (harvesterNode != null)
					harvesters[actorNode.Key] = harvesterNode.ChildrenMatching("DeliveryBuildings", includeRemovals: false).FirstOrDefault()?.NodeValue<HashSet<string>>() ?? new HashSet<string>();

				var refineryNode = actorNode.ChildrenMatching("Refinery", includeRemovals: false).FirstOrDefault() ??
					actorNode.ChildrenMatching("TiberianSunRefinery", includeRemovals: false).FirstOrDefault();
				if (refineryNode != null)
					refineries.Add(actorNode.Key);
			}

			foreach (var harvester in harvesters)
			{
				foreach (var deliveryBuilding in harvester.Value)
				{
					foreach (var refinery in refineries)
					{
						if (refinery == deliveryBuilding)
						{
							if (!refineryNodes.ContainsKey(refinery))
								refineryNodes[refinery] = new List<MiniYamlNode>();

							var node = new MiniYamlNode("DockType", deliveryBuilding.ToString());
							if (!refineryNodes[refinery].Any(n => n.Key == node.Key))
								refineryNodes[refinery].Add(node);
						}
					}
				}
			}

			yield break;
		}

		public override IEnumerable<string> UpdateActorNode(ModData modData, MiniYamlNode actorNode)
		{
			var refineryNode = actorNode.ChildrenMatching("Refinery", includeRemovals: false).FirstOrDefault() ??
				actorNode.ChildrenMatching("TiberianSunRefinery", includeRemovals: false).FirstOrDefault();

			if (refineryNode != null)
			{
				var dockNode = new MiniYamlNode("DockHost", "");

				dockNode.AddNode("Type", "Unload");

				foreach (var value in refineyValues)
				{
					var node = refineryNode.ChildrenMatching(value, includeRemovals: false).FirstOrDefault();
					if (node != null)
					{
						dockNode.AddNode(node);
						refineryNode.RemoveNode(node);
					}
				}

				if (!refineryNodes.ContainsKey(actorNode.Key) || !refineryNodes[actorNode.Key].Any(n => n.Key == "DockType"))
					dockNode.AddNode("DockType", "Unload");
				else
					dockNode.AddNode(refineryNodes[actorNode.Key].First(n => n.Key == "DockType"));

				var oldOffset = CVec.Zero;
				var dockOffsetNode = refineryNode.ChildrenMatching("DockOffset", includeRemovals: false).FirstOrDefault();
				if (dockOffsetNode != null)
				{
					oldOffset = dockOffsetNode.NodeValue<CVec>();
					refineryNode.RemoveNode(dockOffsetNode);
				}

				// TODO: This conversion isn't correct
				var newOffset = new WVec(oldOffset.X, oldOffset.Y, 0);
				dockNode.AddNode("DockOffset", newOffset.ToString());

				actorNode.AddNode(dockNode);
			}

			var harvesterNode = actorNode.ChildrenMatching("Harvester", includeRemovals: false).FirstOrDefault();
			if (harvesterNode != null)
			{
				var dockClientNode = new MiniYamlNode("DockClientManager", "");

				foreach (var value in harvesterValues)
				{
					var node = harvesterNode.ChildrenMatching(value, includeRemovals: false).FirstOrDefault();
					if (node != null)
					{
						dockClientNode.AddNode(node);
						harvesterNode.RemoveNode(node);
					}
				}

				var nodeVoice = harvesterNode.ChildrenMatching("DeliverVoice", includeRemovals: false).FirstOrDefault();
				if (nodeVoice != null)
				{
					harvesterNode.RemoveNode(nodeVoice);
					nodeVoice.RenameKey("Voice");
					dockClientNode.AddNode(nodeVoice);
				}

				var nodeColor = harvesterNode.ChildrenMatching("DeliverLineColor", includeRemovals: false).FirstOrDefault();
				if (nodeColor != null)
				{
					harvesterNode.RemoveNode(nodeColor);
					nodeColor.RenameKey("DockLineColor");
					dockClientNode.AddNode(nodeColor);
				}

				var nodeQueue = harvesterNode.ChildrenMatching("UnloadQueueCostModifier", includeRemovals: false).FirstOrDefault();
				if (nodeQueue != null)
				{
					harvesterNode.RemoveNode(nodeQueue);
					nodeQueue.RenameKey("OccupancyCostModifier");
					dockClientNode.AddNode(nodeQueue);
				}

				var nodeDelay = harvesterNode.ChildrenMatching("SearchForDeliveryBuildingDelay", includeRemovals: false).FirstOrDefault();
				if (nodeDelay != null)
				{
					harvesterNode.RemoveNode(nodeDelay);
					nodeDelay.RenameKey("SearchForDockDelay");
					dockClientNode.AddNode(nodeDelay);
				}

				harvesterNode.RenameChildrenMatching("DeliveryBuildings", "DockType", includeRemovals: false);
				harvesterNode.RemoveNodes("MaxUnloadQueue", includeRemovals: false);

				actorNode.AddNode(dockClientNode);
			}

			yield break;
		}
	}
}
