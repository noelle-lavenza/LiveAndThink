
using System;
using System.Collections.Generic;
using System.Reflection.Emit;
using HarmonyLib;
using XRL.World;

namespace LiveAndThink.Logic
{
	/// <summary>
	/// Patches IComponent.ConTarget to allow the player's ConTarget to go below and above 1.
	/// </summary>
	[HarmonyPatch]
	public static class ConTargetPatch
	{
		[HarmonyPatch(typeof(IComponent<GameObject>), "ConTarget", new Type[] { typeof(GameObject) })]
		static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
		{
			CodeMatcher codeMatcher = new CodeMatcher(instructions)
				.MatchStartForward(
					new CodeMatch(code => code.Calls(AccessTools.Method(typeof(GameObject), nameof(GameObject.IsPlayer))))
				)
				.Advance(4)
				.ThrowIfInvalid("LiveAndThink.ConTargetPatch: Could not find IsPlayer injection site!");
			// Remove the IsPlayer early return.
			return codeMatcher.AddLabels(codeMatcher.InstructionAt(-5).ExtractLabels())
				.RemoveInstructionsWithOffsets(-5, -3)
				.InstructionEnumeration();
		}
		/*
		[HarmonyPatch(typeof(IComponent<GameObject>), "ConTarget", new Type[] { typeof(GameObject) })]
		static void Postfix(ref float __result, GameObject target)
		{
			if (target.IsPlayer())
			{
				__result = Math.Max(1, __result);
			}
		}
		*/
	}
}
