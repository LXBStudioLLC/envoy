using Envoy.Core.Models;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Envoy.UI;

/// <summary>
/// Code-built modal editor used by the Vault for Experience / Education /
/// Project entries (APP-06). A single purpose-built form keeps the
/// add/edit/delete surface small; Ok returns the edited entry, Cancel null.
/// </summary>
public sealed class VaultEntryEditor : Window
{
    private readonly Dictionary<string, TextBox> _text = new();

    private VaultEntryEditor(string title, params (string Label, string? Initial, bool Multiline, bool ForEntry)[] fields)
    {
        Title = title;
        Width = 460;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = new SolidColorBrush(Color.FromRgb(0x0A, 0x0E, 0x17));
        BorderBrush = new SolidColorBrush(Color.FromRgb(0x1A, 0x3A, 0x4A));
        BorderThickness = new Thickness(1);
        FontFamily = new FontFamily("pack://application:,,,/Fonts/#JetBrains Mono");
        Foreground = Brushes.Gainsboro;

        var panel = new StackPanel { Margin = new Thickness(16) };
        Content = panel;

        foreach (var (label, initial, multiline, forEntry) in fields)
        {
            panel.Children.Add(new TextBlock
            {
                Text = label,
                FontSize = 10,
                Foreground = new SolidColorBrush(Color.FromRgb(0x88, 0x92, 0xA4)),
                Margin = new Thickness(0, 8, 0, 2)
            });

            if (multiline)
            {
                var toBox = new TextBox { Height = 90, TextWrapping = TextWrapping.Wrap, AcceptsReturn = true, Text = initial };
                panel.Children.Add(toBox);
                _text[label] = toBox;
            }
            else
            {
                var textBox = new TextBox { Text = initial };
                panel.Children.Add(textBox);
                _text[label] = textBox;
            }
        }

        var okBtn = new Button { Content = "OK", Width = 100, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 14, 0, 0) };
        okBtn.Click += (_, _) => { DialogResult = true; Close(); };
        var cancelBtn = new Button { Content = "CANCEL", Width = 100, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(8, 14, 0, 0) };
        cancelBtn.Click += (_, _) => { DialogResult = false; Close(); };

        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        buttons.Children.Add(okBtn);
        buttons.Children.Add(cancelBtn);
        panel.Children.Add(buttons);

        Owner = System.Windows.Application.Current.Windows.Cast<Window>().FirstOrDefault(w => w is MainWindow);
    }


    private string Text(string label) => _text[label].Text.Trim();

    /// <summary>Trims but keeps user-provided empties (optional fields).</summary>
    private string TextOrProvided(string label) => _text[label].Text.Trim();

    private string TextOrProvidedEmpty(string label) => _text[label].Text.Trim();

    public static ExperienceEntry? ShowExperience(ExperienceEntry entry)
    {
        var dialog = new VaultEntryEditor(
            "Edit work history entry",
            ("JOB TITLE", entry.JobTitle, false, true),
            ("COMPANY", entry.Company, false, true),
            ("LOCATION", entry.Location ?? "", false, false),
            ("START (e.g. 2021-04)", entry.StartDate, false, true),
            ("END (empty when current)", entry.EndDate ?? "", false, false),
            ("BULLETS (one per line)", string.Join("\n", entry.Bullets), true, true));

        var body = (StackPanel)dialog.Content;
        var currentCheck = new CheckBox
        {
            Content = "Currently working here",
            Foreground = Brushes.Gainsboro,
            IsChecked = entry.IsCurrent,
            Margin = new Thickness(0, 2, 0, 0)
        };
        body.Children.Insert(body.Children.Count - 1, currentCheck);

        if (dialog.ShowDialog() != true)
            return null;

        entry.JobTitle = dialog.Text("JOB TITLE");
        entry.Company = dialog.Text("COMPANY");
        entry.Location = dialog.TextOrProvided("LOCATION");
        entry.StartDate = dialog.TextOrProvided("START (e.g. 2021-04)");
        entry.IsCurrent = currentCheck.IsChecked == true;

        // Keeping the model's shape honest: a current job has no end date.
        entry.EndDate = entry.IsCurrent
            ? null
            : dialog.TextOrProvidedEmpty("END (empty when current)");

        entry.Bullets = dialog.Text("BULLETS (one per line)")
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(b => b.Length > 0)
            .ToList();

        return entry;
    }

    public static EducationEntry? ShowEducation(EducationEntry entry)
    {
        var dialog = new VaultEntryEditor(
            "Edit education entry",
            ("DEGREE", entry.Degree, false, true),
            ("INSTITUTION", entry.Institution, false, true),
            ("GRADUATED (e.g. 2020-05)", entry.GraduationDate, false, false),
            ("LOCATION", entry.Location, false, false));

        if (dialog.ShowDialog() != true)
            return null;

        entry.Degree = dialog.Text("DEGREE");
        entry.Institution = dialog.Text("INSTITUTION");
        entry.GraduationDate = dialog.TextOrProvided("GRADUATED (e.g. 2020-05)");
        entry.Location = dialog.TextOrProvided("LOCATION");
        return entry;
    }

    public static ProjectEntry? ShowProject(ProjectEntry entry)
    {
        var dialog = new VaultEntryEditor(
            "Edit project entry",
            ("NAME", entry.Name, false, true),
            
            ("TECHNOLOGIES (comma-separated)", string.Join(", ", entry.Technologies), false, true),
            ("DESCRIPTION", entry.Description, true, true));

        if (dialog.ShowDialog() != true)
            return null;

        entry.Name = dialog.TextOrProvided("NAME");
        entry.Technologies = dialog.Text("TECHNOLOGIES (comma-separated)")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        entry.Description = dialog.Text("DESCRIPTION");
        return entry;
    }

    /// <summary>Adds/edits a stored answer (APP-10); question null → cancelled.</summary>
    public static (string? Question, string? Answer) ShowAnswer(string? initialQuestion, string? initialAnswer)
    {
        var dialog = new VaultEntryEditor(
            "Stored answer",
            ("QUESTION", initialQuestion ?? "", false, true),
            ("ANSWER", initialAnswer ?? "", true, true));

        if (dialog.ShowDialog() != true)
            return (null, null);

        var q = dialog.TextOrProvided("QUESTION");
        var a = dialog.TextOrProvided("ANSWER");
        if (q.Length < 3 || a.Length < 1)
        {
            MessageBox.Show("Both a question and an answer are required.", "Envoy", MessageBoxButton.OK, MessageBoxImage.Warning);
            return (null, null);
        }

        return (q, a);
    }
}
