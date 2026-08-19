using NzuaMcp.Nzua;

namespace NzuaMcp.Tests;

public class MarkModelTests
{
    [Theory]
    [InlineData("10", null, null, 15)]
    [InlineData("Н", null, null, SpecialMarks.Absent)]
    [InlineData("delete", null, null, SpecialMarks.Delete)]
    [InlineData(null, 12, null, 17)]
    [InlineData(null, null, "хв", SpecialMarks.Sick)]
    public void Resolve_AcceptsDocumentedValues(string? mark, int? grade, string? specialMark, int expected)
    {
        Assert.Equal(expected, MarkValueResolver.Resolve(mark, grade, specialMark));
    }

    [Fact]
    public void Resolve_UnknownValueNeverBecomesDelete()
    {
        var exception = Assert.Throws<NzuaException>(() => MarkValueResolver.Resolve(mark: "unknown"));

        Assert.Contains("явне значення delete", exception.Message);
    }

    [Fact]
    public void Resolve_EmptyValueFails()
    {
        Assert.Throws<NzuaException>(() => MarkValueResolver.Resolve());
    }

    [Theory]
    [InlineData(new int[] { }, MarkScale.None)]
    [InlineData(new[] { SpecialMarks.Absent, SpecialMarks.Sick }, MarkScale.None)]
    [InlineData(new[] { 6, 12, 17 }, MarkScale.Numeric)]
    [InlineData(new[] { SpecialMarks.NusBeginner, SpecialMarks.NusHigh }, MarkScale.Levels)]
    [InlineData(new[] { 12, SpecialMarks.NusGood }, MarkScale.Mixed)]
    public void MarkScaleDetector_SeparatesNumericLevelsAndMixed(int[] markIds, MarkScale expected)
    {
        Assert.Equal(expected, MarkScaleDetector.Detect(markIds));
    }

    [Fact]
    public void WritePolicy_BlocksJournalMutationUnlessExplicitlyEnabled()
    {
        Assert.Throws<NzuaException>(() =>
            NzuaWritePolicy.EnsureAllowed("/journal/set-mark", writesAllowed: false));
        NzuaWritePolicy.EnsureAllowed("/journal/set-mark", writesAllowed: true);
        NzuaWritePolicy.EnsureAllowed("/site/semester-change", writesAllowed: false);
    }
}
