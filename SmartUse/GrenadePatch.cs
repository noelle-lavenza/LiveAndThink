using System;
using System.Reflection.Emit;
using System.Collections.Generic;
using XRL.World;
using HarmonyLib;
using System.Reflection;
using XRL.World.Parts;
using XRL.World.AI.GoalHandlers;
using XRL.World.AI;
using System.Linq;
using XRL.UI;
using LiveAndThink.Logic;

/// <summary>
/// A collection of Harmony patches that make creatures
/// use their activated abilities/mutations more wisely.
namespace LiveAndThink.SmartUse
{
	/// <summary>
	/// Modify Kill.TryThrownWeapon to check if the thrown weapon is a grenade
	/// and if there are friendly creatures in its danger radius.
	/// </summary>
	[HarmonyPatch]
	public static class GrenadePatch
	{
		[NonSerialized]
		private static Dictionary<Type, MethodInfo> dangerRadiusLookup = new Dictionary<Type, MethodInfo>();
		[NonSerialized]
		private static Type[] dangerRadiusParameterList = new Type[1];
		
		[NonSerialized]
		private static Dictionary<Type, MethodInfo> canEndangerAllyLookup = new Dictionary<Type, MethodInfo>();
		[NonSerialized]
		private static Type[] canEndangerAllyParameterList = new Type[2] {null, typeof(GameObject)};

		/// <summary>
		/// Return the danger radius of an IGrenade part.
		/// </summary>
		public static int GetDangerRadius(IGrenade grenade)
		{
			MethodInfo dangerRadiusMethod = GetDangerRadiusMethod(grenade);
			if (dangerRadiusMethod == null)
			{
				return 0;
			}
			return (int)dangerRadiusMethod.Invoke(null, new object[1] {grenade});
		}

		public static bool CanEndangerAlly(IGrenade grenade, GameObject target)
		{
			MethodInfo canEndangerAllyMethod = GetCanEndangerAllyMethod(grenade);
			if (canEndangerAllyMethod == null)
			{
				return true;
			}
			return (bool)canEndangerAllyMethod.Invoke(null, new object[2] {grenade, target});
		}

		static MethodInfo GetCanEndangerAllyMethod(IGrenade grenade)
		{
			Type type = grenade.GetType();
			if (!canEndangerAllyLookup.TryGetValue(type, out MethodInfo canEndangerAllyMethod))
			{
				canEndangerAllyParameterList[0] = type;
				canEndangerAllyMethod = type.GetMethod("CanEndangerAlly", canEndangerAllyParameterList);
				if (canEndangerAllyMethod != null)
				{
					canEndangerAllyLookup[type] = canEndangerAllyMethod;
				}
			}
			return canEndangerAllyMethod;
		}

		static bool CanEndangerAlly(DeploymentGrenade grenade, GameObject target)
		{
			if (grenade.LoyalToThrower)
			{
				return false;
			}
			return true;
		}

		static bool CanEndangerAlly(GasGrenade grenade, GameObject target)
		{
			// First, get the GameObjectBlueprint from grenade.GasObject.
			// Then, get blueprint.GetPartParameter("Gas", "GasType").
			// Then, get the part of that type.
			// Then, based on the type of gas, check the custom logic for that gas.
			GameObject gasSample = GameObjectFactory.Factory.CreateSampleObject(grenade.GasObject);
			IPart gasPart = gasSample.GetPart(gasSample.GetPart<Gas>().GasType);
			if (gasPart == null)
			{
				return false;
			}
			if (!CheckGasCanAffectEvent.Check(target, gasSample))
			{
				return false;
			}
			if (gasPart is GasDamaging damaging)
			{
				return damaging.Match(target);
			}
			if (gasPart is GasPoison poison)
			{
				return poison.IsAffectable(target);
			}
			if (gasPart is GasStun stun)
			{
				return stun.IsAffectable(target);
			}
			if (gasPart is GasSleep sleep)
			{
				return sleep.IsAffectable(target);
			}
			if (gasPart is EMPGrenade)
			{
				return target.HasRegisteredEvent("ApplyEMP") || target.HasPart("Metal") || target.GetPartsDescendedFrom<IActivePart>().Any(x => x.IsEMPSensitive) || target.GetInstalledCyberneticsReadonly().Any(x => x.GetPartsDescendedFrom<IActivePart>().Any(y => y.IsEMPSensitive));
			}
			return true;
		}

		// Gas and Thermal grenades have an effective radius of 1 (all adjacent tiles) prior to spreading; ensuring that neither the user nor their allies are in that area should give them time to flee the effect.
		// Flashbang, EMP, Deployment, Sunder, Gravity, and Phase grenades have a fixed radius specified on their respective parts—however, FireSupport/microturret deployment grenades should probably not check that radius, as they won't pose a threat to the thrower or creatures neutral to them.
		// TimeDilation grenades don't have a fixed radius set on their part, but their generated field always has a radius of 9.
		// HEGrenades have an undetermined radius because of how explosions spread, but some arbitrary scaling with force would probably be okay to avoid creatures obliterating themselves and their allies with Hand-E-Nukes.

		/// <summary>
		/// GasGrenades have an effective danger radius of 1 (all adjacent tiles).
		/// </summary>
		static int GetDangerRadius(GasGrenade grenade)
		{
			return 1;
		}

		/// <summary>
		/// Thermal grenades have an effective danger radius of 1 (all adjacent tiles).
		/// </summary>
		static int GetDangerRadius(ThermalGrenade grenade)
		{
			return 1;
		}

		/// <summary>
		/// Flashbang grenades have a Radius field.
		/// </summary>
		static int GetDangerRadius(FlashbangGrenade grenade)
		{
			return grenade.Radius;
		}

		/// <summary>
		/// EMP grenades have a Radius field.
		/// </summary>
		static int GetDangerRadius(EMPGrenade grenade)
		{
			return grenade.Radius;
		}

		/// <summary>
		/// Deployment grenades have a Radius field, but if LoyalToThrower is true, it has an effective radius of 0.
		/// </summary>
		static int GetDangerRadius(DeploymentGrenade grenade)
		{
			return grenade.Radius;
		}

		/// <summary>
		/// Sunder grenades have a Radius field.
		/// </summary>
		static int GetDangerRadius(SunderGrenade grenade)
		{
			return grenade.Radius;
		}

		/// <summary>
		/// Gravity grenades have a Radius field.
		/// </summary>
		static int GetDangerRadius(GravityGrenade grenade)
		{
			return grenade.Radius;
		}

		/// <summary>
		/// Phase grenades have a Radius field, implemented as a die roll.
		/// </summary>
		static int GetDangerRadius(PhaseGrenade grenade)
		{
			return XRL.Rules.Stat.RollMax(grenade.Radius.ToString());
		}

		/// <summary>
		/// TimeDilation grenades have an effective danger radius of 0, since they don't hurt anything.
		/// </summary>
		static int GetDangerRadius(TimeDilationGrenade grenade)
		{
			return 0;
		}

		/// <summary>
		/// HEGrenades have a danger radius scaling with their Force field.
		/// The force ranges from 1200 to 10000000.
		/// 2000 has a radius of about 4, 7000 has a radius of about 8, 15000 has a radius of about 12.
		/// </summary>
		static int GetDangerRadius(HEGrenade grenade)
		{
			return (int)Math.Sqrt(grenade.Force / 100);
		}

		/// <summary>
		/// Use reflection to return the proper GetDangerRadius override for a given type, if it exists.
		/// Ignore the base IGrenade type, though.
		/// </summary>
		static MethodInfo GetDangerRadiusMethod(IGrenade grenade)
		{
			if (!dangerRadiusLookup.TryGetValue(grenade.GetType(), out MethodInfo dangerMethod))
			{
				dangerRadiusParameterList[0] = grenade.GetType();
				dangerMethod = AccessTools.Method(typeof(GrenadePatch), nameof(GrenadePatch.GetDangerRadius), dangerRadiusParameterList, null);
				// UnityEngine.Debug.Log($"dangerMethod for {grenade.GetType().Name} is {dangerMethod}");
				if (dangerMethod != null && dangerMethod.GetParameters()[0].ParameterType.Name == "IGrenade")
				{
					dangerMethod = null;
				}
				dangerRadiusLookup.Add(grenade.GetType(), dangerMethod);
			}
			return dangerMethod;
		}

		static bool GrenadeSafetyCheck(GameObject thrownWeapon, Brain throwerBrain, Cell targetCell)
		{
			if (Options.GetOption("OptionSafeGrenades") != "Yes")
			{
				return true;
			}
			if (!thrownWeapon.HasPartDescendedFrom<IGrenade>())
			{
				return true;
			}
			IGrenade grenade = (IGrenade)thrownWeapon.GetPartDescendedFrom<IGrenade>();
			int dangerRadius = GetDangerRadius(grenade);
			// UnityEngine.Debug.Log($"DangerRadius for {thrownWeapon.DebugName} is {dangerRadius}");
			if (dangerRadius == 0)
			{
				return true;
			}
			// Check a radius of dangerRadius around targetCell
			// for any visible, friendly creatures.
			// If any are found, return false.
			List<GameObject> bystanders = targetCell.FastFloodVisibility("Combat", dangerRadius);
			foreach (GameObject bystander in bystanders)
			{
				// UnityEngine.Debug.Log($"Thrower {throwerBrain.ParentObject.DebugName} is {throwerBrain.GetOpinion(bystander)} to {bystander.DebugName}");
				// UnityEngine.Debug.Log($"Bystander {bystander.DebugName} {(CanEndangerAlly(grenade, bystander) ? "can" : "cannot")} be damaged by {grenade.GetType().Name}");
				if (throwerBrain.IsBystander(bystander, includeSelf: true) && CanEndangerAlly(grenade, bystander))
				{
					return false;
				}
			}
			return true;
		}

		/// <summary>
		/// Modify Kill.TryThrownWeapon to check if the thrown weapon is a grenade
		/// and if there are friendly creatures in its danger radius.
		/// </summary>
		// Inject two instructions before ldstr "I'm going to throw my "
		// and call the GrenadeSafetyCheck method.
		// If false, return false.
		// If true, continue with the original code.
		[HarmonyPatch(typeof(Kill), nameof(Kill.TryThrownWeapon))]
		static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
		{
			var codes = new List<CodeInstruction>(instructions);
			int startidx = -1;
			int retidx = -1;
			LocalBuilder weaponVar = null;
			LocalBuilder returnVar = null;
			for (int i=0; i<codes.Count; i++)
			{
				if (weaponVar == null && codes[i].opcode == OpCodes.Ldloc_S && ((LocalBuilder)codes[i].operand).LocalIndex == (byte) 6)
				{
					weaponVar = (LocalBuilder)codes[i].operand;
					continue;
				}
				if (retidx == -1 && codes[i].opcode == OpCodes.Stloc_S && ((LocalBuilder) codes[i].operand).LocalIndex == (byte) 15)
				{
					retidx = i+1;
					returnVar = codes[i].operand as LocalBuilder;
					continue;
				}
				if (codes[i].Is(OpCodes.Ldstr, "I'm going to throw my "))
				{
					startidx = i-1;
					break;
				}
			}
			Label safeLabel = generator.DefineLabel();
			CodeInstruction leaveInstruction = codes[retidx].Clone();
			codes[startidx].labels.Add(safeLabel); // add a label to the instruction after our check
			codes.InsertRange(startidx, new CodeInstruction[] {
				// ldloc.s    weaponVar
				new CodeInstruction(OpCodes.Ldloc_S, weaponVar),
				// ldarg.0
				// ldfld      class XRL.Game.Brain XRL.Game.AI.GoalHandler::ParentBrain
				new CodeInstruction(OpCodes.Ldarg_0),
				CodeInstruction.LoadField(typeof(GoalHandler), nameof(GoalHandler.ParentBrain)),
				// ldloc.2
				new CodeInstruction(OpCodes.Ldloc_3),
				// call       GrenadePatch::GrenadeSafetyCheck(GameObject, Brain, Cell)
				CodeInstruction.Call(typeof(GrenadePatch), nameof(GrenadeSafetyCheck)),
				new CodeInstruction(OpCodes.Brtrue, safeLabel),
				new CodeInstruction(OpCodes.Ldc_I4_0),
				new CodeInstruction(OpCodes.Stloc_S, returnVar),
				leaveInstruction});
			return codes;
		}
	}
}