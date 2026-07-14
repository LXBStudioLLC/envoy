using Envoy.GhostDetection.Models;

namespace Envoy.Discovery.Sources;

/// <summary>
/// Reads a single public, unauthenticated ATS job board and returns its postings.
/// Implementations skip individual malformed postings, but let board-level failures
/// (unknown board, network/timeout) propagate so the caller can report them.
/// </summary>
public interface IAtsBoardSource
{
    /// <summary>Which ATS this source reads.</summary>
    JobSource Ats { get; }

    /// <summary>Fetch all currently-published postings for one board token.</summary>
    Task<IReadOnlyList<JobPosting>> FetchBoardAsync(string token, string? companyName, CancellationToken ct = default);

    /// <summary>
    /// Lightly probe whether a board exists for the given token without fetching all
    /// postings. Returns true if the board is reachable and has at least one posting.
    /// Used by the auto-discovery feature to check if a company name maps to a board.
    /// </summary>
    Task<bool> BoardExistsAsync(string token, CancellationToken ct = default);
}
