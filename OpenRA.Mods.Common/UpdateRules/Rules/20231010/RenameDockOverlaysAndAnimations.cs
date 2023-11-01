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
	public class RenameDockOverlaysAndAnimations : UpdateRule, IBeforeUpdateActors
	{
		public override string Name => "WithDockedOverlay was removed.";

		public override string Description =>
			"WithDockedOverlay was removed, use WithIdleOverlay instead combined with GrantConditionOnDock instead.";

		public IEnumerable<string> BeforeUpdateActors(ModData modData, List<MiniYamlNodeBuilder> resolvedActors)
		{
			yield break;
		}

		public override IEnumerable<string> UpdateActorNode(ModData modData, MiniYamlNodeBuilder actorNode)
		{
			var dockedOverlays = actorNode.ChildrenMatching("WithDockedOverlay").ToList();
			if (dockedOverlays.Count == 0)
				yield break;

			foreach (var docked in dockedOverlays)
			{
				docked.RenameKey("WithIdleOverlay");
				var condition = docked.LastChildMatching("RequiresCondition");
				if (condition == null)
					docked.AddNode("RequiresCondition", "unloading");
				else
					condition.Value.Value = $"({condition.Value.Value}) && unloading";
			}

			var overlay = new MiniYamlNodeBuilder("GrantConditionOnDock", "");
			overlay.AddNode("Condition", "unloading");
			actorNode.AddNode(overlay);
		}
	}
}
