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

/// <summary>
/// A collection of Harmony patches that make creatures
/// use their activated abilities/mutations more wisely.
namespace LiveAndThink.SmartUse
{
	/// <summary>
	/// Modify AITonicUse.FireEvent using a transpiler to 
	/// lower the priority of the Apply command to 1.
	/// </summary>
	[HarmonyPatch]
	public static class TonicUsePatch
	{
		public static int GetTonicPriority()
		{
			if (Options.GetOption("OptionLowTonicPriority") == "Yes")
			{
				return 1;
			}
			return 100; // this is the default, don't ask me why
		}

		[HarmonyPatch(typeof(AITonicUse), nameof(AITonicUse.FireEvent))]
		static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
		{
			var codes = new List<CodeInstruction>(instructions);
			for (int i = 0; i < codes.Count; i++)
			{
				if (codes[i].opcode == OpCodes.Ldstr && codes[i].operand.ToString() == "Apply")
				{
					codes[i + 1] = CodeInstruction.Call(typeof(TonicUsePatch), nameof(GetTonicPriority));
					break;
				};
			}
			return codes;
		}
	}
}