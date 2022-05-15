using System;
using System.Collections.Generic;
using System.Linq;
using XRL.World;
using XRL.World.AI;
using XRL.World.AI.GoalHandlers;
using XRL.World.Parts;
using LiveAndThink.Disarm;

namespace LiveAndThink.Equip
{
	[Serializable]
	public class SearchForWeapon : GoalHandler
	{
		private string searchPart = "";

		public SearchForWeapon(string _searchPart)
		{
			searchPart = _searchPart;
		}

		public override void TakeAction()
		{
			Think("I'm going to find a new weapon!");
			int searchRadius = XRL.Rules.Stat.Random(ParentBrain.MinKillRadius, ParentBrain.MaxKillRadius);
			List<GameObject> newWeapons = ParentObject.CurrentZone.FastFloodVisibility(ParentObject.CurrentCell.X, ParentObject.CurrentCell.Y, searchRadius, searchPart, ParentObject);
			if (newWeapons.Count() <= 0)
			{
				ParentBrain.DoReequip = true;
				return;
			}
			Func<GameObject, double> scorerPredicate = delegate(GameObject GO) {return Brain.PreciseWeaponScore(GO, ParentObject);};
			if (searchPart == "MissileWeapon")
			{
				scorerPredicate = delegate(GameObject GO) {return Brain.PreciseMissileWeaponScore(GO, ParentObject);};
			}
			List<GameObject> equipCandidates = newWeapons.Where(GO => GO.CurrentCell != null && GO.IsTakeable() && CurrentCell.PathDistanceTo(GO.CurrentCell.location) <= searchRadius && scorerPredicate(GO) > 0.0).OrderByDescending(scorerPredicate).ToList();
			if (equipCandidates.Count() <= 0)
			{
				ParentBrain.DoReequip = true;
				return;
			}
			ParentBrain.PushGoal(new EquipObject(equipCandidates.First()));
		}
	}
}