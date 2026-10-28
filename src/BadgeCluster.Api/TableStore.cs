using Azure;
using Azure.Data.Tables;

namespace BadgeCluster.Api;

public sealed class Counters : ITableEntity
{
    public string PartitionKey { get; set; } = "v1";
    public string RowKey { get; set; } = "";
    public ETag ETag { get; set; }
    public DateTimeOffset? Timestamp { get; set; }
    public long Samples { get; set; }
    public long Inside { get; set; }
    public long Sequence { get; set; }
    public int Sessions { get; set; }
    public DateTimeOffset? LastAcceptedAt { get; set; }
}

public sealed class TableStore(TableClient table, TimeProvider clock)
{
    private const string Partition = "v1";
    public Task Initialize(CancellationToken ct) => table.CreateIfNotExistsAsync(ct);

    private async Task<Counters?> Read(string key, CancellationToken ct)
    {
        var response = await table.GetEntityIfExistsAsync<Counters>(Partition, key, cancellationToken: ct);
        return response.HasValue ? response.Value : null;
    }

    public async Task<ClusterSnapshot> Snapshot(CancellationToken ct)
    {
        var totals = await Read("totals", ct);
        return await Snapshot(totals ?? new Counters(), ct);
    }

    private async Task<ClusterSnapshot> Snapshot(Counters totals, CancellationToken ct)
    {
        var cutoff = clock.GetUtcNow().AddMinutes(-5);
        var filter = TableClient.CreateQueryFilter(
            $"PartitionKey eq {Partition} and RowKey ge {"n:"} and RowKey lt {"n;"} and LastAcceptedAt gt {cutoff}");
        var active = 0;
        await foreach (var _ in table.QueryAsync<Counters>(filter, select: ["RowKey"], cancellationToken: ct))
            active++;
        return new(totals.Samples, totals.Inside, active, totals.LastAcceptedAt);
    }

    public async Task<ContributionResponse> Submit(Contribution input, CancellationToken ct)
    {
        var key = "s:" + input.SessionId + ":" + input.NodeId;
        var nodeKey = "n:" + input.NodeId;
        for (var attempt = 0; attempt < 12; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            var session = await Read(key, ct);
            if (session is not null && input.Sequence <= session.Sequence)
                return new(session.Sequence, session.Samples, await Snapshot(ct));
            var delta = input.Samples - (session?.Samples ?? 0);
            var insideDelta = input.Inside - (session?.Inside ?? 0);
            if (delta < 0 || insideDelta < 0 || insideDelta > delta)
                throw new ContributionConflict("non_monotonic_counters");
            if (delta > 1_000_000) throw new ContributionConflict("increment_too_large");
            var totals = await Read("totals", ct);
            if (session is null && (totals?.Sessions ?? 0) >= 100_000)
                throw new ContributionConflict("session_quota_reached");
            var node = await Read(nodeKey, ct);
            var newSession = session is null;
            var newTotals = totals is null;
            var newNode = node is null;
            session ??= new Counters { RowKey = key };
            totals ??= new Counters { RowKey = "totals" };
            node ??= new Counters { RowKey = nodeKey };
            var now = clock.GetUtcNow();
            session.Sequence = input.Sequence;
            session.Samples = input.Samples;
            session.Inside = input.Inside;
            session.LastAcceptedAt = now;
            totals.Samples = checked(totals.Samples + delta);
            totals.Inside = checked(totals.Inside + insideDelta);
            // Keep JSON counters exactly representable by dashboard JavaScript.
            if (totals.Samples > 9_000_000_000_000_000)
                throw new ContributionConflict("global_sample_quota_reached");
            totals.Sessions += newSession ? 1 : 0;
            totals.LastAcceptedAt = now;
            node.LastAcceptedAt = now;
            // All three entities share one partition. ETags protect each write;
            // the transaction either commits every entity or none of them.
            var actions = new[] { Action(session, newSession), Action(totals, newTotals), Action(node, newNode) };
            try
            {
                await table.SubmitTransactionAsync(actions, ct);
                return new(input.Sequence, input.Samples, await Snapshot(totals, ct));
            }
            catch (RequestFailedException ex) when (ex.Status is 409 or 412)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(Random.Shared.Next(5, 50) * (attempt + 1)), ct);
            }
        }
        throw new StoreBusyException();
    }

    private static TableTransactionAction Action(Counters entity, bool create) => new(
        create ? TableTransactionActionType.Add : TableTransactionActionType.UpdateReplace, entity, entity.ETag);
}
