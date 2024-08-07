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
using System.Diagnostics;

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
			// We need a special exception here to handle stuff like bows and launchers
			// that use existing objects as projectiles instead of just summoning them from a blueprint.
			MagazineAmmoLoader magazineAmmoLoader = missileWeapon.GetPart<MagazineAmmoLoader>();
			if (magazineAmmoLoader != null)
			{
				projectile = GetProjectileObjectEvent.GetFor(magazineAmmoLoader.Ammo, missileWeapon);
				if (projectile != null && projectile.IsInvalid())
				{
					projectile = null; // try again
				}
			}
			// If we still don't have a projectile, try the normal event
			if (projectile == null)
			{
				GetMissileWeaponProjectileEvent.GetFor(missileWeapon, ref projectile, ref blueprint);
				// If we don't have one after that, create a sample object from the ammo blueprint
				if (projectile == null && blueprint != null)
				{
					projectile = GameObject.CreateSample(blueprint);
				}
			}
			if(!GameObject.Validate(projectile) || projectile.IsInGraveyard())
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
				UnityEngine.Debug.Log($"Thrower {shooterBrain.ParentObject.DebugName} is {Enum.GetName(typeof(Brain.FeelingLevel), shooterBrain.GetFeelingLevel(bystander))} to {bystander.DebugName}");
				UnityEngine.Debug.Log($"Bystander {bystander.DebugName} {(CanEndangerAlly(projectile, bystander) ? "can" : "cannot")} be damaged by {projectile.GetType().Name}");
				if (shooterBrain.IsBystander(bystander, includeSelf: true) && CanEndangerAlly(projectile, bystander))
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
			var codeMatcher = new CodeMatcher(instructions)
				.MatchEndForward(
					new CodeMatch(OpCodes.Ldloc_S, name: "load gameObject"), // gameObject, our weapon
					new CodeMatch(OpCodes.Ldarg_0),
					new CodeMatch(code => code.Calls(AccessTools.DeclaredPropertyGetter(typeof(GoalHandler), nameof(GoalHandler.ParentObject)))),
					new CodeMatch(OpCodes.Ldloc_0),
					new CodeMatch(code => code.Calls(AccessTools.DeclaredMethod(typeof(AIWantUseWeaponEvent), nameof(AIWantUseWeaponEvent.Check)))),
					new CodeMatch(OpCodes.Brfalse, name: "branch")
				).Advance(1)
				.ThrowIfInvalid("LiveAndThink.MissilePatch: Unable to find AIWantUseWeaponEvent.Check injection site!");
			return codeMatcher
				.Insert(
					codeMatcher.NamedMatch("load gameObject").Clone(),
					new CodeInstruction(OpCodes.Ldarg_0), // ParentObject
					CodeInstruction.LoadField(typeof(GoalHandler), nameof(GoalHandler.ParentBrain)), // parentObject.ParentBrain
					new CodeInstruction(OpCodes.Ldloc_3), // Cell
					CodeInstruction.Call(typeof(MissilePatch), nameof(MissileSafetyCheck)),
					codeMatcher.NamedMatch("branch").Clone()
				).InstructionEnumeration();
		}
	}
}