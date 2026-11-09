using System;

namespace PdfConversionApi;

/// <summary>The account's plan, its limits and this month's count, from <c>GET /v2/usage</c>.</summary>
public sealed class Usage
{
    internal Usage(
        string plan,
        long included,
        long used,
        long? remaining,
        bool overage,
        int running,
        int concurrentLimit,
        int maxUploadMB,
        DateTimeOffset periodStart,
        DateTimeOffset periodEnd)
    {
        Plan = plan;
        Included = included;
        Used = used;
        Remaining = remaining;
        Overage = overage;
        Running = running;
        ConcurrentLimit = concurrentLimit;
        MaxUploadMB = maxUploadMB;
        PeriodStart = periodStart;
        PeriodEnd = periodEnd;
    }

    /// <summary>The plan's name, such as <c>Free</c>.</summary>
    public string Plan { get; }

    /// <summary>Successful conversions included each calendar month.</summary>
    public long Included { get; }

    /// <summary>Successful conversions so far this month.</summary>
    public long Used { get; }

    /// <summary>What is left of <see cref="Included"/> this month, or null when the API sends none.</summary>
    public long? Remaining { get; }

    /// <summary>Whether the plan keeps converting past <see cref="Included"/>.</summary>
    public bool Overage { get; }

    /// <summary>Conversions running for the account right now.</summary>
    public int Running { get; }

    /// <summary>How many conversions the plan runs at the same time.</summary>
    public int ConcurrentLimit { get; }

    /// <summary>The largest request the plan accepts, all files together, in MB.</summary>
    public int MaxUploadMB { get; }

    /// <summary>The start of this month's count, in UTC.</summary>
    public DateTimeOffset PeriodStart { get; }

    /// <summary>The end of this month's count, in UTC: the count starts again then.</summary>
    public DateTimeOffset PeriodEnd { get; }

    /// <inheritdoc />
    public override string ToString() => "Usage(" + Plan + ", " + Used + " of " + Included + " used)";
}
