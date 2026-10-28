using System.Net;
using System.Net.Http.Json;
using Azure.Data.Tables;
using BadgeCluster.Api;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace BadgeCluster.Api.Tests;

public sealed class TestClock : TimeProvider
{
    public DateTimeOffset Now { get; set; } = new(2026, 10, 28, 16, 0, 0, TimeSpan.Zero);
    public override DateTimeOffset GetUtcNow() => Now;
}

public sealed class ApiFactory(string tableName, TestClock clock) : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Storage:ConnectionString"] = Environment.GetEnvironmentVariable("TEST_STORAGE_CONNECTION") ?? "UseDevelopmentStorage=true",
            ["Storage:TableName"] = tableName
        }));
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(clock);
        });
    }
}

public sealed class IntegrationTests : IAsyncLifetime
{
    private readonly string tableName = "Test" + Guid.NewGuid().ToString("N");
    private readonly TestClock clock = new();
    private ApiFactory factory = null!;
    private HttpClient http = null!;
    private readonly string connection = Environment.GetEnvironmentVariable("TEST_STORAGE_CONNECTION") ?? "UseDevelopmentStorage=true";
    private static Contribution Input(long sequence = 1, long samples = 250, long inside = 196,
        string node = "node-a", string session = "session-a") => new(1, node, session, sequence, samples, inside);

    public Task InitializeAsync()
    {
        factory = new(tableName, clock);
        http = factory.CreateClient();
        return Task.CompletedTask;
    }
    public async Task DisposeAsync()
    {
        http.Dispose();
        await factory.DisposeAsync();
        await new TableClient(connection, tableName).DeleteAsync();
    }
    private async Task<ContributionResponse> Submit(Contribution input)
    {
        var response = await http.PostAsJsonAsync("/api/v1/contributions", input);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ContributionResponse>())!;
    }
    private async Task<ClusterSnapshot> Snapshot() =>
        (await http.GetFromJsonAsync<ClusterSnapshot>("/api/v1/cluster"))!;

    [Fact]
    public async Task Zero_samples_and_health_and_dashboard()
    {
        var snapshot = await Snapshot();
        Assert.Equal(0, snapshot.Samples);
        Assert.Null(snapshot.PiEstimate);
        Assert.Null(snapshot.UpdatedAt);
        Assert.Equal(HttpStatusCode.OK, (await http.GetAsync("/health")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await http.GetAsync("/health/ready")).StatusCode);
        var home = await http.GetAsync("/");
        Assert.Contains("One irrational goal", await home.Content.ReadAsStringAsync());
        Assert.True(home.Headers.Contains("Content-Security-Policy"));
    }

    [Fact]
    public async Task Duplicate_and_out_of_order_requests_do_not_double_count()
    {
        await Submit(Input());
        await Submit(Input(2, 500, 390));
        var duplicate = await Submit(Input(2, 500, 390));
        var older = await Submit(Input());
        Assert.Equal(2, duplicate.AcceptedSequence);
        Assert.Equal(500, older.AcceptedSamples);
        Assert.Equal(500, older.Cluster.Samples);
        Assert.Equal(390, older.Cluster.Inside);
        Assert.Equal(3.12, older.Cluster.PiEstimate);
    }

    [Fact]
    public async Task Response_loss_retry_and_concurrent_duplicate_requests_are_atomic()
    {
        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Submit(Input())));
        Assert.All(results, result => Assert.Equal(250, result.Cluster.Samples));
        Assert.Equal(250, (await Snapshot()).Samples);
    }

    [Fact]
    public async Task Concurrent_distinct_nodes_preserve_all_totals()
    {
        await Task.WhenAll(Enumerable.Range(0, 8).Select(i => Submit(Input(node: "node-" + i))));
        var snapshot = await Snapshot();
        Assert.Equal(2000, snapshot.Samples);
        Assert.Equal(1568, snapshot.Inside);
        Assert.Equal(8, snapshot.ActiveNodes);
    }

    [Fact]
    public async Task Sessions_count_independently_but_active_nodes_are_distinct()
    {
        await Submit(Input());
        await Submit(Input(session: "session-b"));
        var snapshot = await Snapshot();
        Assert.Equal(500, snapshot.Samples);
        Assert.Equal(1, snapshot.ActiveNodes);
    }

    [Fact]
    public async Task Active_nodes_expire_and_duplicate_retries_do_not_revive_them()
    {
        await Submit(Input());
        clock.Now = clock.Now.AddMinutes(5);
        Assert.Equal(0, (await Snapshot()).ActiveNodes);
        await Submit(Input());
        Assert.Equal(0, (await Snapshot()).ActiveNodes);
        await Submit(Input(2)); // New sequence with identical counters is a heartbeat.
        Assert.Equal(1, (await Snapshot()).ActiveNodes);
    }

    [Theory]
    [InlineData(0, 250, 196)]
    [InlineData(1, -1, 0)]
    [InlineData(1, 250, 251)]
    [InlineData(1, 250, -1)]
    [InlineData(1, 1000000000001, 0)]
    public async Task Invalid_values_are_rejected(long sequence, long samples, long inside)
    {
        var response = await http.PostAsJsonAsync("/api/v1/contributions", Input(sequence, samples, inside));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, (await Snapshot()).Samples);
    }

    [Fact]
    public async Task Invalid_protocol_ids_and_malformed_json_are_rejected()
    {
        foreach (var input in new[] { Input() with { ProtocolVersion = 2 }, Input() with { NodeId = "../node" },
                     Input() with { SessionId = new string('x', 65) }, Input() with { NodeId = null! } })
            Assert.Equal(HttpStatusCode.BadRequest, (await http.PostAsJsonAsync("/api/v1/contributions", input)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await http.PostAsync("/api/v1/contributions",
            new StringContent("{broken", System.Text.Encoding.UTF8, "application/json"))).StatusCode);
    }

    [Fact]
    public async Task Decreasing_counters_impossible_deltas_and_huge_increments_are_rejected()
    {
        await Submit(Input());
        foreach (var input in new[] { Input(2, 249, 195), Input(2, 500, 195), Input(2, 251, 200), Input(2, 1000251, 800000) })
            Assert.Equal(HttpStatusCode.Conflict, (await http.PostAsJsonAsync("/api/v1/contributions", input)).StatusCode);
        Assert.Equal(250, (await Snapshot()).Samples);
    }

    [Fact]
    public async Task Data_survives_complete_service_restart()
    {
        await Submit(Input());
        http.Dispose();
        await factory.DisposeAsync();
        factory = new(tableName, clock);
        http = factory.CreateClient();
        Assert.Equal(250, (await Snapshot()).Samples);
        Assert.Equal(250, (await Submit(Input())).Cluster.Samples);
    }

    [Fact]
    public async Task Oversized_body_is_rejected()
    {
        var response = await http.PostAsync("/api/v1/contributions", new StringContent(
            new string(' ', 2049), System.Text.Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        Assert.Equal(0, (await Snapshot()).Samples);
    }

    [Fact]
    public async Task Rate_limit_is_bounded_and_returns_retry_after()
    {
        HttpResponseMessage? rejected = null;
        for (var i = 0; i < 130; i++)
        {
            var response = await http.GetAsync("/health");
            if (response.StatusCode == HttpStatusCode.TooManyRequests) { rejected = response; break; }
            response.Dispose();
        }
        Assert.NotNull(rejected);
        Assert.NotNull(rejected.Headers.RetryAfter);
        rejected.Dispose();
    }
}
