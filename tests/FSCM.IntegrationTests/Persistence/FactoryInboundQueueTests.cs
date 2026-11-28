using System.Net;
using System.Net.Http.Json;
using Fscm.Traceability.Data;
using Fscm.Traceability.Services;
using FSCM.Application.Features.Picking;
using FSCM.Persistence.WarehouseWork;
using Microsoft.EntityFrameworkCore;
using FSCM.API.Errors;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;

namespace FSCM.IntegrationTests.Persistence;

public sealed class FactoryInboundQueueTests
{
    [Fact]
    public void Factory_queue_query_restricts_status_and_destination_before_returning_rows()
    {
        using var db = new FactoryDbContext(new DbContextOptionsBuilder<FactoryDbContext>()
            .UseNpgsql("Host=127.0.0.1;Database=unused;Username=unused").Options);
        var service = new ProductionService(db, null!, TimeProvider.System);
        var sql = service.DispatchedQuery([Guid.Parse("11111111-1111-1111-1111-111111111111")]).ToQueryString();
        Assert.Contains("Dispatched", sql);
        Assert.Contains("DestinationWarehouseId", sql);
        Assert.Contains("11111111-1111-1111-1111-111111111111", sql);
        Assert.Contains("FACTORY_CARTON", sql);
        Assert.Contains("ORDER BY", sql);
    }

    [Fact]
    public async Task Factory_queue_rejects_an_unscoped_or_invalid_request_without_reading_database()
    {
        var service = new ProductionService(null!, null!, TimeProvider.System);
        foreach (var ids in new[] { Array.Empty<Guid>(), new[] { Guid.Empty }, Enumerable.Range(0, 501).Select(_ => Guid.NewGuid()).ToArray() })
            await Assert.ThrowsAsync<Fscm.Traceability.Models.FactoryException>(() => service.DispatchedAsync(ids, default));
    }

    [Fact]
    public async Task Gateway_sends_only_the_requested_warehouse_scope_and_deserializes_the_fixed_destination()
    {
        var warehouse = Guid.NewGuid();
        var handler = new QueueHandler(request =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal($"https://factory.test/api/integration/dispatches?warehouseIds={warehouse:D}", request.RequestUri!.ToString());
            return new(HttpStatusCode.OK) { Content = JsonContent.Create(new[] {
                new FactoryInboundPallet(Guid.NewGuid(), "PID-1", warehouse, DateTimeOffset.UtcNow,
                    "BID-1", "SKU001", "Product", 6, new DateOnly(2027, 1, 1)) }) };
        });
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://factory.test/") };
        var gateway = new FactoryHttpGateway(http);
        Assert.Empty(await gateway.DispatchedAsync([], default));
        Assert.Equal(0, handler.Calls);
        var result = await gateway.DispatchedAsync([warehouse, warehouse], default);
        Assert.Equal(warehouse, Assert.Single(result).DestinationWarehouseId);
        Assert.Null(result[0].ReceiptDocumentId);
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task Receiving_and_queue_require_explicit_grants_before_running_services()
    {
        using var factory = new FSCM.IntegrationTests.Api.IdentityAuthorizationTests.IdentityApiFactory();
        factory.SetPermissions("WarehouseKeeper");
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
            "Bearer", factory.CreateToken(factory.WarehouseKeeper));
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/v1/warehouse-documents/factory-pending")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync(
            "/api/v1/warehouse-documents/factory-receipts/from-pallet", new { palletCode = "PID-1" })).StatusCode);
    }

    [Theory]
    [InlineData(HttpStatusCode.ServiceUnavailable, "service_unavailable", 503)]
    [InlineData(HttpStatusCode.InternalServerError, "service_unavailable", 503)]
    [InlineData(HttpStatusCode.Unauthorized, "service_unavailable", 503)]
    [InlineData(HttpStatusCode.Forbidden, "service_unavailable", 503)]
    [InlineData(HttpStatusCode.TooManyRequests, "service_unavailable", 503)]
    [InlineData(HttpStatusCode.Conflict, "conflict", 409)]
    [InlineData(HttpStatusCode.NotFound, "not_found", 404)]
    public async Task Gateway_and_http_handler_distinguish_provider_outages_from_business_conflicts(
        HttpStatusCode upstream, string code, int status)
    {
        using var http = new HttpClient(new QueueHandler(_ => new(upstream))) { BaseAddress = new Uri("https://factory.test/") };
        var error = await Assert.ThrowsAsync<PickingOperationException>(() =>
            new FactoryHttpGateway(http).DispatchedAsync([Guid.NewGuid()], default));
        Assert.Equal(code, error.Code);
        await AssertProblemStatus(error, status);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("null")]
    public async Task Malformed_factory_responses_are_bad_gateway(string payload)
    {
        using var http = new HttpClient(new QueueHandler(_ => new(HttpStatusCode.OK)
        { Content = new StringContent(payload) })) { BaseAddress = new Uri("https://factory.test/") };
        var error = await Assert.ThrowsAsync<PickingOperationException>(() =>
            new FactoryHttpGateway(http).DispatchedAsync([Guid.NewGuid()], default));
        Assert.Equal("bad_gateway", error.Code);
        await AssertProblemStatus(error, 502);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Connection_failure_and_timeout_are_service_unavailable(bool timeout)
    {
        using var http = new HttpClient(new QueueHandler(_ => timeout
            ? throw new TaskCanceledException("timeout") : throw new HttpRequestException("connection refused")))
        { BaseAddress = new Uri("https://factory.test/") };
        var error = await Assert.ThrowsAsync<PickingOperationException>(() =>
            new FactoryHttpGateway(http).DispatchedAsync([Guid.NewGuid()], default));
        Assert.Equal("service_unavailable", error.Code);
        await AssertProblemStatus(error, 503);
    }

    [Fact]
    public async Task Caller_cancellation_is_not_reported_as_a_provider_failure()
    {
        using var source = new CancellationTokenSource();
        using var http = new HttpClient(new QueueHandler(_ =>
        {
            source.Cancel();
            throw new OperationCanceledException(source.Token);
        })) { BaseAddress = new Uri("https://factory.test/") };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new FactoryHttpGateway(http).DispatchedAsync([Guid.NewGuid()], source.Token));
    }

    private static async Task AssertProblemStatus(PickingOperationException error, int status)
    {
        var writer = new CapturingProblems();
        var handler = new PickingExceptionHandler(writer, NullLogger<PickingExceptionHandler>.Instance);
        var context = new DefaultHttpContext();
        Assert.True(await handler.TryHandleAsync(context, error, default));
        Assert.Equal(status, context.Response.StatusCode);
        Assert.Equal(status, writer.Problem!.Status);
        Assert.Equal(error.Code, writer.Problem.Extensions["code"]);
    }

    private sealed class CapturingProblems : IProblemDetailsService
    {
        public ProblemDetails? Problem { get; private set; }
        public ValueTask WriteAsync(ProblemDetailsContext context)
        {
            Problem = context.ProblemDetails;
            return ValueTask.CompletedTask;
        }
        public ValueTask<bool> TryWriteAsync(ProblemDetailsContext context)
        {
            Problem = context.ProblemDetails;
            return ValueTask.FromResult(true);
        }
    }

    private sealed class QueueHandler(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(response(request));
        }
    }
}
