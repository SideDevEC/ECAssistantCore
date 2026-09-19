using ECAssistant.Core.Setup;
using Xunit;

namespace ECAssistant.Core.Tests.Setup;

/// <summary>
/// HardwareProfile tuning rules: GPU layers / context / batch adapt to the machine.
/// Detection itself is OS-probed; rules are tested via constructed profiles.
/// </summary>
public sealed class HardwareProfileTests
{
    private static ModelCatalogEntry Entry(double sizeGb, uint ctx = 32768, bool embedding = false, string? backend = null, int maxTokens = 1024)
    {
        var e = new ModelCatalogEntry
        {
            Id = "test-model",
            Category = embedding ? CatalogModelCategory.Embedding : CatalogModelCategory.Chat,
            SuggestedConfig = new CatalogSuggestedConfig
            {
                GpuLayers = 99,
                ContextSize = ctx,
                BatchSize = 512,
                Backend = backend,
                MaxTokens = maxTokens
            }
        };
        e.Files.Add(new CatalogModelFile { Filename = "model.gguf", SizeGb = sizeGb });
        return e;
    }

    [Fact]
    public void Adjust_AppleSilicon_BigRam_FullOffload()
    {
        var hw = new HardwareProfile { TotalRamGb = 96, IsAppleSilicon = true, HasCuda = false };
        var tuned = hw.Adjust(Entry(6.6));

        Assert.Equal(99, tuned.GpuLayers);
        Assert.Equal(32768u, tuned.ContextSize);
        Assert.Equal(512, tuned.BatchSize);
    }

    [Fact]
    public void Adjust_AppleSilicon_SmallRam_LargeModel_CpuFallback()
    {
        var hw = new HardwareProfile { TotalRamGb = 8, IsAppleSilicon = true, HasCuda = false };
        var tuned = hw.Adjust(Entry(20));

        Assert.Equal(0, tuned.GpuLayers);
    }

    [Fact]
    public void Adjust_Cuda_MidModel_FullOffload()
    {
        var hw = new HardwareProfile { TotalRamGb = 32, IsAppleSilicon = false, HasCuda = true };
        var tuned = hw.Adjust(Entry(6.6));

        Assert.Equal(99, tuned.GpuLayers);
    }

    [Fact]
    public void Adjust_Cuda_HugeModel_CpuFallback()
    {
        var hw = new HardwareProfile { TotalRamGb = 32, IsAppleSilicon = false, HasCuda = true };
        var tuned = hw.Adjust(Entry(20));

        Assert.Equal(0, tuned.GpuLayers);
    }

    [Fact]
    public void Adjust_Vulkan_SmallModel_FullOffload()
    {
        var hw = new HardwareProfile { TotalRamGb = 32, IsAppleSilicon = false, HasCuda = false };
        var tuned = hw.Adjust(Entry(2.7));

        Assert.Equal(99, tuned.GpuLayers);
    }

    [Fact]
    public void Adjust_Vulkan_LargeModel_CpuFallback()
    {
        var hw = new HardwareProfile { TotalRamGb = 32, IsAppleSilicon = false, HasCuda = false };
        var tuned = hw.Adjust(Entry(20));

        Assert.Equal(0, tuned.GpuLayers);
    }

    [Fact]
    public void Adjust_LowRam_ScalesContextAndBatch()
    {
        var hw = new HardwareProfile { TotalRamGb = 5, IsAppleSilicon = true, HasCuda = false };
        var tuned = hw.Adjust(Entry(1.5, ctx: 32768));

        Assert.Equal(16384u, tuned.ContextSize);
        Assert.Equal(256, tuned.BatchSize);
    }

    [Fact]
    public void Adjust_NormalRam_KeepsCatalogContext()
    {
        // 65k context for the big catalog tiers must survive untouched on 12GB+ machines
        var hw = new HardwareProfile { TotalRamGb = 16, IsAppleSilicon = true, HasCuda = false };
        var tuned = hw.Adjust(Entry(1.5, ctx: 65536));

        Assert.Equal(65536u, tuned.ContextSize);
    }

    [Fact]
    public void Adjust_TinyRam_StillGetsAtLeast16k()
    {
        var hw = new HardwareProfile { TotalRamGb = 4, IsAppleSilicon = false, HasCuda = false };
        var tuned = hw.Adjust(Entry(1.5, ctx: 65536));

        Assert.Equal(16384u, tuned.ContextSize);
    }

    [Fact]
    public void Adjust_Embedding_OmitsBatch_KeepsSmallContext()
    {
        var hw = new HardwareProfile { TotalRamGb = 32, IsAppleSilicon = false, HasCuda = true };
        var tuned = hw.Adjust(Entry(0.05, ctx: 2048, embedding: true));

        Assert.Equal(0, tuned.BatchSize);
        Assert.Equal(2048u, tuned.ContextSize);
    }

    [Fact]
    public void Adjust_PassesThroughBackendAndMaxTokens()
    {
        var hw = new HardwareProfile { TotalRamGb = 32, IsAppleSilicon = true, HasCuda = false };
        var tuned = hw.Adjust(Entry(6.6, backend: "process", maxTokens: 2048));

        Assert.Equal("process", tuned.Backend);
        Assert.Equal(2048, tuned.MaxTokens);
    }

    [Fact]
    public void Detect_ReturnsSaneValues()
    {
        var hw = HardwareProfile.Detect();

        Assert.True(hw.TotalRamGb > 0);
    }
}
