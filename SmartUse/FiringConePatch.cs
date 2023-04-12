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
		/// Modify Kill.TryMissileWeapon to replace Zone.Line with FiringConePatch.GetFiringCone.
		/// and if there are friendly creatures in its danger radius.
		/// </summary>
		// call class [mscorlib]System.Collections.Generic.List`1<class XRL.World.Point> XRL.World.Zone::Line(int32, int32, int32, int32)
		// minus eight
		// Inject eight instructions before call Zone::Line
		// and call FiringConePatch::GetFiringCone.
		// Then replace !IsHostileTowards with Bystander.IsBystander.
		[HarmonyPatch(typeof(Kill), nameof(Kill.TryMissileWeapon))]
		static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
		{
			var codes = new List<CodeInstruction>(instructions);
			LocalBuilder local9 = (LocalBuilder) codes.Find(x => x.opcode == OpCodes.Ldloc_S && ((LocalBuilder)x.operand).LocalIndex == (byte) 9).operand;
			int callidx = codes.FindIndex(x => x.Calls(AccessTools.Method(typeof(Zone), "Line")));
			int startidx = callidx - 8;
			if (callidx == -1)
			{
				return instructions; // do nothing
			}
			codes[callidx] = CodeInstruction.Call(typeof(FiringConePatch), nameof(GetFiringCone));
			// ldarg.0; call get ParentObject
			// ldarg.0; call get Target
			// ldloc.s 9
			// List<Label> labels = new List<Label>(codes[startidx].labels); // there aren't actually any labels there
			codes.RemoveRange(startidx, 8);
			codes.InsertRange(startidx, new List<CodeInstruction> {
				new CodeInstruction(OpCodes.Ldarg_0),
				new CodeInstruction(OpCodes.Call, AccessTools.PropertyGetter(typeof(Kill), nameof(Kill.ParentObject))),
				new CodeInstruction(OpCodes.Ldarg_0),
				new CodeInstruction(OpCodes.Call, AccessTools.PropertyGetter(typeof(Kill), nameof(Kill.Target))),
				new CodeInstruction(OpCodes.Ldloc_S, local9)
			});
			// codes[startidx].labels = labels;
			// now we replace IsHostileTowards.
			int hostileidx = codes.FindIndex(x => x.Calls(AccessTools.Method(typeof(Brain), "IsHostileTowards")));
			if (hostileidx == -1)
			{
				return codes; // do no further modifications
			}
			codes[hostileidx].operand = AccessTools.Method(typeof(Bystander), nameof(Bystander.IsBystander));
			codes[hostileidx+1].opcode = OpCodes.Brfalse_S;
			return codes;
		}

		static List<Point> GetFiringCone(GameObject firingObject, GameObject targetObject, GameObject missileWeaponObject)
		{
			Cell currentCell = firingObject.CurrentCell;
			Cell targetCell = targetObject.CurrentCell;
			// Use a simple straight line check if int < 12 or if the feature is off.
			if (Options.GetOption("OptionFiringCone", "No") != "Yes" || firingObject.Stat("Intelligence") < 12) // below 12 int or with it off, we just check for a clear line of sight
			{
				return Zone.Line(currentCell.X, currentCell.Y, targetCell.X, targetCell.Y);
			}
			// Variance compensation if int >=18, tactics skill, weapon skill, or Artillery role
			// Not handled here: Careful aiming (cares about neutral bystanders) if not aggressive/hostile
			string combatRole = targetObject.GetTagOrStringProperty("Role", "Minion");
			MissileWeapon missileWeapon = missileWeaponObject.GetPart<MissileWeapon>();
			bool isSkilled = missileWeapon.IsSkilled(firingObject);
			bool doVarianceCompensation = firingObject.Stat("Intelligence") >= 18 || firingObject.HasSkill("Tactics") || isSkilled || combatRole == "Artillery";
			double aimPenalty = -firingObject.StatMod(missileWeapon.Modifier);
			double unskilledPenalty = (doVarianceCompensation || missileWeaponObject.IsNatural()) ? 1 : 3; // if you aren't familiar with it you underestimate it
			aimPenalty += ((double) missileWeapon.WeaponAccuracy / unskilledPenalty); // account for its inherent inaccuracy
			aimPenalty -= ((double) missileWeaponObject.GetIntProperty("MissileWeaponAccuracyBonus") / unskilledPenalty); // and its inherent accuracy
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
				return Zone.Line(currentCell.X, currentCell.Y, targetCell.X, targetCell.Y);
			}
			Location2D startLocation = currentCell.location;
			Location2D endLocation = targetCell.location;
			List<Location2D> result = new List<Location2D>();
			GetCone(startLocation, endLocation, currentCell.PathDistanceTo(targetCell) + 1, (int) (aimPenalty * 2), result); // * 2 because it's both sides
			ScreenBuffer Buffer = ScreenBuffer.GetScrapBuffer1();
			if (Options.GetOption("OptionFiringConeDebug", "No") == "Yes" && firingObject.InActiveZone())
			{
				while (true)
				{
					Buffer.RenderBase();
					for (int l = 0; l < result.Count; l++)
					{
						Buffer.Goto(result[l].x, result[l].y);
						Cell cell3 = currentCell.ParentZone.GetCell(result[l]);
						Buffer.Write("&CX");
					}
					Buffer.Draw();
					if (Keyboard.kbhit())
					{
						break;
					}
				}
			}
			return result.Select(location => new Point(location.x, location.y)).ToList();
		}
	}
}