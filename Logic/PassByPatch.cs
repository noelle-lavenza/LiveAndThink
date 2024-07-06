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

		public static void Mark(this CodeMatcher instance)
		{
			
		}

		[HarmonyPatch(typeof(Step), nameof(Step.TakeAction))]
		static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
		{
			Label return_label = generator.DefineLabel();
			return new CodeMatcher(instructions)
				.MatchStartForward(
					new CodeMatch(OpCodes.Ldarg_0),
					new CodeMatch(OpCodes.Ldloc_0),
					new CodeMatch(CodeInstruction.Call(typeof(Step), "CellHasHostile")) // private, must use string instead of nameof()
				)
				.ThrowIfInvalid("LiveAndThink.Logic.PassByPatch: Unable to find CellHasHostile injection point!")
				.Insert(
					new CodeInstruction(OpCodes.Ldarg_0),
					new CodeInstruction(OpCodes.Ldloc_0),
					CodeInstruction.Call(typeof(PassByPatch), nameof(PassByPatch.AttackBlockerIfPresent)),
					new CodeInstruction(OpCodes.Brtrue_S, return_label)
				)
				// find the return so we can avoid FailToParent
				.MatchStartForward(
					new CodeMatch(OpCodes.Ret)
				)
				.AddLabels(new List<Label> {return_label})
				.MatchEndForward(
					new CodeMatch(code => code.LoadsConstant("There's something in my way!"))
				)
				.ThrowIfInvalid("LiveAndThink.Logic.PassByPatch: Unable to find Think(\"There's something in my way!\") injection point.")
				.SetOperandAndAdvance("There's someone non-hostile in my way!")
				.InstructionEnumeration();
		}
	}
}
