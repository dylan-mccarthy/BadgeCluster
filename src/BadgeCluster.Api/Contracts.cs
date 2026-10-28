using System.Text.RegularExpressions;

namespace BadgeCluster.Api;

public sealed record Contribution(int ProtocolVersion, string NodeId, string SessionId,
    long Sequence, long Samples, long Inside)
{
    public string? Validate()
    {
        if (ProtocolVersion != 1) return "unsupported_protocol";
        if (!ValidId(NodeId) || !ValidId(SessionId)) return "invalid_id";
        if (Sequence is < 1 or > 1_000_000_000) return "invalid_sequence";
        if (Samples is < 0 or > 1_000_000_000_000 || Inside < 0 || Inside > Samples)
            return "invalid_counters";
        return null;
    }

    private static bool ValidId(string? id) => id is { Length: >= 1 and <= 64 }
        && Regex.IsMatch(id, "^[a-zA-Z0-9_-]+$", RegexOptions.CultureInvariant);
}

public sealed record ClusterSnapshot(long Samples, long Inside, int ActiveNodes, DateTimeOffset? UpdatedAt)
{
    public double? PiEstimate => Samples == 0 ? null : 4.0 * Inside / Samples;
}
public sealed record ContributionResponse(long AcceptedSequence, long AcceptedSamples, ClusterSnapshot Cluster);
public sealed class ContributionConflict(string code) : Exception(code);
public sealed class StoreBusyException() : Exception("Storage concurrency limit reached");
