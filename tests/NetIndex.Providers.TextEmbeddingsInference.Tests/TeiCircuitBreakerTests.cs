using System.Net;
using FluentAssertions;
using NetIndex.Core.Abstractions;
using Xunit;

namespace NetIndex.Providers.TextEmbeddingsInference.Tests;

public class TeiCircuitBreakerTests
{
    private static readonly TimeSpan Break = TimeSpan.FromSeconds(30);

    [Fact]
    public void Breaker_OpensAfterThresholdConsecutiveFailures()
    {
        var clock = new FakeTimeProvider();
        var breaker = new TeiCircuitBreaker(3, Break, clock);

        breaker.RecordFailure();
        breaker.RecordFailure();
        breaker.State.Should().Be(CircuitState.Closed);
        breaker.RecordFailure();

        breaker.State.Should().Be(CircuitState.Open);
        breaker.TryAcquire().Should().BeFalse();
    }

    [Fact]
    public void Breaker_SuccessResetsConsecutiveFailureCount()
    {
        var breaker = new TeiCircuitBreaker(3, Break, new FakeTimeProvider());

        breaker.RecordFailure();
        breaker.RecordFailure();
        breaker.RecordSuccess();
        breaker.RecordFailure();
        breaker.RecordFailure();

        breaker.State.Should().Be(CircuitState.Closed);
    }

    [Fact]
    public void Breaker_HalfOpenAdmitsExactlyOneProbe()
    {
        var clock = new FakeTimeProvider();
        var breaker = new TeiCircuitBreaker(1, Break, clock);
        breaker.RecordFailure();

        clock.Advance(Break);

        breaker.State.Should().Be(CircuitState.HalfOpen);
        breaker.TryAcquire().Should().BeTrue();
        breaker.TryAcquire().Should().BeFalse();
    }

    [Fact]
    public void Breaker_SuccessfulProbeCloses()
    {
        var clock = new FakeTimeProvider();
        var breaker = new TeiCircuitBreaker(1, Break, clock);
        breaker.RecordFailure();
        clock.Advance(Break);
        breaker.TryAcquire();

        breaker.RecordSuccess();

        breaker.State.Should().Be(CircuitState.Closed);
        breaker.TryAcquire().Should().BeTrue();
    }

    [Fact]
    public void Breaker_FailedProbeReopensForAFullBreak()
    {
        var clock = new FakeTimeProvider();
        var breaker = new TeiCircuitBreaker(3, Break, clock);
        for (var i = 0; i < 3; i++)
        {
            breaker.RecordFailure();
        }
        clock.Advance(Break);
        breaker.TryAcquire().Should().BeTrue();

        breaker.RecordFailure();

        breaker.State.Should().Be(CircuitState.Open);
        clock.Advance(Break - TimeSpan.FromSeconds(1));
        breaker.TryAcquire().Should().BeFalse();
        clock.Advance(TimeSpan.FromSeconds(1));
        breaker.State.Should().Be(CircuitState.HalfOpen);
    }

    [Fact]
    public void Breaker_ReleaseFreesAProbeSlotWithoutChangingState()
    {
        var clock = new FakeTimeProvider();
        var breaker = new TeiCircuitBreaker(1, Break, clock);
        breaker.RecordFailure();
        clock.Advance(Break);
        breaker.TryAcquire();

        breaker.Release();

        breaker.State.Should().Be(CircuitState.HalfOpen);
        breaker.TryAcquire().Should().BeTrue();
    }

    [Fact]
    public async Task Reranker_OpenCircuit_MakesNoHttpCallAndThrowsRetryableCircuitOpenAsync()
    {
        var clock = new FakeTimeProvider();
        var handler = FakeHandler.Status(HttpStatusCode.InternalServerError);
        using var sut = TeiFactory.Create(handler, clock, o => o.FailureThreshold = 2);

        for (var i = 0; i < 2; i++)
        {
            await FluentActions.Awaiting(() => sut.RerankAsync(TeiFactory.Results(2), "q"))
                .Should().ThrowAsync<NetIndexProviderException>();
        }
        handler.Calls.Should().Be(2);

        var act = () => sut.RerankAsync(TeiFactory.Results(2), "q");

        var ex = (await act.Should().ThrowAsync<TeiCircuitOpenException>()).Which;
        ex.IsRetryable.Should().BeTrue();
        ex.ErrorCode.Should().Be("circuit_open");
        handler.Calls.Should().Be(2);
    }

    [Fact]
    public async Task Reranker_HalfOpenProbeSuccess_ClosesCircuitAsync()
    {
        var clock = new FakeTimeProvider();
        var healthy = false;
        var handler = new FakeHandler((_, _) => Task.FromResult(healthy
            ? new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("[{\"index\":0,\"score\":0.1},{\"index\":1,\"score\":0.2}]"),
            }
            : new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)));
        using var sut = TeiFactory.Create(handler, clock, o => o.FailureThreshold = 1);
        await FluentActions.Awaiting(() => sut.RerankAsync(TeiFactory.Results(2), "q"))
            .Should().ThrowAsync<NetIndexProviderException>();
        clock.Advance(Break);
        healthy = true;

        var ranked = await sut.RerankAsync(TeiFactory.Results(2), "q");

        ranked.Select(r => r.Item.Id).Should().Equal("c1", "c0");
        sut.Breaker.State.Should().Be(CircuitState.Closed);
    }

    [Fact]
    public async Task Reranker_ClientErrorsDoNotTripTheBreakerAsync()
    {
        var handler = FakeHandler.Status(HttpStatusCode.BadRequest);
        using var sut = TeiFactory.Create(handler, configure: o => o.FailureThreshold = 1);

        for (var i = 0; i < 3; i++)
        {
            await FluentActions.Awaiting(() => sut.RerankAsync(TeiFactory.Results(2), "q"))
                .Should().ThrowAsync<NetIndexProviderException>();
        }

        sut.Breaker.State.Should().Be(CircuitState.Closed);
        handler.Calls.Should().Be(3);
    }
}
