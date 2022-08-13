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

/// <summary>
/// A collection of Harmony patches that make creatures
/// use their activated abilities/mutations more wisely.
namespace LiveAndThink.SmartUse
{
	/// <summary>
	/// Modify Kill.TryMissileWeapon to check if the fired missile is explosive/dangerous
	/// and if there are friendly creatures in its danger radius.
	/// </summary>
	[HarmonyPatch]
	public static class MissilePatch
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
		/// Return the danger radius of a projectile object.
		/// </summary>
		static int GetDangerRadius(GameObject projectile)
		{
			int dangerRadius = 0;
			// Check if it contains a grenade part, and apply that grenade part's radius.
			if(projectile.HasPartDescendedFrom<IGrenade>())
			{
				dangerRadius = Math.Max(dangerRadius, GrenadePatch.GetDangerRadius(projectile.GetFirstPartDescendedFrom<IGrenade>()));
			}
			if(projectile.HasPart("ExplodeOnHit"))
			{
				ExplodeOnHit explodeOnHit = projectile.GetPart<ExplodeOnHit>();
				dangerRadius = Math.Max(dangerRadius, (int)Math.Sqrt(explodeOnHit.Force / 100));
			}
			return dangerRadius;
		}

		static bool CanEndangerAlly(GameObject projectile, GameObject target)
		{
			bool canEndangerAlly = false;
			// Check if it contains a grenade part, and return that part's CanEndangerAlly if true.
			if(!canEndangerAlly && projectile.HasPartDescendedFrom<IGrenade>())
			{
				canEndangerAlly = GrenadePatch.CanEndangerAlly(projectile.GetFirstPartDescendedFrom<IGrenade>(), target);
			}
			if(!canEndangerAlly && projectile.HasPart("ExplodeOnHit"))
			{
				canEndangerAlly = true;
			}
			return canEndangerAlly;
		}

		static bool MissileSafetyCheck(GameObject missileWeapon, Brain shooterBrain, Cell targetCell)
		{
			if (Options.GetOption("OptionSafeMissiles") != "Yes")
			{
				return true;
			}
			if (missileWeapon == null || !missileWeapon.IsValid())
			{
				return true;
			}
			GameObject projectile = null;
			string blueprint = null;
			GetMissileWeaponProjectileEvent.GetFor(missileWeapon, ref projectile, ref blueprint);
			if (projectile == null)
			{
				projectile = GameObject.createSample(blueprint);
			}
			if(!GameObject.validate(projectile) || projectile.IsInGraveyard())
			{
				UnityEngine.Debug.Log($"Missile weapon {missileWeapon.DebugName} has no projectile!");
				return true;
			}
			int dangerRadius = GetDangerRadius(projectile);
			UnityEngine.Debug.Log($"Missile dangerRadius for {projectile.DebugName} is {dangerRadius}");
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
				UnityEngine.Debug.Log($"Thrower {shooterBrain.ParentObject.DebugName} is {shooterBrain.GetOpinion(bystander)} to {bystander.DebugName}");
				UnityEngine.Debug.Log($"Bystander {bystander.DebugName} {(CanEndangerAlly(projectile, bystander) ? "can" : "cannot")} be damaged by {projectile.GetType().Name}");
				if (!shooterBrain.IsHostileTowards(bystander) && CanEndangerAlly(projectile, bystander))
				{
					return false;
				}
			}
			return true;
		}

		/// <summary>
		/// Modify Kill.TryMissileWeapon to check if the thrown weapon is a ammo
		/// and if there are friendly creatures in its danger radius.
		/// </summary>
		// Inject one instruction before ldstr "AIWantUseWeapon"
		// and call the MissileSafetyCheck method.
		// If false, return false.
		// If true, continue with the original code.
		[HarmonyPatch(typeof(Kill), nameof(Kill.TryMissileWeapon))]
		static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
		{
			var codes = new List<CodeInstruction>(instructions);
			LocalBuilder local9 = (LocalBuilder) codes.Find(x => x.opcode == OpCodes.Ldloc_S && ((LocalBuilder)x.operand).LocalIndex == (byte) 9).operand;
			int bridx = -1;
			for (int i=codes.FindIndex(x => x.Is(OpCodes.Ldstr, "AIWantUseWeapon")); i<codes.Count; i++)
			{
				if (codes[i].opcode == OpCodes.Brfalse)
				{
					bridx = i;
					break;
				} 
			}
			if (bridx == -1)
			{
				return instructions; // do nothing
			}
			CodeInstruction leaveInstruction = codes[bridx].Clone();
			codes.InsertRange(bridx+1, new CodeInstruction[] {
				// ldloc.s    local9
				new CodeInstruction(OpCodes.Ldloc_S, local9),
				// ldarg.0
				// ldfld      class XRL.Game.Brain XRL.Game.AI.GoalHandler::ParentBrain
				new CodeInstruction(OpCodes.Ldarg_0),
				CodeInstruction.LoadField(typeof(GoalHandler), nameof(GoalHandler.ParentBrain)),
				// ldloc.2
				new CodeInstruction(OpCodes.Ldloc_2),
				// call       MissilePatch::MissileSafetyCheck(GameObject, Brain, Cell)
				CodeInstruction.Call(typeof(MissilePatch), nameof(MissileSafetyCheck)),
				leaveInstruction});
			return codes;
		}

		/// <summary>
		/// Modify MagazineAmmoLoader.HandleEvent(GetMissileWeaponProjectileEvent E) to work with bows and launchers
		/// by setting E.Projectile to Ammo.GetProjectileObjectEvent.GetFor(Ammo, ParentObject) if Ammo is valid/non-null.
		/// </summary>
		[HarmonyPatch(typeof(MagazineAmmoLoader), nameof(MagazineAmmoLoader.HandleEvent), new Type[] { typeof(GetMissileWeaponProjectileEvent) })]
		static bool Prefix(MagazineAmmoLoader __instance, GetMissileWeaponProjectileEvent E, ref bool __result)
		{
			if (__instance.Ammo != null && __instance.Ammo.IsValid())
			{
				E.Projectile = GetProjectileObjectEvent.GetFor(__instance.Ammo, __instance.ParentObject);
				__result = false;
				return false;
			}
			return true;
		}
	}
}