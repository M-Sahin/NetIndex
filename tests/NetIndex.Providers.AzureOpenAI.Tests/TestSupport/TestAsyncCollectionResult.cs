using System.ClientModel;
using System.ClientModel.Primitives;
using NSubstitute;

namespace NetIndex.Providers.AzureOpenAI.Tests.TestSupport;

public sealed class TestAsyncCollectionResult<T>(IEnumerable<T> values, Exception? failure = null) : AsyncCollectionResult<T>
{
    private readonly ClientResult _page = ClientResult.FromResponse(Substitute.For<PipelineResponse>());

    protected override async IAsyncEnumerable<T> GetValuesFromPageAsync(ClientResult page)
    {
        foreach (var value in values)
        {
            await Task.Yield();
            yield return value;
        }

        if (failure is not null)
        {
            throw failure;
        }
    }

    public override async IAsyncEnumerable<ClientResult> GetRawPagesAsync()
    {
        await Task.Yield();
        yield return _page;
    }

    public override ContinuationToken? GetContinuationToken(ClientResult page) => null;
}
