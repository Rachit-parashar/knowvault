using KnowVault.Domain.Chunking;

namespace KnowVault.Domain.Tests.Chunking;

public class ChunkingOptionsTests
{
    [Fact]
    public void Defaults_match_build_plan()
    {
        var options = new ChunkingOptions();

        Assert.Equal(512, options.TargetTokensPerChunk);
        Assert.Equal(0.15, options.OverlapRatio);
        Assert.Equal(76, options.OverlapTokens);
        Assert.True(options.IncludeBreadcrumbs);
        Assert.True(options.KeepTablesWhole);
    }

    [Fact]
    public void Defaults_are_valid()
    {
        var options = new ChunkingOptions();
        options.Validate();
    }

    [Theory]
    [InlineData(32)]
    [InlineData(4096)]
    public void Rejects_chunk_size_out_of_range(int tokens)
    {
        var options = new ChunkingOptions { TargetTokensPerChunk = tokens };
        Assert.Throws<ArgumentOutOfRangeException>(options.Validate);
    }

    [Theory]
    [InlineData(-0.1)]
    [InlineData(0.5)]
    [InlineData(0.9)]
    public void Rejects_overlap_out_of_range(double ratio)
    {
        var options = new ChunkingOptions { OverlapRatio = ratio };
        Assert.Throws<ArgumentOutOfRangeException>(options.Validate);
    }
}