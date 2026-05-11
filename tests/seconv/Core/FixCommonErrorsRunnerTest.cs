using Nikse.SubtitleEdit.Core.Common;
using SeConv.Core;
using Xunit;

namespace SeConvTests.Core;

public class FixCommonErrorsRunnerTest
{
    [Fact]
    public void RunAll_OnEmptySubtitle_NoThrow()
    {
        var sub = new Subtitle();
        FixCommonErrorsRunner.RunAll(sub);
        Assert.Empty(sub.Paragraphs);
    }

    [Fact]
    public void RunAll_FixesMissingSpaceAfterComma()
    {
        var sub = new Subtitle();
        sub.Paragraphs.Add(new Paragraph("hello,world.", 0, 1000));
        sub.Renumber();

        FixCommonErrorsRunner.RunAll(sub);

        // FixMissingSpaces adds a space after the comma; FixStartWithUppercaseLetter
        // capitalises the leading 'h'. End result: "Hello, world."
        Assert.Equal("Hello, world.", sub.Paragraphs[0].Text);
    }

    [Fact]
    public void RunAll_FixesUnneededSpaceBeforePeriod()
    {
        var sub = new Subtitle();
        sub.Paragraphs.Add(new Paragraph("Hello world .", 0, 2000));
        sub.Renumber();

        FixCommonErrorsRunner.RunAll(sub);

        Assert.DoesNotContain(" .", sub.Paragraphs[0].Text);
        Assert.EndsWith(".", sub.Paragraphs[0].Text);
    }

    [Fact]
    public void RunAll_RemovesEmptyLines()
    {
        var sub = new Subtitle();
        sub.Paragraphs.Add(new Paragraph("Hello world.", 0, 2000));
        sub.Paragraphs.Add(new Paragraph(string.Empty, 3000, 5000));
        sub.Paragraphs.Add(new Paragraph("Goodbye.", 6000, 8000));
        sub.Renumber();

        FixCommonErrorsRunner.RunAll(sub);

        // FixEmptyLines drops the empty paragraph
        Assert.Equal(2, sub.Paragraphs.Count);
        Assert.Equal("Hello world.", sub.Paragraphs[0].Text);
        Assert.Equal("Goodbye.", sub.Paragraphs[1].Text);
    }

    [Fact]
    public void RunAll_FixesAloneLowercaseI_InEnglish()
    {
        var sub = new Subtitle();
        sub.Paragraphs.Add(new Paragraph("Yes, i went to the store.", 0, 2000));
        sub.Renumber();

        FixCommonErrorsRunner.RunAll(sub);

        // FixAloneLowercaseIToUppercaseI fixes lowercase 'i' as a standalone word
        Assert.Contains(" I ", sub.Paragraphs[0].Text);
    }

    [Fact]
    public void Run_WithExplicitRule_OnlyAppliesThatRule()
    {
        var sub = new Subtitle();
        // FixMissingSpaces would add a space after the comma; FixStartWithUppercaseLetter*
        // would capitalise the leading 'h'. With only FixMissingSpaces selected, the 'h'
        // must stay lowercase.
        sub.Paragraphs.Add(new Paragraph("hello,world.", 0, 2000));
        sub.Renumber();

        FixCommonErrorsRunner.Run(sub, ["FixMissingSpaces"]);

        Assert.Equal("hello, world.", sub.Paragraphs[0].Text);
    }

    [Fact]
    public void Run_WithEmptyList_RunsAllRules()
    {
        var sub = new Subtitle();
        sub.Paragraphs.Add(new Paragraph("hello,world.", 0, 2000));
        sub.Renumber();

        FixCommonErrorsRunner.Run(sub, Array.Empty<string>());

        // Same outcome as RunAll: capitalised + space inserted
        Assert.Equal("Hello, world.", sub.Paragraphs[0].Text);
    }

    [Fact]
    public void ResolveRuleIds_NullOrWhitespace_ReturnsAll()
    {
        var all = FixCommonErrorsRunner.AvailableRuleIds;

        Assert.Equal(all, FixCommonErrorsRunner.ResolveRuleIds(null));
        Assert.Equal(all, FixCommonErrorsRunner.ResolveRuleIds(""));
        Assert.Equal(all, FixCommonErrorsRunner.ResolveRuleIds("   "));
        Assert.Equal(all, FixCommonErrorsRunner.ResolveRuleIds("all"));
    }

    [Fact]
    public void ResolveRuleIds_ExplicitList_ReturnsSubsetInCanonicalOrder()
    {
        // Spec is in user-supplied order; result should be in canonical (alphabetical-ish)
        // order so behavior is deterministic.
        var resolved = FixCommonErrorsRunner.ResolveRuleIds("FixMissingSpaces,FixCommas");

        Assert.Equal(new[] { "FixCommas", "FixMissingSpaces" }, resolved);
    }

    [Fact]
    public void ResolveRuleIds_AllMinusOne_DropsThatRule()
    {
        var resolved = FixCommonErrorsRunner.ResolveRuleIds("all,-FixDanishLetterI");

        Assert.Equal(FixCommonErrorsRunner.AvailableRuleIds.Count - 1, resolved.Count);
        Assert.DoesNotContain("FixDanishLetterI", resolved);
    }

    [Fact]
    public void ResolveRuleIds_NegationsOnly_ImpliesAll()
    {
        var resolved = FixCommonErrorsRunner.ResolveRuleIds("-FixDanishLetterI,-FixCommas");

        Assert.Equal(FixCommonErrorsRunner.AvailableRuleIds.Count - 2, resolved.Count);
        Assert.DoesNotContain("FixDanishLetterI", resolved);
        Assert.DoesNotContain("FixCommas", resolved);
    }

    [Fact]
    public void ResolveRuleIds_CaseInsensitive()
    {
        var resolved = FixCommonErrorsRunner.ResolveRuleIds("fixcommas,FIXMISSINGSPACES");

        Assert.Equal(new[] { "FixCommas", "FixMissingSpaces" }, resolved);
    }

    [Fact]
    public void ResolveRuleIds_UnknownRule_Throws()
    {
        var ex = Assert.Throws<ArgumentException>(
            () => FixCommonErrorsRunner.ResolveRuleIds("FixCommas,NotARealRule"));

        Assert.Contains("NotARealRule", ex.Message);
    }
}
