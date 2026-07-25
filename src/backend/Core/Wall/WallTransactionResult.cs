namespace CvAut
{
    /// <summary>
    /// Outcome of a single wall upgrade transaction, carrying the accounting that
    /// WallDebugRecorder needs for its session counters.
    /// </summary>
    internal sealed record WallTransactionResult(int VerifiedCount, string Reason, string Resource = "none", int CandidateMatchCount = 0, int RequestedCount = 0, int Cost = 0)
    {
        public static WallTransactionResult Skip(string reason) => new(0, reason);
        public WallTransactionResult WithCandidateMatchCount(int count) => this with { CandidateMatchCount = count };
        public WallTransactionResult WithCost(int cost) => this with { Cost = cost };
        public static WallTransactionResult Verified(string resource, int count, int cost, int candidateMatchCount, int requestedCount) =>
            new(count, "verified", Resource: resource, CandidateMatchCount: candidateMatchCount, RequestedCount: requestedCount, Cost: cost);
    }
}
