using XRL.World;
using XRL.World.Parts;

namespace LiveAndThink.Logic
{
	public static class Bystander
	{
		public static bool IsBystander(this Brain ourBrain, GameObject them)
		{
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