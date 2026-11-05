using Envoy.Core.Models;
using Envoy.Core.Services;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using static Envoy.UI.Theme;

namespace Envoy.UI;

public partial class VaultView : UserControl
{
    private readonly IProfileRepository _profileRepo;
    private readonly IAnswerBankRepository _answers;
    private MasterProfile? _profile;

    private static readonly FontFamily BodyFont = new("pack://application:,,,/Fonts/#JetBrains Mono");

    public VaultView(IProfileRepository profileRepo, IAnswerBankRepository answers)
    {
        _profileRepo = profileRepo;
        _answers = answers;
        InitializeComponent();
        Loaded += VaultView_Loaded;
    }

    private async void VaultView_Loaded(object sender, RoutedEventArgs e) => await RenderAnswerBankAsync();

    public async Task SetProfileId(Guid profileId)
    {
        try
        {
            _profile = await _profileRepo.GetByIdAsync(profileId);
        }
        catch
        {
            _profile = null;
        }

        try
        {
            if (_profile == null)
            {
                NoProfileLabel.Visibility = Visibility.Visible;
                ProfilePanel.Visibility = Visibility.Collapsed;
                return;
            }

            NoProfileLabel.Visibility = Visibility.Collapsed;
            ProfilePanel.Visibility = Visibility.Visible;

            TxtName.Text = _profile.Name;
            TxtEmail.Text = _profile.Email;
            TxtPhone.Text = _profile.Phone;
            TxtLocation.Text = _profile.Location ?? "";
            TxtLinkedIn.Text = _profile.LinkedIn ?? "";
            TxtWebsite.Text = _profile.Website ?? "";
            TxtSummary.Text = _profile.Summary ?? "";
            TxtSkills.Text = string.Join(", ", _profile.Skills);

            ExperienceList.ItemsSource = _profile.Experience
                .Select(x => new VaultEntryRow(x.JobTitle + " at " + x.Company,
                    $"{x.StartDate} to {x.EndDate ?? (x.IsCurrent ? "present" : "?")}",
                    x.Bullets.Select(b => "  - " + b).ToList(),
                    EditKind.Experience, x)).ToList();

            EducationList.ItemsSource = _profile.Education
                .Select(e => new VaultEntryRow(e.Degree + " — " + e.Institution,
                    e.GraduationDate ?? string.Empty, new List<string>(), EditKind.Education, e)).ToList();

            ProjectsList.ItemsSource = _profile.Projects
                .Select(p => new VaultEntryRow(p.Name, string.Join(", ", p.Technologies),
                    (p.Description ?? string.Empty).Split('\n', StringSplitOptions.RemoveEmptyEntries)
                        .Select(l => "  " + l).Take(3).ToList(),
                    EditKind.Project, p)).ToList();

            AnomaliesText.Text = _profile.Anomalies.Any()
                ? string.Join("\n", _profile.Anomalies.Select(a => $"- {a.Field}: {a.Message} ({a.Severity})"))
                : "No issues found";
            AnomaliesText.Foreground = _profile.Anomalies.Any()
                ? Yellow
                : Green;

            SaveStatus.Text = "";
        }
        catch
        {
            NoProfileLabel.Visibility = Visibility.Visible;
            ProfilePanel.Visibility = Visibility.Collapsed;
        }
    }

    private sealed record VaultEntryRow(string Heading, string Meta, List<string> Lines, EditKind Kind, object Entry);

    private enum EditKind { Experience, Education, Project, Answer }

    private async void VaultItem_EditDelete_Click(object sender, RoutedEventArgs e)
    {
        if (_profile is null) return;
        if (sender is not Button btn || btn.Tag is not VaultEntryRow row) return;

        if (row.Kind == EditKind.Answer)
        {
            BtnAnswerBankCRUD_Click(sender, e);
            return;
        }

        try
        {
            bool editedSomething;

            if (btn.Content?.ToString() == "DELETE")
            {
                var confirm = MessageBox.Show(
                    "Remove this entry from your profile?",
                    "Envoy: remove " + row.Kind,
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question,
                    MessageBoxResult.No);
                if (confirm != MessageBoxResult.Yes) return;

                Remove(row.Entry);
                editedSomething = false;
            }
            else
            {
                editedSomething = row.Kind switch
                {
                    EditKind.Experience => ApplyExperience((ExperienceEntry)row.Entry, VaultEntryEditor.ShowExperience((ExperienceEntry)row.Entry)),
                    EditKind.Education => ApplyEducation((EducationEntry)row.Entry, VaultEntryEditor.ShowEducation((EducationEntry)row.Entry)),
                    EditKind.Project => ApplyProject((ProjectEntry)row.Entry, VaultEntryEditor.ShowProject((ProjectEntry)row.Entry)),
                    EditKind.Answer => false, // the answer bank routes through its own handler
                    _ => false
                };
                if (!editedSomething) return; // user cancelled
            }

            await PersistAsync();
        }
        catch (Exception ex)
        {
            SaveStatus.Text = $"Couldn't apply edit: {ex.Message}";
            SaveStatus.Foreground = Red;
        }
    }

    private void Remove(object entry)
    {
        if (entry is null) return;
        switch (entry)
        {
            case ExperienceEntry x: _profile!.Experience.Remove(x); break;
            case EducationEntry e2: _profile!.Education.Remove(e2); break;
            case ProjectEntry p: _profile!.Projects.Remove(p); break;
        }
    }

    private async Task<bool> BeginEntryEdit(VaultEntryRow row)
    {
        switch (row.Kind)
        {
            case EditKind.Experience:
                {
                    var entry = (ExperienceEntry)row.Entry;
                    var edited = VaultEntryEditor.ShowExperience(entry);
                    return edited is null ? false : (ApplyExperience(entry, edited), true).Item1;
                }
            case EditKind.Education:
                {
                    var entry = (EducationEntry)row.Entry;
                    var edited = VaultEntryEditor.ShowEducation(entry);
                    return edited is null ? false : (ApplyEducation(entry, edited), true).Item1;
                }
            case EditKind.Project:
                {
                    var entry = (ProjectEntry)row.Entry;
                    var edited = VaultEntryEditor.ShowProject(entry);
                    return edited is null ? false : (ApplyProject(entry, edited), true).Item1;
                }
            default:
                return false;
        }
    }

    private bool ApplyExperience(ExperienceEntry target, ExperienceEntry? edited)
    {
        if (edited is null) return false;
        target.JobTitle = edited.JobTitle;
        target.Company = edited.Company;
        target.Location = edited.Location;
        target.StartDate = edited.StartDate;
        target.EndDate = edited.IsCurrent ? null : edited.EndDate;
        target.IsCurrent = edited.IsCurrent;
        target.Bullets = edited.Bullets;
        return true;
    }

    private bool ApplyEducation(EducationEntry target, EducationEntry? edited)
    {
        if (edited is null) return false;
        target.Degree = edited.Degree;
        target.Institution = edited.Institution;
        target.GraduationDate = edited.GraduationDate;
        target.Location = edited.Location;
        return true;
    }

    private bool ApplyProject(ProjectEntry target, ProjectEntry? edited)
    {
        if (edited is null) return false;
        target.Name = edited.Name;
        target.Description = edited.Description;
        target.Technologies = edited.Technologies;
        return true;
    }

    private async void BtnAddExperience_Click(object sender, RoutedEventArgs e)
    {
        if (_profile is null) return;
        var entry = VaultEntryEditor.ShowExperience(new ExperienceEntry());
        if (entry is null) return;

        _profile.Experience.Add(entry);
        await PersistAsync(entry.JobTitle + " at " + entry.Company);
    }

    private async void BtnAddEducation_Click(object sender, RoutedEventArgs e)
    {
        if (_profile is null) return;
        var entry = VaultEntryEditor.ShowEducation(new EducationEntry());
        if (entry is null) return;

        _profile.Education.Add(entry);
        await PersistAsync((entry.Degree ?? "") + " — " + (entry.Institution ?? ""));
    }

    private async void BtnAddProject_Click(object sender, RoutedEventArgs e)
    {
        if (_profile is null) return;
        var entry = VaultEntryEditor.ShowProject(new ProjectEntry());
        if (entry is null) return;

        _profile.Projects.Add(entry);
        await PersistAsync(entry.Name);
    }

    private async Task PersistAsync(string? justAdded = null)
    {
        await _profileRepo.UpdateAsync(_profile!);
        SaveStatus.Text = string.IsNullOrWhiteSpace(justAdded)
            ? "Saved."
            : $"Saved: {justAdded}.";
        SaveStatus.Foreground = Green;
        await SetProfileId(_profile!.Id);
    }

    // ── APP-10: the answer bank (Q&A bank) UI ─────────────────────────────

    private async void BtnAddAnswer_Click(object sender, RoutedEventArgs e)
    {
        var (question, answer) = VaultEntryEditor.ShowAnswer(null, null);
        if (question is null || answer is null) return;

        await _answers.AddOrUpdateAsync(question, answer);
        SaveStatus.Text = $"Saved answer: {question}";
        SaveStatus.Foreground = Green;
        await RenderAnswerBankAsync();
    }

    private async void BtnAnswerBankCRUD_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not AnswerBankEntry entry) return;

        if (btn.Content?.ToString() == "DELETE")
        {
            var confirm = MessageBox.Show(
                "Remove this stored answer?",
                "Envoy: remove answer",
                MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No);
            if (confirm != MessageBoxResult.Yes) return;

            await _answers.RemoveAsync(entry.Id);
        }
        else
        {
            var (question, answer) = VaultEntryEditor.ShowAnswer(entry.Question, entry.Answer);
            if (question is null || answer is null) return;
            await _answers.AddOrUpdateAsync(question, answer);
        }

        SaveStatus.Text = "Answer bank updated.";
        SaveStatus.Foreground = Green;
        await RenderAnswerBankAsync();
    }

    private async Task RenderAnswerBankAsync()
    {
        var entries = await _answers.GetAllAsync();
        AnswerList.ItemsSource = entries
            .OrderBy(e => e.Question, StringComparer.OrdinalIgnoreCase)
            .Select(e => new VaultEntryRow(
                e.Question.Length > 90 ? e.Question[..90] + "…" : e.Question,
                (e.Answer.Length > 120 ? e.Answer[..120] + "…" : e.Answer),
                new List<string>(), EditKind.Answer, e))
            .ToList();
    }

    private async void BtnSave_Click(object sender, RoutedEventArgs e)
    {
        if (_profile == null) return;

        try
        {
            _profile.Name = TxtName.Text;
            _profile.Email = TxtEmail.Text;
            _profile.Phone = TxtPhone.Text;
            _profile.Location = TxtLocation.Text;
            _profile.LinkedIn = TxtLinkedIn.Text;
            _profile.Website = TxtWebsite.Text;
            _profile.Summary = TxtSummary.Text;
            _profile.Skills = TxtSkills.Text.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()).ToList();
            _profile.UpdatedAt = DateTime.UtcNow;

            await _profileRepo.UpdateAsync(_profile);
            SaveStatus.Text = "Saved.";
            SaveStatus.Foreground = Green;
        }
        catch (Exception ex)
        {
            SaveStatus.Text = $"Couldn't save: {ex.Message}";
            SaveStatus.Foreground = Red;
        }
    }

    private void BtnBack_Click(object sender, RoutedEventArgs e)
    {
        var mainWindow = (MainWindow)Window.GetWindow(this);
        mainWindow.NavigateToDashboard();
    }
}
