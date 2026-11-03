using VoiceBattle.Session.Text;
using Xunit;

namespace VoiceBattle.Session.Tests;

public class ArabicShaperTests
{
    [Fact]
    public void JoinsLettersIntoPresentationForms()
        // اسمع: alef (isolated, never joins forward) · seen initial · meem medial · ain final
        => Assert.Equal("ﺍﺳﻤﻊ", ArabicShaper.Shape("اسمع"));

    [Fact]
    public void ReversesForLeftToRightRenderer()
        => Assert.Equal("ﻊﻤﺳﺍ", ArabicShaper.ForDisplay("اسمع"));

    [Fact]
    public void LamAlefLigature()
    {
        Assert.Equal("ﻻ", ArabicShaper.Shape("لا"));
        Assert.Equal("ﺑﻼ", ArabicShaper.Shape("بلا"));   // beh initial + lam-alef final
        Assert.Equal("ﻷ", ArabicShaper.Shape("لأ"));
    }

    [Fact]
    public void NonJoiningLettersBreakTheWord()
        // دور: dal isolated (nothing before it), waw final? no — dal does not join forward, so waw is isolated, reh after waw isolated
        => Assert.Equal("ﺩﻭﺭ", ArabicShaper.Shape("دور"));

    [Fact]
    public void TehMarbutaAndAlefMaksura()
    {
        Assert.Equal("ﺗﻴﺘﺎ", ArabicShaper.Shape("تيتا"));
        Assert.Equal("ﻣﻮﺳﻴﻘﻰ", ArabicShaper.Shape("موسيقى"));
    }

    [Fact]
    public void HarakatAreTransparentForJoining()
        => Assert.Equal("ﻣُﺤَﻤّﺪ", ArabicShaper.Shape("مُحَمّد"));

    [Fact]
    public void NumbersAndLatinKeepTheirOrder()
    {
        string shown = ArabicShaper.ForDisplay("ضرر 14");
        Assert.StartsWith("14 ", shown);
        Assert.Contains("Error 404", ArabicShaper.ForDisplay("خطأ Error 404"));
        Assert.Contains("1.5", ArabicShaper.ForDisplay("سرعة 1.5"));
    }

    [Fact]
    // RTL "(ب)": the logical "(" ends up on the right and must open leftwards, so both glyphs swap.
    public void BracketsAreMirrored() => Assert.Equal("(\uFE8F)", ArabicShaper.ForDisplay("(ب)"));

    [Fact]
    public void LatinOnlyUnchanged()
    {
        Assert.Equal("Task complete. Beep.", ArabicShaper.ForDisplay("Task complete. Beep."));
        Assert.False(ArabicShaper.NeedsShaping("PERFECT"));
    }

    [Fact]
    public void EveryCharacterLineShapesToPresentationForms()
    {
        foreach (var c in D.Data.Characters.All)
            foreach (var line in new[] { c.Name.Ar, c.Taunt.Ar, c.Win.Ar, c.Lose.Ar })
                foreach (var ch in ArabicShaper.ForDisplay(line))
                    Assert.False(ch >= 'ء' && ch <= 'ي', $"unshaped letter U+{(int)ch:X4} left in \"{line}\"");
    }

    [Fact]
    public void MultipleLinesKeepLineOrder()
    {
        var parts = ArabicShaper.ForDisplay("أ\nب").Split('\n');
        Assert.Equal("ﺃ", parts[0]);
        Assert.Equal("ﺏ", parts[1]);
    }
}
