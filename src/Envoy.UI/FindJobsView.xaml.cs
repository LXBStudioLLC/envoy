using Envoy.Core.Configuration;
using Envoy.Core.Models;
using Envoy.Core.Services;
using Envoy.Discovery;
using Envoy.Discovery.Models;
using Envoy.GhostDetection;
using Envoy.GhostDetection.Models;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using static Envoy.UI.Theme;

namespace Envoy.UI;

public partial class FindJobsView : UserControl
{
    private readonly JobDiscoveryService _discovery;
    private readonly GhostScorer _scorer;
    private readonly EnvoySettings _settings;
    private readonly IJobEventRepository _jobEvents;
    private List<AtsBoardRef> _boards = new();
    private List<DiscoveredJobItem> _lastItems = new();

    public FindJobsView(JobDiscoveryService discovery, GhostScorer scorer, EnvoySettings settings, IJobEventRepository jobEvents)
    {
        _discovery = discovery;
        _scorer = scorer;
        _settings = settings;
        _jobEvents = jobEvents;
        InitializeComponent();
        Loaded += FindJobsView_Loaded;
    }

    private void FindJobsView_Loaded(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(_settings.BraveSearchApiKeyEncrypted))
        {
            StatusText.Text = "✓ Brave key saved — leave blank to reuse it, or enter a new key to replace.";
            StatusText.Foreground = Gray;
        }
        LoadBoards();
    }

    private void LoadBoards()
    {
        _boards = _discovery.DefaultBoards.ToList();
        RefreshBoardList();
    }

    private void RefreshBoardList()
    {
        var items = _boards.Select(b => new BoardListItem
        {
            Id = $"{b.Ats}:{b.Token}",
            Company = b.CompanyName ?? b.Token,
            Ats = b.Ats.ToString()
        }).ToList();

        BoardList.ItemsSource = items;
        BoardCountLabel.Text = $"{_boards.Count} board{(_boards.Count == 1 ? "" : "s")}";
        NoBoardsLabel.Visibility = _boards.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private DiscoveryQuery BuildQuery() => new()
    {
        Keywords = TxtKeywords.Text?.Trim(),
        Location = TxtLocation.Text?.Trim(),
        RemoteOnly = ChkRemote.IsChecked == true,
        MaxResults = 100
    };

    private async void BtnScanBoards_Click(object sender, RoutedEventArgs e)
    {
        SetBusy(true, "SCANNING PUBLIC BOARDS...");
        try
        {
            var result = await _discovery.SearchBoardsAsync(_boards, BuildQuery());
            await RenderAsync(result);
        }
        catch (Exception ex) { ShowError(ex.Message); }
        finally { SetBusy(false); }
    }

    private async void BtnWebSearch_Click(object sender, RoutedEventArgs e)
    {
        var typed = TxtBraveKey.Password?.Trim() ?? "";
        if (!string.IsNullOrEmpty(typed)) SaveKeyIfChanged(typed);
        var key = !string.IsNullOrEmpty(typed) ? typed : (_settings.BraveSearchApiKey ?? "");
        SetBusy(true, "SEARCHING THE WEB...");
        try
        {
            var result = await _discovery.WebSearchAsync(key, TxtKeywords.Text?.Trim() ?? "", BuildQuery());
            await RenderAsync(result);
        }
        catch (Exception ex) { ShowError(ex.Message); }
        finally { SetBusy(false); }
    }

    private async void BtnAddCompany_Click(object sender, RoutedEventArgs e)
    {
        var companyName = TxtAddCompany.Text?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(companyName))
        {
            AddCompanyStatus.Text = "Enter a company name first.";
            AddCompanyStatus.Foreground = Yellow;
            return;
        }

        BtnAddCompany.IsEnabled = false;
        AddCompanyStatus.Text = $"Probing ATS platforms for \"{companyName}\"...";
        AddCompanyStatus.Foreground = Cyan;

        try
        {
            var board = await _discovery.DiscoverBoardAsync(companyName);
            if (board != null)
            {
                if (_boards.Any(b => b.Ats == board.Ats && b.Token == board.Token))
                {
                    AddCompanyStatus.Text = $"{companyName} is already in your board list ({board.Ats}).";
                    AddCompanyStatus.Foreground = Gray;
                }
                else
                {
                    _boards.Add(board);
                    RefreshBoardList();
                    SeedBoards.Save(_boards);
                    AddCompanyStatus.Text = $"✓ Found {companyName} on {board.Ats} (token: {board.Token}). Added to your boards.";
                    AddCompanyStatus.Foreground = Green;
                    TxtAddCompany.Clear();
                }
            }
            else
            {
                AddCompanyStatus.Text = $"✕ No public job board found for \"{companyName}\" on any ATS. Try a different name or check the spelling.";
                AddCompanyStatus.Foreground = Red;
            }
        }
        catch (Exception ex)
        {
            AddCompanyStatus.Text = $"✕ Error: {ex.Message}";
            AddCompanyStatus.Foreground = Red;
        }
        finally
        {
            BtnAddCompany.IsEnabled = true;
        }
    }

    private void BtnRemoveBoard_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string id)
        {
            var parts = id.Split(':', 2);
            if (parts.Length == 2 && Enum.TryParse<JobSource>(parts[0], out var ats))
            {
                _boards.RemoveAll(b => b.Ats == ats && b.Token == parts[1]);
                RefreshBoardList();
                SeedBoards.Save(_boards);
            }
        }
    }

    private void BtnSaveKey_Click(object sender, RoutedEventArgs e)
    {
        var key = TxtBraveKey.Password?.Trim() ?? "";
        if (string.IsNullOrEmpty(key))
        {
            StatusText.Text = "Enter a key first.";
            StatusText.Foreground = Yellow;
            return;
        }
        if (SaveKeyIfChanged(key))
        {
            TxtBraveKey.Clear();
            StatusText.Text = "✓ BRAVE KEY SAVED";
            StatusText.Foreground = Green;
        }
        else
        {
            StatusText.Text = "✕ Could not save — settings.json may be locked; key NOT stored.";
            StatusText.Foreground = Red;
        }
    }

    private bool SaveKeyIfChanged(string key)
    {
        if (string.IsNullOrEmpty(key) || key == _settings.BraveSearchApiKey)
            return true;
        try { _settings.BraveSearchApiKey = key; }
        catch { return false; }
        return _settings.Save();
    }

    private async Task RenderAsync(DiscoveryResult result)
    {
        var items = new List<DiscoveredJobItem>(result.Jobs.Count);
        foreach (var job in result.Jobs)
        {
            var score = await _scorer.ScoreAsync(job, localOnly: true);
            items.Add(ToItem(job, score));
        }
        ResultsList.ItemsSource = items;
        _lastItems = items;
        RecordSightings(items);

        if (items.Count == 0)
        {
            var msg = result.Errors.Count > 0 ? string.Join("  |  ", result.Errors) : "No matching jobs found.";
            StatusText.Text = $"⚠ {msg}";
            StatusText.Foreground = result.Errors.Count > 0 ? Yellow : Gray;
        }
        else
        {
            var suffix = result.Errors.Count > 0 ? $"  ·  {result.Errors.Count} source(s) unavailable" : "";
            StatusText.Text = $"✓ {items.Count} JOB(S) FOUND{suffix}";
            StatusText.Foreground = Green;
        }
    }

    private static DiscoveredJobItem ToItem(JobPosting job, GhostScore score)
    {
        var (brush, label) = score.Band switch
        {
            RiskBand.High => ((Brush)Red, "HIGH"),
            RiskBand.Elevated => (Yellow, "ELEVATED"),
            _ => (Green, "OK")
        };

        var evidence = score.TopEvidence.Length > 0
            ? string.Join("\n", score.TopEvidence.Select(ev => "• " + ev))
            : "";

        var meta = $"{job.Source} · {(job.PostedAtUtc?.ToString("yyyy-MM-dd") ?? "date n/a")}";
        if (!string.IsNullOrWhiteSpace(job.SalaryText))
            meta += $" · {job.SalaryText}";

        return new DiscoveredJobItem
        {
            Title = string.IsNullOrWhiteSpace(job.JobTitle) ? "—" : job.JobTitle,
            Company = string.IsNullOrWhiteSpace(job.CompanyName) ? "—" : job.CompanyName,
            Location = string.IsNullOrWhiteSpace(job.Location) ? "—" : job.Location,
            Meta = meta,
            RiskText = score.Band == RiskBand.Neutral ? "OK" : $"{label} {score.RiskScore:0}",
            RiskBrush = brush,
            Evidence = evidence,
            EvidenceVisibility = string.IsNullOrEmpty(evidence) ? Visibility.Collapsed : Visibility.Visible,
            Url = job.Url,
            Posting = job,
            Snapshot = new GhostScoreSnapshot(score.RiskScore, score.Band.ToString(), score.TopEvidence)
        };
    }

    // Ledger bookkeeping is best-effort and off the UI thread; a bookkeeping
    // failure must never break the search results on screen.
    private void RecordSightings(List<DiscoveredJobItem> items)
    {
        var sightings = items
            .Where(i => i.Posting != null)
            .Select(i => JobEvent.ForPosting(
                JobEventType.Sighted,
                i.Posting!.Url, i.Posting.JobTitle, i.Posting.CompanyName,
                i.Posting.Source.ToString(), i.Snapshot))
            .ToList();
        if (sightings.Count == 0) return;

        _ = Task.Run(async () =>
        {
            try { await _jobEvents.RecordSightingsAsync(sightings); }
            catch { /* bookkeeping only */ }
        });
    }

    private void RecordItemEvent(DiscoveredJobItem item, JobEventType type)
    {
        if (item.Posting == null) return;
        var jobEvent = JobEvent.ForPosting(
            type, item.Posting.Url, item.Posting.JobTitle, item.Posting.CompanyName,
            item.Posting.Source.ToString(), item.Snapshot);

        _ = Task.Run(async () =>
        {
            try { await _jobEvents.AddAsync(jobEvent); }
            catch { /* bookkeeping only */ }
        });
    }

    private void CmbSort_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_lastItems.Count == 0 || CmbSort?.SelectedItem == null) return;

        var sortIndex = CmbSort.SelectedIndex;
        var sorted = sortIndex switch
        {
            0 => _lastItems.OrderByDescending(i => i.Meta).ToList(),  // Newest first (meta contains date)
            1 => _lastItems.OrderByDescending(i => i.RiskText).ToList(),  // Ghost score
            2 => _lastItems.OrderBy(i => i.Company).ToList(),  // Company A-Z
            _ => _lastItems
        };
        ResultsList.ItemsSource = sorted;
    }

    private void BtnView_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is DiscoveredJobItem item && !string.IsNullOrWhiteSpace(item.Url))
        {
            RecordItemEvent(item, JobEventType.Viewed);
            try { Process.Start(new ProcessStartInfo(item.Url) { UseShellExecute = true }); }
            catch (Exception ex) { ShowError($"Could not open link: {ex.Message}"); }
        }
    }

    private void BtnSkip_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not DiscoveredJobItem item) return;

        RecordItemEvent(item, JobEventType.Skipped);

        // Drop the row but keep whatever order is currently on screen.
        _lastItems.Remove(item);
        var current = (ResultsList.ItemsSource as IEnumerable<DiscoveredJobItem>)?.Where(i => i != item).ToList()
            ?? _lastItems.ToList();
        ResultsList.ItemsSource = current;

        var flagged = item.Snapshot?.Band is "High" or "Elevated";
        StatusText.Text = flagged
            ? $"✓ GHOST DODGED  ·  {item.RiskText}  ·  {item.Company}"
            : $"SKIPPED  ·  {item.Company}";
        StatusText.Foreground = flagged ? Green : Gray;
    }

    private void SetBusy(bool busy, string? message = null)
    {
        BtnScanBoards.IsEnabled = !busy;
        BtnWebSearch.IsEnabled = !busy;
        if (message != null)
        {
            StatusText.Text = message;
            StatusText.Foreground = Cyan;
        }
    }

    private void ShowError(string message)
    {
        StatusText.Text = $"✕ {message}";
        StatusText.Foreground = Red;
    }
}

public class DiscoveredJobItem
{
    public string Title { get; init; } = "";
    public string Company { get; init; } = "";
    public string Location { get; init; } = "";
    public string Meta { get; init; } = "";
    public string RiskText { get; init; } = "";
    public Brush RiskBrush { get; init; } = Brushes.Gray;
    public string Evidence { get; init; } = "";
    public Visibility EvidenceVisibility { get; init; } = Visibility.Collapsed;
    public string Url { get; init; } = "";

    // Raw posting + score snapshot so user actions on this row can be recorded
    // in the ledger with the evidence that was on screen.
    public JobPosting? Posting { get; init; }
    public GhostScoreSnapshot? Snapshot { get; init; }
}

public class BoardListItem
{
    public string Id { get; init; } = "";
    public string Company { get; init; } = "";
    public string Ats { get; init; } = "";
}