using System;
using System.Reflection;
using System.Reflection.Emit;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using XRL.UI;
using XRL.World;
using XRL.World.AI.GoalHandlers;
using XRL.World.Parts;
using XRL.Rules;
using XRL;

namespace LiveAndThink.Logic
{
	/// <summary>
	/// Modify Kill.TakeAction to change the distance to
	/// stop pursuing the enemy at from 1 tile away to 2.
	/// </summary>
	[HarmonyPatch]
	public static class MetaMelee
	{
		public static int StopDistance(Kill Self, int lastAttacked)
		{
			if (Options.GetOption("OptionMetaMelee", "No") == "Yes")
			{
				// Check ConTarget to see if our enemy is is as strong as or stronger than us.
				// That means ConTarget(Other) >= 0.8.
				// If ConTarget(Other) >= 2, then we might flee instead if the enemy is too scary.
				float conTarget = Self.ParentBrain.ConTarget(Self.Target);
				if (conTarget >= 4 && !Self.ParentObject.MakeSave("Willpower", (int) (conTarget * 2), Self.Target, "Ego"))
				{
					Self.ParentBrain.PushGoal(new Retreat(Stat.Random(30, 50)));
					return int.MaxValue; // Don't ever advance!
				}
				else if (conTarget >= 0.8)
				{
					// Make a contested willpower roll.
					// For every 10 turns the fight goes on, the difficulty is increased by 1.
					if(!Self.ParentObject.MakeSave("Willpower", lastAttacked / 10, Self.Target) && lastAttacked > 5)
					{
						return 1; // Losing our patience, go in for the kill!
					}
					return 2; // Patient enough to wait.
				}
				return 1; // We're not afraid of this enemy!
			}
			return 1;
		}

		// Find index of `ldstr "I'm close enough to my target."`
		// Index of modified instruction is seven instructions before that.
		// Change OpCodes.Ldc_I4_1 to a call to the StopDistance helper.
		[HarmonyPatch(typeof(Kill), nameof(Kill.TakeAction))]
		static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
		{
			return new CodeMatcher(instructions)
				.MatchStartForward(
					new CodeMatch(OpCodes.Ldloc_2),
					new CodeMatch(OpCodes.Ldc_I4_1),
					new CodeMatch(OpCodes.Bgt_S)
				).Advance(1)
				.ThrowIfInvalid("LiveAndThink.MetaMelee: Unable to find num <= 1 injection point.")
				.InsertAndAdvance(
					new CodeInstruction(OpCodes.Ldarg_0),
					new CodeInstruction(OpCodes.Ldarg_0),
					CodeInstruction.LoadField(typeof(Kill), "LastAttacked"))
				.SetInstruction(CodeInstruction.Call(typeof(MetaMelee), nameof(StopDistance)))
				.InstructionEnumeration();
		}
	}
}

