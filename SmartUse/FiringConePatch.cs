using System.Collections.Generic;
using HarmonyLib;
using XRL.World;
using XRL.World.Effects;
using XRL.World.Parts;
using static XRL.Rules.Geometry;
using Genkit;
using System.Linq;
using XRL.World.AI.GoalHandlers;
using System.Reflection.Emit;
using LiveAndThink.Logic;
using ConsoleLib.Console;
using XRL.UI;
using XRL.World.AI;

namespace LiveAndThink.SmartUse
{
	/// <summary>
	/// Modify Kill.TryMissileWeapon to check a firing cone
	/// rather than a strict firing line.
	/// </summary>
	[HarmonyPatch]
	public static class FiringConePatch
	{
		/// <summary>
		/// Modify Kill.TryMissileWeapon to check FiringConePatch.CheckFiringCone.
		/// and if there are friendly creatures in its danger radius.
		/// </summary>
		// ldstr "I'm going to fire one or more missile weapons, probably my "
		// one instruction before
		// call FiringConePatch::GetFiringCone.
		// Then replace !IsHostileTowards with Bystander.IsBystander.
		[HarmonyPatch(typeof(Kill), nameof(Kill.TryMissileWeapon))]
		static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
		{
			CodeMatcher codeMatcher = new CodeMatcher(instructions)
				.MatchStartForward(
					new CodeMatch(OpCodes.Ldloc_S, name: "getLocalMissileWeapon"),
					new CodeMatch(OpCodes.Ldarg_0),
					new CodeMatch(code => code.Calls(AccessTools.DeclaredPropertyGetter(typeof(GoalHandler), nameof(GoalHandler.ParentObject)))),
					new CodeMatch(OpCodes.Ldloc_0),
					new CodeMatch(code => code.Calls(AccessTools.DeclaredMethod(typeof(AIWantUseWeaponEvent), nameof(AIWantUseWeaponEvent.Check))))
				).ThrowIfInvalid("LiveAndThink.SmartUse.FiringConePatch: Unable to find weapon GameObject local variable.");
			CodeInstruction getLocalMissileWeapon = codeMatcher.NamedMatch("getLocalMissileWeapon");
			Label afterLabel = generator.DefineLabel();
			codeMatcher.MatchStartForward(
				new CodeMatch(OpCodes.Ldarg_0),
				new CodeMatch(code => code.LoadsConstant("I'm going to fire one or more missile weapons, probably my "))
			).AddLabels(new List<Label> {afterLabel})
			.Insert(
				new CodeInstruction(OpCodes.Ldarg_0),
				new CodeInstruction(OpCodes.Call, AccessTools.PropertyGetter(typeof(Kill), nameof(Kill.ParentObject))),
				new CodeInstruction(OpCodes.Ldloc_0), // targetObject
				getLocalMissileWeapon.Clone(), // load weapon var
				CodeInstruction.Call(typeof(FiringConePatch), nameof(CheckFiringCone)),
				new CodeInstruction(OpCodes.Brtrue, afterLabel),
				new CodeInstruction(OpCodes.Ldc_I4_0),
				new CodeInstruction(OpCodes.Ret)
			);
			// now we replace IsHostileTowards with IsBystander and return
			return codeMatcher
				.MatchStartBackwards(
					new CodeMatch(code => code.Calls(AccessTools.Method(typeof(Brain), nameof(Brain.IsHostileTowards))))
				)
				.InsertAndAdvance( // add an extra false argument, to excluse self from the IsBystander check
					new CodeInstruction(OpCodes.Ldc_I4_0)
				)
				.SetOperandAndAdvance(AccessTools.Method(typeof(Bystander), nameof(Bystander.IsBystander)))
				.SetOpcodeAndAdvance(OpCodes.Brfalse_S) // invert check
				.InstructionEnumeration();
		}

		static bool CheckFiringCone(GameObject firingObject, GameObject targetObject, GameObject missileWeaponObject)
		{
			Cell currentCell = firingObject.CurrentCell;
			Cell targetCell = targetObject.CurrentCell;
			// Don't check for friendlies in the cone if INT < 12 or if the feature is off.
			if (Options.GetOption("OptionFiringCone", "No") != "Yes" || firingObject.Stat("Intelligence") < 12) // below 12 int or with it off, we just check for a clear line of sight
			{
				return true;
			}
			// Variance compensation if INT >=18, tactics skill, weapon skill, or Artillery role
			// Not handled here: Careful aiming (cares about neutral bystanders) if not aggressive/hostile
			string combatRole = targetObject.GetTagOrStringProperty("Role", "Minion");
			MissileWeapon missileWeapon = missileWeaponObject.GetPart<MissileWeapon>();
			bool isSkilled = missileWeapon.IsSkilled(firingObject);
			bool doVarianceCompensation = firingObject.Stat("Intelligence") >= 18 || firingObject.HasSkill("Tactics") || isSkilled || combatRole == "Artillery";
			double aimPenalty = -firingObject.StatMod(missileWeapon.Modifier);
			double unskilledPenalty = (doVarianceCompensation || missileWeaponObject.IsNatural()) ? 1 : 3; // if you aren't familiar with it you underestimate it
			aimPenalty += (double) missileWeapon.WeaponAccuracy / unskilledPenalty; // account for its inherent inaccuracy
			aimPenalty -= (double) missileWeaponObject.GetIntProperty("MissileWeaponAccuracyBonus") / unskilledPenalty; // and its inherent accuracy
			if (isSkilled)
			{
				aimPenalty -= 2;
			}
			aimPenalty += targetObject.GetIntProperty("IncomingAimModifier");
			if (targetObject.HasEffect("RifleMark") && targetObject.GetEffect<RifleMark>().Marker == firingObject)
			{
				aimPenalty -= 3; // include the -2 to AimLevel applied in Combat.cs on top of the -1 in MissileWeapon.cs
			}
			aimPenalty -= missileWeapon.AimVarianceBonus; // don't have to be skilled for a scope to help
			aimPenalty -= firingObject.GetIntProperty("MissileWeaponAccuracyBonus");
			if (doVarianceCompensation)
			{
				const double varianceStdDev = 4.72; // bad hardcoded numbers based on the |2d20-21| variance calculation
				const double varianceMean = 6.65; // ditto
				UnityEngine.Debug.Log($"Variance compensation for {firingObject.DebugName}: {(int)(2*(varianceMean + varianceStdDev))}");
				aimPenalty += varianceMean + varianceStdDev; // in case we wanna up it to two standard deviations later
			}
			UnityEngine.Debug.Log($"Aim penalty for {firingObject.DebugName}: {aimPenalty}");
			if(aimPenalty == 0) // Cone doesn't currently work properly with an angle of 0.
			{
				return true;
			}
			Location2D startLocation = currentCell.Location;
			Location2D endLocation = targetCell.Location;
			List<Location2D> result = new List<Location2D>();
			UnityEngine.Debug.Log($"Firing distance for {firingObject.DebugName}: {currentCell.PathDistanceTo(targetCell) + 1}");
			GetCone(startLocation, endLocation, currentCell.PathDistanceTo(targetCell) + 1, (int) (aimPenalty * 2), result); // * 2 because it's both sides
			ScreenBuffer Buffer = ScreenBuffer.GetScrapBuffer1();
			Zone parentZone = currentCell.ParentZone;
			if (Options.GetOption("OptionFiringConeDebug", "No") == "Yes" && firingObject.InActiveZone())
			{
				while (true)
				{
					Buffer.RenderBase();
					for (int l = 0; l < result.Count; l++)
					{
						Buffer.Goto(result[l].X, result[l].Y);
						Cell cell3 = parentZone.GetCell(result[l]);
						Buffer.Write("&CX");
					}
					Buffer.Draw();
					if (Keyboard.kbhit())
					{
						break;
					}
				}
			}
			return !result
				.Select(location => parentZone.GetCell(location))
				.Any(cell => cell.GetObjects(obj => obj.IsCombatObject())
					.Any(combatant => Bystander.IsBystander(firingObject.Brain, combatant)));
		}
	}
}