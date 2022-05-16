using System;
using System.Collections.Generic;
using System.Linq;
using XRL.World;
using XRL.World.AI;
using XRL.World.AI.GoalHandlers;
using XRL.World.Parts;

namespace LiveAndThink.Disarm
{
	[Serializable]
	public class EquipObject : GoalHandler
	{
		protected GameObject targetObject;
		
		protected int FailureChances = 3;

		public EquipObject(GameObject GO)
		{
			targetObject = GO;
		}

		public override void Create()
		{
			if (!ParentObject.IsMobile() || ParentObject.Stat("Intelligence") < 7)
			{
				Pop();
			}
		}

		public override bool CanFight()
		{
			return false;
		}

		public override bool Finished()
		{
			return GameObject.validate(targetObject) && ParentObject.Contains(targetObject);
		}

		private void DoFail()
		{
			FailToParent();
			OnFail();
		}

		/// <summary>
		/// Perform an action (like pushing another goal) when this goal fails.
		/// </summary>
		protected virtual void OnFail()
		{
			return; // This is for subtypes to override.
		}

		public override void TakeAction()
		{
			if (!GameObject.validate(targetObject))
			{
				Think($"What I was trying to equip no longer exists!");
				DoFail();
				return;
			}
			if (!targetObject.IsTakeable())
			{
				Think($"I can't equip {targetObject.t(Stripped: true)}!");
				DoFail();
				return;
			}
			if (ParentObject.Contains(targetObject))
			{
				Think($"I already have {targetObject.t(Stripped: true)}!");
				return; // don't fail
			}
			if (targetObject.InInventory != null) // okay to be fair it was funny to have stuff stolen from your inventory
			{
				Think($"Someone else is already holding {targetObject.t(Stripped: true)}!");
				DoFail();
				return;
			}
			if (ParentObject.InSameOrAdjacentCellTo(targetObject))
			{
				Think($"I'm going to take {targetObject.t(Stripped: true)}.");
				ParentBrain.PushGoal(new DelegateGoal(delegate (GoalHandler h)
				{
					ParentObject.TakeObject(targetObject, false);
					ParentBrain.DoReequip = true;
					h.Pop();
				}));
			}
			else
			{
				Think($"I'm going to move to {targetObject.t(Stripped: true)}.");
				if (!MoveTowards(targetObject.CurrentCell))
				{
					if (--FailureChances > 0)
					{
						Think($"I can't move to {targetObject.t(Stripped: true)}, but I'll try {FailureChances} more times.");
					}
					else
					{
						Think($"I can't move to {targetObject.t(Stripped: true)}, so I'm giving up!");
						DoFail();
					}
				}
			}
		}
	}
}