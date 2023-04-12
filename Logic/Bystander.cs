using XRL.World;
using XRL.World.Parts;

namespace LiveAndThink.Logic
{
	public static class Bystander
	{
		public static bool IsBystander(this Brain ourBrain, GameObject them, bool includeSelf = true)
		{
			// If we're including ourself, then we count as a bystander.
			if (!includeSelf && ourBrain.ParentObject == them)
			{
				return false;
			}
			// If we are hostile, only allies are bystanders.
			// If we aren't hostile, allies and neutral creatures are bystanders.
			if (ourBrain.IsAlliedTowards(them))
			{
				return true;
			}
			else if(!ourBrain.Hostile)
			{
				return ourBrain.IsNeutralTowards(them);
			}
			return false;
		}
	}
}