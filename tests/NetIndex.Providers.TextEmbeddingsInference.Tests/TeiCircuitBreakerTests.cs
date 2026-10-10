using System.Net;
using FluentAssertions;
using NetIndex.Core.Abstractions;
using Xunit;

namespace NetIndex.Providers.TextEmbeddingsInference.Tests;

public class TeiCircuitBreakerTests
{
    private static readonly TimeSpan Break = TimeSpan.FromSeconds(30);

    private static Admission Acquire(TeiCircuitBreaker breaker)
    {
        breaker.TryAcquire(out var admission).Should().BeTrue();
        return admission;
    }

    [Fact]
    public void Breaker_OpensAfterThresholdConsecutiveFailures()
    {
        var breaker = new TeiCircuitBreaker(3, Break, new FakeTimeProvider());

        breaker.RecordFailure(Acquire(breaker));
        breaker.RecordFailure(Acquire(breaker));
        breaker.State.Should().Be(CircuitState.Closed);
        breaker.RecordFailure(Acquire(breaker));

        breaker.State.Should().Be(CircuitState.Open);
        breaker.TryAcquire(out _).Should().BeFalse();
    }

    [Fact]
    public void Breaker_SuccessResetsConsecutiveFailureCount()
    {
        var breaker = new TeiCircuitBreaker(3, Break, new FakeTimeProvider());

        breaker.RecordFailure(Acquire(breaker));
        breaker.RecordFailure(Acquire(breaker));
        breaker.RecordSuccess(Acquire(breaker));
        breaker.RecordFailure(Acquire(breaker));
        breaker.RecordFailure(Acquire(breaker));

        breaker.State.Should().Be(CircuitState.Closed);
    }

    [Fact]
    public void Breaker_HalfOpenAdmitsExactlyOneProbe()
    {
        var clock = new FakeTimeProvider();
        var breaker = new TeiCircuitBreaker(1, Break, clock);
        breaker.RecordFailure(Acquire(breaker));

        clock.Advance(Break);

        breaker.State.Should().Be(CircuitState.HalfOpen);
        breaker.TryAcquire(out _).Should().BeTrue();
        breaker.TryAcquire(out _).Should().BeFalse();
    }

    [Fact]
    public void Breaker_SuccessfulProbeCloses()
    {
        var clock = new FakeTimeProvider();
        var breaker = new TeiCircuitBreaker(1, Break, clock);
        breaker.RecordFailure(Acquire(breaker));
        clock.Advance(Break);
        var probe = Acquire(breaker);

        breaker.RecordSuccess(probe);

        breaker.State.Should().Be(CircuitState.Closed);
        breaker.TryAcquire(out _).Should().BeTrue();
    }

    [Fact]
    public void Breaker_FailedProbeReopensForAFullBreak()
    {
        var clock = new FakeTimeProvider();
        var breaker = new TeiCircuitBreaker(3, Break, clock);
        for (var i = 0; i < 3; i++)
        {
            breaker.RecordFailure(Acquire(breaker));
        }
        clock.Advance(Break);
        var probe = Acquire(breaker);

        breaker.RecordFailure(probe);

        breaker.State.Should().Be(CircuitState.Open);
        clock.Advance(Break - TimeSpan.FromSeconds(1));
        breaker.TryAcquire(out _).Should().BeFalse();
        clock.Advance(TimeSpan.FromSeconds(1));
        breaker.State.Should().Be(CircuitState.HalfOpen);
    }

    [Fact]
    public void Breaker_ReleaseFreesAProbeSlotWithoutChangingState()
    {
        var clock = new FakeTimeProvider();
        var breaker = new TeiCircuitBreaker(1, Break, clock);
        breaker.RecordFailure(Acquire(breaker));
        clock.Advance(Break);
        var probe = Acquire(breaker);

        breaker.Release(probe);

        breaker.State.Should().Be(CircuitState.HalfOpen);
        breaker.TryAcquire(out _).Should().BeTrue();
    }

    [Fact]
    public void Breaker_LateSuccessFromBeforeTheCircuitOpenedCannotCloseIt()
    {
        var breaker = new TeiCircuitBreaker(1, Break, new FakeTimeProvider());
        var slowCall = Acquire(breaker);
        breaker.RecordFailure(Acquire(breaker));
        breaker.State.Should().Be(CircuitState.Open);

        breaker.RecordSuccess(slowCall);

        breaker.State.Should().Be(CircuitState.Open, "a stale admission is ignored");
    }

    [Fact]
    public void Breaker_LateFailureOrReleaseCannotFreeAProbeSlotItDoesNotOwn()
    {
        var clock = new FakeTimeProvider();
        var breaker = new TeiCircuitBreaker(1, Break, clock);
        var slowCall = Acquire(breaker);
        breaker.RecordFailure(Acquire(breaker));
        clock.Advance(Break);
        Acquire(breaker).Should().NotBe(slowCall);

        breaker.RecordFailure(slowCall);
        breaker.Release(slowCall);

        breaker.TryAcquire(out _).Should().BeFalse("the probe slot is still held by the real probe");
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

    [Fact]
    public async Task Reranker_InvalidResponseTripsTheBreakerAsync()
    {
        var handler = FakeHandler.Json("[]");
        using var sut = TeiFactory.Create(handler, configure: o => o.FailureThreshold = 1);

        await FluentActions.Awaiting(() => sut.RerankAsync(TeiFactory.Results(2), "q"))
            .Should().ThrowAsync<NetIndexProviderException>();

        sut.Breaker.State.Should().Be(CircuitState.Open);
        await FluentActions.Awaiting(() => sut.RerankAsync(TeiFactory.Results(2), "q"))
            .Should().ThrowAsync<TeiCircuitOpenException>();
        handler.Calls.Should().Be(1, "an open circuit makes no HTTP request");
    }

    [Fact]
    public async Task Reranker_NullItemsAreInvalidResponseAndCountAsFailureAsync()
    {
        using var sut = TeiFactory.Create(FakeHandler.Json("[null,null]"), configure: o => o.FailureThreshold = 1);

        var act = () => sut.RerankAsync(TeiFactory.Results(2), "q");

        (await act.Should().ThrowAsync<NetIndexProviderException>()).Which.ErrorCode.Should().Be("invalid_response");
        sut.Breaker.State.Should().Be(CircuitState.Open);
    }

    [Fact]
    public async Task Reranker_UnexpectedExceptionDuringHalfOpenProbe_ReopensAndFreesTheSlotAsync()
    {
        var clock = new FakeTimeProvider();
        var mode = "fail";
        var handler = new FakeHandler((_, _) => mode switch
        {
            "fail" => Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError)),
            "boom" => throw new ArgumentException("boom"),
            _ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("[{\"index\":0,\"score\":0.1},{\"index\":1,\"score\":0.2}]"),
            }),
        });
        using var sut = TeiFactory.Create(handler, clock, o => o.FailureThreshold = 1);
        await FluentActions.Awaiting(() => sut.RerankAsync(TeiFactory.Results(2), "q")).Should().ThrowAsync<NetIndexProviderException>();
        clock.Advance(Break);
        mode = "boom";

        var act = () => sut.RerankAsync(TeiFactory.Results(2), "q");

        (await act.Should().ThrowAsync<NetIndexProviderException>()).Which.ErrorCode.Should().Be("unexpected_failure");
        sut.Breaker.State.Should().Be(CircuitState.Open);
        clock.Advance(Break);
        mode = "ok";
        (await sut.RerankAsync(TeiFactory.Results(2), "q")).Should().HaveCount(2);
        sut.Breaker.State.Should().Be(CircuitState.Closed);
    }

    [Fact]
    public async Task Reranker_CallerCancellationOfHalfOpenProbe_ReleasesTheSlotAsync()
    {
        var clock = new FakeTimeProvider();
        var hang = false;
        var handler = new FakeHandler(async (_, ct) =>
        {
            if (hang)
            {
                await Task.Delay(Timeout.Infinite, ct);
            }

            return new HttpResponseMessage(HttpStatusCode.InternalServerError);
        });
        using var sut = TeiFactory.Create(handler, clock, o => o.FailureThreshold = 1);
        await FluentActions.Awaiting(() => sut.RerankAsync(TeiFactory.Results(2), "q")).Should().ThrowAsync<NetIndexProviderException>();
        clock.Advance(Break);
        hang = true;
        using var cts = new CancellationTokenSource();
        cts.CancelAfter(50);

        await FluentActions.Awaiting(() => sut.RerankAsync(TeiFactory.Results(2), "q", cts.Token))
            .Should().ThrowAsync<OperationCanceledException>();

        sut.Breaker.State.Should().Be(CircuitState.HalfOpen);
        sut.Breaker.TryAcquire(out _).Should().BeTrue("the cancelled probe released its slot");
    }
}
