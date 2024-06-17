using System;
using System.Reflection.Emit;
using System.Collections.Generic;
using XRL.World;
using HarmonyLib;
using XRL.World.Parts.Mutation;
using XRL.UI;

/// <summary>
/// A collection of Harmony patches that make creatures
/// use their activated abilities/mutations more wisely.
namespace LiveAndThink.SmartUse
{
	/// <summary>
	/// Modify Quills.FireEvent/AIGetOffensiveMutationList to
	/// prevent using quills when adjacent to a friendly creature.
	/// </summary>
	[HarmonyPatch]
	public static class QuillsPatch
	{
		static bool QuillsFriendSafetyCheck(Quills quills)
		{
			if (Options.GetOption("OptionSafeQuills")  != "Yes")
			{
				return true;
			}
			List<Cell> adjacentCells = quills.ParentObject.CurrentCell.GetAdjacentCells();
			if (adjacentCells.Count <= 0)
			{
				return false;
			}
			foreach (Cell cell in adjacentCells)
			{
				foreach (GameObject bystander in cell.GetObjectsWithPartReadonly("Combat"))
				{
					if (bystander.HasPart("Combat") && !(bystander.GetPart<XRL.World.Parts.Mutations>()?.HasMutation("Quills") ?? false) && !quills.ParentObject.Brain.IsHostileTowards(bystander))
					{
						return false;
					}
				}
			}
			return true;
		}

		[HarmonyPatch(typeof(Quills), nameof(Quills.FireEvent))]
		static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
		{
			var codes = new List<CodeInstruction>(instructions);
			// Insertion point:
			// call instance bool class XRL.World.IComponent`1<class XRL.World.GameObject>::IsMyActivatedAbilityAIUsable(valuetype [mscorlib]System.Guid, class XRL.World.GameObject)
			// Plus two.
			// Duplicate jump instruction before.
			// codes.InsertRange(startidx, new CodeInstruction[] {
			// new CodeInstruction(OpCodes.Ldarg_0)
			// CodeInstruction.Call(typeof(QuillsPatch), nameof(QuillsFriendSafetyCheck))
			// codes[startidx-1].Clone()
			// };
			int startidx = -1;
			for (int i=0; i<codes.Count; i++)
			{
				if (codes[i].Is(OpCodes.Call, AccessTools.Method(typeof(IComponent<GameObject>), nameof(IComponent<GameObject>.IsMyActivatedAbilityAIUsable))))
				{
					startidx = i+2;
					break;
				}
			}
			codes.InsertRange(startidx, new CodeInstruction[] {
				new CodeInstruction(OpCodes.Ldarg_0),
				CodeInstruction.Call(typeof(QuillsPatch), nameof(QuillsFriendSafetyCheck)),
				codes[startidx-1].Clone()});
			return codes;
		}
	}
}
