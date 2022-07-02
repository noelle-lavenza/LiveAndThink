using System.Collections.Generic;
using System.Reflection.Emit;
using HarmonyLib;
using XRL.UI;
using XRL.World;
using XRL.World.AI.GoalHandlers;

namespace LiveAndThink.Logic
{
	/// <summary>
	/// Patches Step.TakeAction to make enemies target creatures blocking their path.
	/// </summary>
	[HarmonyPatch]
	public static class PassByPatch
	{
		private static bool AttackBlockerIfPresent(this Step inst, Cell dest)
		{
			// UnityEngine.Debug.Log($"{inst.ParentObject.DebugName}.AttackBlockerIfPresent - Start");
			if (Options.GetOption("OptionPassBy") != "Yes")
			{
				// UnityEngine.Debug.Log($"{inst.ParentObject.DebugName}.AttackBlockerIfPresent - Disabled");
				return false;
			}
			// UnityEngine.Debug.Log($"\t{inst.ParentObject.DebugName}.AttackBlockerIfPresent - Checking for blocker");
			GameObject blocker = dest?.GetFirstObjectWithPart("Combat", x => (inst.ParentBrain.IsHostileTowards(x) && inst.ParentObject.PhaseAndFlightMatches(x)));
			if (blocker == null)
			{
				// UnityEngine.Debug.Log($"{inst.ParentObject.DebugName}.AttackBlockerIfPresent - Found no blocker");
				return false;
			}
			inst.ParentBrain.WantToKill(blocker, "because they're in my way");
			// UnityEngine.Debug.Log($"{inst.ParentObject.DebugName}.AttackBlockerIfPresent - Found blocker");
			return true;
		}

		[HarmonyPatch(typeof(Step), nameof(Step.TakeAction))]
		static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
		{
			var codes = new List<CodeInstruction>(instructions);
			var idx = codes.FindLastIndex(x => x.Is(OpCodes.Ldstr, "There's something in my way!"));
			if (idx != -1)
			{
				codes[idx].operand = "There's someone non-hostile in my way!";
			}
			idx = codes.FindIndex(x => x.Calls(AccessTools.Method(typeof(Step), "CellHasHostile"))) - 2;
			if (idx != -1)
			{
				Label retLabel = generator.DefineLabel();
				codes[idx+18].labels.Add(retLabel);
				codes.Insert(idx, new CodeInstruction(OpCodes.Brtrue_S, retLabel));
				codes.Insert(idx, CodeInstruction.Call(typeof(PassByPatch), nameof(PassByPatch.AttackBlockerIfPresent)));
				codes.Insert(idx, new CodeInstruction(OpCodes.Ldloc_0));
				codes.Insert(idx, new CodeInstruction(OpCodes.Ldarg_0));
			}
			return codes;
		}
	}
}
