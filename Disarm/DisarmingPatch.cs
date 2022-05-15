using XRL.World;
using XRL.World.Capabilities;
using HarmonyLib;
using XRL.World.Parts;

namespace LiveAndThink.Disarm
{
	/// <summary>
	/// Modify Disarm(GameObject Object, GameObject Disarmer, ...) to
	/// push several GoalHandlers onto Object's brain's goals.
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
			Object.pBrain.PushGoal(new ReequipOrFindNew(__result));
		}
	}
}