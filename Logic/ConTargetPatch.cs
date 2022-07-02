
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
			var codes = new List<CodeInstruction>(instructions);
			var index = codes.FindIndex(x => x.Is(OpCodes.Callvirt, AccessTools.Method(typeof(GameObject), nameof(GameObject.IsPlayer))));
			if (index != -1)
			{
				// Move the label on index - 1 to index + 4.
				codes[index + 4].labels.AddRange(codes[index - 1].labels);
				// Remove all code between callvirt IsPlayer - 1 and callvirt IsPlayer + 3.
				codes.RemoveRange(index - 1, 5);
			}
			var tagIndex = codes.FindIndex(x => x.Is(OpCodes.Callvirt, AccessTools.Method(typeof(GameObject), nameof(GameObject.GetTag))));
			if (tagIndex != -1)
			{
				// Replace GetTag with GetPropertyOrTag.
				codes[tagIndex].operand = AccessTools.Method(typeof(GameObject), nameof(GameObject.GetPropertyOrTag));
			}
			// We add a new IsPlayer check in the postfix.
			return codes;
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
