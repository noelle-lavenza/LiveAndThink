using System;
using System.Reflection;
using System.Reflection.Emit;
using System.Collections.Generic;
using System.Linq;
using XRL.World;
using XRL.World.Capabilities;
using HarmonyLib;
using XRL.World.Parts;
using XRL.World.Parts.Mutation;
using XRL.UI;
using static LiveAndThink.Harmony.CodeMatcherExtensions;

namespace LiveAndThink.Disarm
{
	/// <summary>
	/// Modify Disarm(GameObject Object, GameObject Disarmer, ...) to
	/// make the disarmed creature try to re-equip its weapon.
	/// </summary>
	[HarmonyPatch]
	public static class DisarmingPatch
	{
		[HarmonyPatch(typeof(Disarming), nameof(Disarming.Disarm))]
		public static void Postfix(GameObject Object, GameObject __result)
		{
			if (__result == null || Object.Brain== null)
			{
				return;
			}
			if(Options.GetOption("OptionDisarmReequip") != "Yes")
			{
				return;
			}
			if(Object.IsPlayer())
			{
				return;
			}
			if (Options.GetOption("OptionReequipSearch") == "Yes")
			{
				Object.Brain.PushGoal(new ReequipOrFindNew(__result));
			}
			else
			{
				Object.Brain.PushGoal(new EquipObject(__result));
			}
		}
	}
	/// <summary>
	/// Modify MagneticPulse.EmitMagneticPulse using a transpiler to
	/// make the disarmed creature try to re-equip its weapon.
	/// </summary>
	[HarmonyPatch]
	public static class MagneticPulsePatch
	{
		static void ReequipHelper(GameObject who, GameObject what)
		{
			if(Options.GetOption("OptionPulseReequip") != "Yes")
			{
				return;
			}
			if(who.IsPlayer())
			{
				return;
			}
			if (who?.Brain!= null && what != null)
			{
				if (Options.GetOption("OptionReequipSearch") == "Yes")
				{
					who.Brain.PushGoal(new ReequipOrFindNew(what));
				}
				else
				{
					who.Brain.PushGoal(new EquipObject(what));
				}
			}
		}

		[HarmonyPatch(typeof(MagneticPulse), nameof(MagneticPulse.EmitMagneticPulse))]
		static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
		{
			CodeMatcher codeMatcher = new CodeMatcher(instructions);
			LocalBuilder local14 = codeMatcher.GetLocalBuilder(14);
			LocalBuilder local17 = codeMatcher.GetLocalBuilder(17);
			LocalBuilder local19 = codeMatcher.GetLocalBuilder(19);
			Label label0 = generator.DefineLabel();
			return codeMatcher
				.MatchEndForward(
					new CodeMatch(CodeInstruction.Call(typeof(XRL.UI.Popup), nameof(XRL.UI.Popup.ShowSpace)))
				)
				.AddLabels(new List<Label> {label0})
				.Advance(1)
				.Insert(
					new CodeInstruction(OpCodes.Ldloc_S, local19).MoveBlocksFrom(codeMatcher.InstructionAt(-1)),
					new CodeInstruction(OpCodes.Brfalse, label0),
					new CodeInstruction(OpCodes.Ldloc_S, local17),
					CodeInstruction.LoadField(local17.LocalType, "affectedObject"),
					CodeInstruction.Call(typeof(GameObject), nameof(GameObject.IsPlayer)),
					new CodeInstruction(OpCodes.Brtrue, label0),
					new CodeInstruction(OpCodes.Ldloc_S, local17),
					CodeInstruction.LoadField(local17.LocalType, "affectedObject"),
					new CodeInstruction(OpCodes.Ldloc_S, local19),
					CodeInstruction.Call(typeof(MagneticPulsePatch), nameof(ReequipHelper))
				)
				.InstructionEnumeration();
		}
	}
}