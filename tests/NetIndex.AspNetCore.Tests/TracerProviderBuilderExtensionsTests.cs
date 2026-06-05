#pragma warning disable CS1591
using NetIndex.AspNetCore;
using NetIndex.Core.Abstractions.Telemetry;
using OpenTelemetry.Trace;
using Xunit;

namespace NetIndex.AspNetCore.Tests;

[Trait("Category", "PipelineContract")]
public sealed class TracerProviderBuilderExtensionsTests
{
    [Fact]
    public void AddNetIndex_WithNullBuilder_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => TracerProviderBuilderExtensions.AddNetIndex(null!));
    }

    [Fact]
    public void AddNetIndex_RegistersNetIndexActivitySource()
    {
        var builder = new RecordingTracerProviderBuilder();

        var result = builder.AddNetIndex();

        Assert.Same(builder, result);
        Assert.Contains(NetIndexActivitySource.Source.Name, builder.Sources);
    }

    private sealed class RecordingTracerProviderBuilder : TracerProviderBuilder
    {
        public List<string> Sources { get; } = [];

        public override TracerProviderBuilder AddSource(params string[] names)
        {
            Sources.AddRange(names);
            return this;
        }

        public override TracerProviderBuilder AddLegacySource(string operationName)
        {
            return this;
        }

        public override TracerProviderBuilder AddInstrumentation<TInstrumentation>(Func<TInstrumentation> instrumentationFactory)
        {
            return this;
        }
    }
}
