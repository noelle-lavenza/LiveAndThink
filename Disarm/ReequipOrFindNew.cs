using System;
using System.Collections.Generic;
using System.Linq;
using XRL.World;
using XRL.World.AI;
using XRL.World.AI.GoalHandlers;
using XRL.World.Parts;
using LiveAndThink.Equip;

namespace LiveAndThink.Disarm
{
	[Serializable]
	public class ReequipOrFindNew : EquipObject
	{
		private string lostPart = "";

		public ReequipOrFindNew(GameObject GO) : base(GO)
		{
			if (GO.HasPart("MissileWeapon"))
			{
				lostPart = "MissileWeapon";
			}
			else if (GO.HasPart("MeleeWeapon"))
			{
				lostPart = "MeleeWeapon";
			}
		}

		/// <summary>
		/// On failure, the parent object might search for a new weapon within range
		/// or just fail, depending on Intelligence and availability.
		/// </summary>
		protected override void OnFail()
		{
			ParentBrain.PushGoal(new SearchForWeapon(lostPart));
		}
	}
}