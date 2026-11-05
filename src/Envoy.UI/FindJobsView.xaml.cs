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
    private readonly IApplicationLogRepository _appLogs;
    private readonly ApplicationQueueService _queue;
    private readonly IApplicationQueueRepository _queueRepo;
    private List<AtsBoardRef> _boards = new();
    private List<DiscoveredJobItem> _lastItems = new();

    /// <summary>APP-02: raised when the user hits APPLY on a result row; the
    /// MainWindow routes the carried posting + score into the Apply view.</summary>
    public event Action<DiscoveredJobItem>? ApplyRequested;

    /// <summary>APP-07: raised for an AppReady queue item ("apply now" from the
    /// queue panel); routes to a pre-tailored, review-ready session.</summary>
    public event Action<QueuedApplication>? PreparedApplyRequested;

    // ── APP-11: batch queue selection ────────────────────────────────────

    private void BtnSelectAll_Click(object sender, RoutedEventArgs e) => SetBatchSelection(true);

    private void BtnClearSelection_Click(object sender, RoutedEventArgs e) => SetBatchSelection(false);

    private void SetBatchSelection(bool value)
    {
        // Mutate the full history first (survives re-sorts), then the visible list.
        foreach (var item in _lastItems.Where(i => i.Posting is not null && !string.IsNullOrWhiteSpace(i.Posting.DescriptionText)))
            item.Selected = value;

        if (ResultsList.ItemsSource is IEnumerable<DiscoveredJobItem> visible)
            foreach (var item in visible)
                item.Selected = value;

        ResultsList.ItemsSource = (ResultsList.ItemsSource as IEnumerable<DiscoveredJobItem>)?.ToList();
    }

    private async void BtnQueueSelected_Click(object sender, RoutedEventArgs e)
    {
        var targets = (ResultsList.ItemsSource as IEnumerable<DiscoveredJobItem>)?.Where(i => i.Selected).ToList()
            ?? new List<DiscoveredJobItem>();
        if (targets.Count == 0)
        {
            StatusText.Text = "Check the boxes on the results you want queued first.";
            StatusText.Foreground = Yellow;
            return;
        }

        var prior = await LoadAppLogCacheAsync();
        var enqueued = 0;
        var skipped = new List<string>();

        foreach (var item in targets)
        {
            var (ok, reason) = await _queue.EnqueueAsync(
                new QueuedApplication
                {
                    PostingKey = PostingKey.For(item.Posting!.Url, item.Posting.CompanyName, item.Posting.JobTitle),
                    JobUrl = item.Posting.Url ?? string.Empty,
                    JobTitle = item.Posting.JobTitle ?? string.Empty,
                    Company = item.Posting.CompanyName ?? string.Empty,
                    DescriptionText = item.Posting.DescriptionText,
                    GhostRiskScore = item.Snapshot?.RiskScore,
                    GhostRiskBand = item.Snapshot?.Band
                },
                duplicateCheck: q =>
                {
                    var (blocked, message) = DuplicateApplyCheck.Evaluate(
                        prior, q.JobUrl, q.Company, q.JobTitle, _settings.DuplicateApplyWindowDays, DateTime.UtcNow);
                    return Task.FromResult<string?>(blocked ? message : null);
                });

            if (ok) enqueued++; else skipped.Add($"{item.Company}: {reason}");
            item.Selected = false;
        }

        StatusText.Text = enqueued > 0
            ? $"Queued {enqueued}. " + (skipped.Count > 0 ? "Skipped: " + string.Join("; ", skipped) : "")
            : "Nothing queued. " + string.Join("; ", skipped);

        StatusText.Foreground = enqueued > 0 ? Cyan : Yellow;
        ResultsList.ItemsSource = targets.Cast<DiscoveredJobItem>().ToList();
    }

    public FindJobsView(JobDiscoveryService discovery, GhostScorer scorer, EnvoySettings settings,
        IJobEventRepository jobEvents, IApplicationLogRepository appLogs, ApplicationQueueService queue,
        IApplicationQueueRepository queueRepo)
    {
        _discovery = discovery;
        _scorer = scorer;
        _settings = settings;
        _jobEvents = jobEvents;
        _appLogs = appLogs;
        _queue = queue;
        _queueRepo = queueRepo;
        InitializeComponent();
        Loaded += FindJobsView_Loaded;
    }

    private void FindJobsView_Loaded(object sender, RoutedEventArgs e)
    {
        _queue.QueueChanged += QueueChanged;
        _ = RenderQueuePanelAsync();
        if (!string.IsNullOrEmpty(_settings.BraveSearchApiKeyEncrypted))
        {
            StatusText.Text = "Brave key saved. Leave the box blank to keep using it, or type a new one to replace it.";
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
        AddCompanyStatus.Text = $"Checking the six ATS platforms for \"{companyName}\"...";
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
                    AddCompanyStatus.Text = $"Found {companyName} on {board.Ats} (token: {board.Token}). Added to your boards.";
                    AddCompanyStatus.Foreground = Green;
                    TxtAddCompany.Clear();
                }
            }
            else
            {
                AddCompanyStatus.Text = $"No public job board found for \"{companyName}\". Try a different name or check the spelling.";
                AddCompanyStatus.Foreground = Red;
            }
        }
        catch (Exception ex)
        {
            AddCompanyStatus.Text = $"Error: {ex.Message}";
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
            StatusText.Text = "Brave key saved.";
            StatusText.Foreground = Green;
        }
        else
        {
            StatusText.Text = "Couldn't save. settings.json may be locked, so the key was not stored.";
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
            StatusText.Text = result.Errors.Count > 0 ? string.Join("  |  ", result.Errors) : "No matching jobs found.";
            StatusText.Foreground = result.Errors.Count > 0 ? Yellow : Gray;
        }
        else
        {
            var suffix = result.Errors.Count > 0 ? $" ({result.Errors.Count} source{(result.Errors.Count == 1 ? "" : "s")} unavailable)" : "";
            StatusText.Text = $"{items.Count} job{(items.Count == 1 ? "" : "s")} found{suffix}";
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
            ? string.Join("\n", score.TopEvidence.Select(ev => "- " + ev))
            : "";

        var meta = $"{job.Source} · {(job.PostedAtUtc?.ToString("yyyy-MM-dd") ?? "date n/a")}";
        if (!string.IsNullOrWhiteSpace(job.SalaryText))
            meta += $" · {job.SalaryText}";

        return new DiscoveredJobItem
        {
            Title = string.IsNullOrWhiteSpace(job.JobTitle) ? "Untitled posting" : job.JobTitle,
            Company = string.IsNullOrWhiteSpace(job.CompanyName) ? "Unknown company" : job.CompanyName,
            Location = string.IsNullOrWhiteSpace(job.Location) ? "Location not listed" : job.Location,
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

    private void BtnApply_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not DiscoveredJobItem item) return;

        if (item.Posting is null || string.IsNullOrWhiteSpace(item.Posting.DescriptionText))
        {
            StatusText.Text = $"No description came with this posting ({item.Company}) — open it, copy the link, and apply from the Apply tab.";
            StatusText.Foreground = Yellow;
            return;
        }

        RecordItemEvent(item, JobEventType.Viewed); // an apply intent is engagement; the submit itself will record Applied
        ApplyRequested?.Invoke(item);
    }

    // APP-07: queue the carried posting for background tailoring. The
    // duplicate guard runs at ENQUEUE time (completed applications), and the
    // queue itself refuses a second active row for the same posting.
    private async void BtnQueue_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not DiscoveredJobItem item) return;

        if (item.Posting is null || string.IsNullOrWhiteSpace(item.Posting.DescriptionText))
        {
            StatusText.Text = $"No description came with this posting ({item.Company}) — open it, copy the link, and apply from the Apply tab.";
            StatusText.Foreground = Yellow;
            return;
        }

        var (enqueued, reason) = await _queue.EnqueueAsync(
            new QueuedApplication
            {
                PostingKey = PostingKey.For(item.Posting.Url, item.Posting.CompanyName, item.Posting.JobTitle),
                JobUrl = item.Posting.Url ?? string.Empty,
                JobTitle = item.Posting.JobTitle ?? string.Empty,
                Company = item.Posting.CompanyName ?? string.Empty,
                DescriptionText = item.Posting.DescriptionText,
                GhostRiskScore = item.Snapshot?.RiskScore,
                GhostRiskBand = item.Snapshot?.Band
            },
            duplicateCheck: async q =>
            {
                var prior = await LoadAppLogCacheAsync();
                var (blocked, message) = DuplicateApplyCheck.Evaluate(
                    prior,
                    q.JobUrl, q.Company, q.JobTitle, _settings.DuplicateApplyWindowDays, DateTime.UtcNow);
                return (string?)(blocked ? message : null);
            });

        StatusText.Text = enqueued
            ? $"Queued {item.Company} — tailoring runs in the background; watch YOUR QUEUE below."
            : reason;
        StatusText.Foreground = enqueued ? Cyan : Yellow;
    }

    // A tiny cache to avoid re-reading the log table on every queue click in a
    // rapid burst; refreshed per click anyway (cheap read, correctness first).
    private List<ApplicationLog>? appLogCache;

    private async Task<List<ApplicationLog>> LoadAppLogCacheAsync()
    {
        appLogCache = await _appLogs.GetAllWithoutScreenshotsAsync();
        return appLogCache;
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
            ? $"Ghost dodged: {item.Company} ({item.RiskText}). It's on your scoreboard."
            : $"Skipped {item.Company}.";
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

    // ── APP-07: the queue panel ──────────────────────────────────────────

    private void QueueChanged() =>
        Dispatcher.BeginInvoke(new Action(async () => await RenderQueuePanelAsync()));

    public sealed record QueueRow(string Label, string Meta, Brush LabelBrush, Visibility ApplyVisibility, QueuedApplication Item);

    private (string Label, Brush Brush) Describe(QueueItemStatus status)
    {
        return status switch
        {
            QueueItemStatus.Queued => ("QUEUED", GrayBrush),
            QueueItemStatus.Preparing => ("TAILORING…", Cyan),
            QueueItemStatus.AppReady => ("READY TO FILL", Green),
            QueueItemStatus.Submitted => ("SUBMITTED", Green),
            QueueItemStatus.NeedsUserSend => ("NEEDS YOUR SEND", Green),
            QueueItemStatus.Failed => ("FAILED", Red),
            QueueItemStatus.BlockedDuplicate => ("ALREADY APPLIED", Yellow),
            QueueItemStatus.NeedsCaptcha => ("CAPTCHA", Yellow),
            QueueItemStatus.Skipped => ("REMOVED", GrayBrush),
            _ => (status.ToString().ToUpperInvariant(), GrayBrush)
        };
    }

    // (kept null-checked: the StatusTo worker bytes can legally be unknown)
    private static readonly Brush GrayBrush = new SolidColorBrush(Color.FromRgb(0x88, 0x92, 0xA4));

    private async Task RenderQueuePanelAsync()
    {
        var items = await _queueRepo.GetAllAsync();
        var rows = new List<QueueRow>();

        foreach (var item in items
            .OrderBy(i => i.Status == QueueItemStatus.Preparing ? 0 : 1)
            .ThenBy(i => i.CreatedAt)
            .Take(15))
        {
            var (statusLabel, brush) = item.Status switch
            {
                QueueItemStatus.Queued => ("QUEUED", Gray),
                QueueItemStatus.Preparing => ("TAILORING…", Cyan),
                QueueItemStatus.AppReady => ("READY TO FILL", Green),
                QueueItemStatus.Submitted => ("SUBMITTED", Green),
                QueueItemStatus.Failed => ("FAILED", Red),
                QueueItemStatus.BlockedDuplicate => ("ALREADY APPLIED", Yellow),
                QueueItemStatus.NeedsCaptcha => ("CAPTCHA", Yellow),
                QueueItemStatus.Skipped => ("REMOVED", Gray),
                _ => (item.Status.ToString().ToUpperInvariant(), Gray)
            };

            var note = string.IsNullOrWhiteSpace(item.Note) ? "" : $" — {item.Note}";
            rows.Add(new QueueRow(
                statusLabel,
                $"{item.Company} · {item.JobTitle}{note}",
                brush,
                item.Status == QueueItemStatus.AppReady && item.TailoredProfileId is not null
                    ? Visibility.Visible : Visibility.Collapsed,
                item));
        }

        QueueList.ItemsSource = rows;
        QueuePanel.Visibility = rows.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void BtnQueueFill_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not QueueRow row) return;
        if (row.Item.Status != QueueItemStatus.AppReady || row.Item.TailoredProfileId is null) return;

        PreparedApplyRequested?.Invoke(row.Item);
    }


    private void ShowError(string message)
    {
        StatusText.Text = message;
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

    /// <summary>APP-11 per-row batch selector; survives re-sorts within the view.</summary>
    public bool Selected { get; set; }

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