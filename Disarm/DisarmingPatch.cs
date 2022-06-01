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
		public static void Postfix(GameObject Object, GameObject Disarmer, string DisarmerStat, GameObject DisarmingWeapon, GameObject __result)
		{
			if (__result == null || Object.pBrain == null)
			{
				return;
			}
			if(Options.GetOption("OptionDisarmReequip") != "Yes")
			{
				return;
			}
			if (Options.GetOption("OptionReequipSearch") == "Yes")
			{
				Object.pBrain.PushGoal(new ReequipOrFindNew(__result));
			}
			else
			{
				Object.pBrain.PushGoal(new EquipObject(__result));
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
			if (who?.pBrain != null && what != null)
			{
				if (Options.GetOption("OptionReequipSearch") == "Yes")
				{
					who.pBrain.PushGoal(new ReequipOrFindNew(what));
				}
				else
				{
					who.pBrain.PushGoal(new EquipObject(what));
				}
			}
		}

		[HarmonyPatch(typeof(MagneticPulse), nameof(MagneticPulse.EmitMagneticPulse))]
		static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
		{
			var codes = new List<CodeInstruction>(instructions);
			int startidx = -1;
			LocalBuilder local14 = null;
			LocalBuilder local17 = null;
			LocalBuilder local19 = null;
			Label label0 = generator.DefineLabel();
			for (int i=0; i<codes.Count - 3; i++)
			{
				if (codes[i].opcode == OpCodes.Ldloc_S || codes[i].opcode == OpCodes.Ldloca_S || codes[i].opcode == OpCodes.Stloc_S)
				{
					LocalBuilder local = (LocalBuilder)codes[i].operand;
					if (local.LocalIndex == 14)
					{
						local14 = local;
					}
					else if (local.LocalIndex == 17)
					{
						local17 = local;
					}
					else if (local.LocalIndex == 19)
					{
						local19 = local;
					}
				}
				if (local14 != null && codes[i-1].opcode == OpCodes.Call
				&& codes[i].Is(OpCodes.Ldloca_S, local14)
				&& codes[i+1].opcode == OpCodes.Call
				&& codes[i+2].opcode == OpCodes.Brtrue)
				{
					startidx = i;
					break;
				}
			}
			// ldloc.s 19
			var jumpLoadInstruction = new CodeInstruction(OpCodes.Ldloc_S, local19); // what needs to be re-equipped
			// brfalse.s label0
			var branchInstruction = new CodeInstruction(OpCodes.Brfalse, label0);
			// ldloc.s 17
			var loadInstruction = new CodeInstruction(OpCodes.Ldloc_S, local17);
			// ldfld class XRL.World.GameObject XRL.World.Parts.Mutation.MagneticPulse/'<>c__DisplayClass17_1'::affectedObject
			var fieldInstruction = CodeInstruction.LoadField(local17.LocalType, "affectedObject"); // who needs to re-equip
			// callvirt instance bool XRL.World.GameObject::IsPlayer()
			var callInstruction = new CodeInstruction(OpCodes.Callvirt, AccessTools.Method(typeof(GameObject), nameof(GameObject.IsPlayer)));
			// brtrue.s label0
			var branchInstruction2 = new CodeInstruction(OpCodes.Brtrue, label0);

			// ldloc.s 17 again
			// ldfld class XRL.World.GameObject XRL.World.Parts.Mutation.MagneticPulse/'<>c__DisplayClass17_1'::affectedObject again
			// ldloc.s 19 again, but no jump
			var loadInstruction2 = new CodeInstruction(OpCodes.Ldloc_S, local19); // what needs to be re-equipped
			// call ReequipHelper
			var callInstruction2 = CodeInstruction.Call(typeof(MagneticPulsePatch), nameof(ReequipHelper));
			jumpLoadInstruction.labels = new List<Label>(codes[startidx].labels);
			codes[startidx].labels = new List<Label>(){label0};
			// insert instructions
			codes.InsertRange(startidx, new CodeInstruction[] { jumpLoadInstruction, branchInstruction, loadInstruction, fieldInstruction, callInstruction, branchInstruction2, loadInstruction, fieldInstruction, loadInstruction2, callInstruction2 });
			return codes;
		}
	}
}