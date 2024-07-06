using HarmonyLib;
using System.Reflection.Emit;
namespace LiveAndThink.Harmony
{
	public static class CodeMatcherExtensions
	{
		public static LocalBuilder GetLocalBuilder(this CodeMatcher instance, int localIndex)
		{
			int priorOffset = instance.Pos;
			LocalBuilder foundLocal = instance.MatchStartForward(
				new CodeMatch(code => (code.operand as LocalBuilder)?.LocalIndex == (byte) localIndex)
			).Instruction?.operand as LocalBuilder;
			instance.Start().Advance(priorOffset);
			return foundLocal;
		}
	}
}