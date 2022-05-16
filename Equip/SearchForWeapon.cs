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

		public override bool CanFight()
		{
			return false;
		}

		public override bool Finished()
		{
			return false;
		}

		public override void TakeAction()
		{
			Think("I'm going to find a new weapon!");
			// First, find the score of our best weapon with searchPart.
			Func<GameObject, double> scorerPredicate = delegate(GameObject GO) {return Brain.PreciseWeaponScore(GO, ParentObject);};
			List<GameObject> invWeapons = ParentObject.Inventory.GetObjects(GO => GO.HasPart(searchPart));
			double maxScore = 0.0;
			if (searchPart == "MissileWeapon")
			{
				scorerPredicate = delegate(GameObject GO) {return Brain.PreciseMissileWeaponScore(GO, ParentObject);};
			}
			if (invWeapons.Count() > 0)
			{
				maxScore = invWeapons.Max(GO => scorerPredicate(GO));
			}

			int searchRadius = XRL.Rules.Stat.Random(ParentBrain.MinKillRadius, ParentBrain.MaxKillRadius);
			List<GameObject> equipCandidates = CurrentZone.FastFloodVisibility(CurrentCell.X, CurrentCell.Y, searchRadius, searchPart, ParentObject).Where(GO => GO.CurrentCell != null && GO.IsTakeable() && CurrentCell.PathDistanceTo(GO.CurrentCell.location) <= searchRadius && scorerPredicate(GO) > maxScore).OrderByDescending(scorerPredicate).ToList();
			if (equipCandidates.Count() <= 0)
			{
				ParentBrain.DoReequip = true;
				FailToParent();
				return;
			}
			ParentBrain.PushGoal(new EquipObject(equipCandidates.First())); // if we don't get it, don't search again.
		}
	}
}