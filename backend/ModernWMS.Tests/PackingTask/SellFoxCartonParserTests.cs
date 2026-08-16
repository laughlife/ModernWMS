using ModernWMS.WMS.Services.PackingTask;

namespace ModernWMS.Tests.PackingTask;

public class SellFoxCartonParserTests
{
    [Fact]
    public void Parse_allows_an_empty_source_array_before_the_weighing_stage_when_explicitly_requested()
    {
        var result = SellFoxCartonParser.Parse("[]", allowEmpty: true);

        Assert.True(result.IsSupported, result.Error);
        Assert.Empty(result.Boxes);
    }

    [Fact]
    public void Parse_rejects_array_index_as_identity()
    {
        var result = SellFoxCartonParser.Parse("[{\"weight\":1},{\"weight\":2}]");

        Assert.False(result.IsSupported);
        Assert.Contains("稳定箱ID", result.Error);
        Assert.Empty(result.Boxes);
    }

    [Fact]
    public void Parse_preserves_unique_source_ids_and_sequence()
    {
        var result = SellFoxCartonParser.Parse("[{\"cartonId\":\"C-2\"},{\"cartonId\":\"C-1\"}]");

        Assert.True(result.IsSupported, result.Error);
        Assert.Equal(["C-2", "C-1"], result.Boxes.Select(x => x.SourceBoxIdentity));
        Assert.Equal([1, 2], result.Boxes.Select(x => x.Sequence));
    }

    [Theory]
    [InlineData("[{\"boxId\":\"B-1\",\"id\":\"ignored\"}]", "B-1")]
    [InlineData("[{\"box_id\":\"B-2\"}]", "B-2")]
    [InlineData("[{\"cartonId\":\"C-1\"}]", "C-1")]
    [InlineData("[{\"carton_id\":\"C-2\"}]", "C-2")]
    [InlineData("[{\"id\":123}]", "123")]
    public void Parse_uses_only_explicit_identity_keys_in_priority_order(string json, string expected)
    {
        var result = SellFoxCartonParser.Parse(json);

        Assert.True(result.IsSupported, result.Error);
        Assert.Equal(expected, Assert.Single(result.Boxes).SourceBoxIdentity);
    }

    [Theory]
    [InlineData("")]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("[null]")]
    [InlineData("[{\"id\":\"  \"}]")]
    [InlineData("[{\"id\":true}]")]
    public void Parse_fails_closed_for_invalid_or_blank_identity_sources(string json)
    {
        var result = SellFoxCartonParser.Parse(json);

        Assert.False(result.IsSupported);
        Assert.NotEmpty(result.Error);
        Assert.Empty(result.Boxes);
    }

    [Fact]
    public void Parse_rejects_duplicate_identity_after_trimming()
    {
        var result = SellFoxCartonParser.Parse("[{\"id\":\"BOX-1\"},{\"boxId\":\" BOX-1 \"}]");

        Assert.False(result.IsSupported);
        Assert.Contains("重复", result.Error);
        Assert.Empty(result.Boxes);
    }

    [Fact]
    public void Parse_keeps_source_measurements_only_inside_read_only_snapshot()
    {
        var result = SellFoxCartonParser.Parse("[{\"id\":\"BOX-1\",\"weight\":12.5,\"length\":30}]");

        var box = Assert.Single(result.Boxes);
        Assert.Contains("\"weight\":12.5", box.SourceSnapshot);
        Assert.DoesNotContain(box.GetType().GetProperties(), property =>
            property.Name is "Weight" or "Length" or "Width" or "Height");
    }
}
