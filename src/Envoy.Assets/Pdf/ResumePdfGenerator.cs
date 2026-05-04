using Envoy.Core.Models;
using Envoy.Core.Services;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Envoy.Assets.Pdf;

public class ResumePdfGenerator : IResumePdfGenerator
{
    static ResumePdfGenerator()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public byte[] Generate(TailoredProfile profile)
    {
        var data = profile.TailoredData;
        var document = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.Letter);
                page.Margin(0.4f, Unit.Inch);
                page.DefaultTextStyle(x => x.FontSize(9.5f).FontFamily("Arial"));
                page.Content().Element(content => BuildContent(content, data));
            });
        });

        return document.GeneratePdf();
    }

    private void BuildHeader(IContainer container, MasterProfile profile)
    {
        container.Column(column =>
        {
            column.Item().AlignCenter().Text(profile.Name)
                .FontSize(20).Bold();

            column.Item().AlignCenter().Text(text =>
            {
                text.Span($"{profile.Location ?? ""} | {profile.Email} | {profile.Phone}").FontSize(9);
                if (!string.IsNullOrEmpty(profile.LinkedIn))
                    text.Span($" | {profile.LinkedIn}").FontSize(9);
                if (!string.IsNullOrEmpty(profile.Website))
                    text.Span($" | {profile.Website}").FontSize(9);
            });

            column.Item().PaddingVertical(5).LineHorizontal(1).LineColor(Colors.Grey.Medium);
        });
    }

    private void BuildContent(IContainer container, MasterProfile profile)
    {
        container.Column(column =>
        {
            if (!string.IsNullOrWhiteSpace(profile.Summary))
            {
                column.Item().Text("SUMMARY").Bold().FontSize(11);
                column.Item().Text(profile.Summary).FontSize(9);
                column.Item().PaddingVertical(3);
            }

            if (profile.Skills.Any())
            {
                column.Item().Text("SKILLS").Bold().FontSize(11);
                column.Item().Text(string.Join(" | ", profile.Skills)).FontSize(9);
                column.Item().PaddingVertical(3);
            }

            if (profile.Experience.Any())
            {
                column.Item().Text("EXPERIENCE").Bold().FontSize(11);
                foreach (var exp in profile.Experience)
                {
                    column.Item().PaddingVertical(2).Column(expCol =>
                    {
                        expCol.Item().Row(row =>
                        {
                            row.RelativeItem().Text(exp.JobTitle).Bold();
                            row.ConstantItem(100).AlignRight().Text(text =>
                            {
                                text.Span($"{exp.StartDate} - {exp.EndDate ?? "Present"}").FontSize(9);
                            });
                        });
                        expCol.Item().Text($"{exp.Company}{(!string.IsNullOrEmpty(exp.Location) ? $", {exp.Location}" : "")}").Italic().FontSize(9);
                        foreach (var bullet in exp.Bullets)
                        {
                            expCol.Item().Text($"  \u2022 {bullet}").FontSize(9);
                        }
                    });
                }
                column.Item().PaddingVertical(3);
            }

            if (profile.Education.Any())
            {
                column.Item().Text("EDUCATION").Bold().FontSize(11);
                foreach (var edu in profile.Education)
                {
                    column.Item().Row(row =>
                    {
                        row.RelativeItem().Text(edu.Degree).Bold();
                        if (!string.IsNullOrEmpty(edu.GraduationDate))
                            row.ConstantItem(80).AlignRight().Text(text =>
                            {
                                text.Span(edu.GraduationDate).FontSize(9);
                            });
                    });
                    column.Item().Text($"{edu.Institution}{(!string.IsNullOrEmpty(edu.Location) ? $", {edu.Location}" : "")}").FontSize(9);
                }
                column.Item().PaddingVertical(3);
            }

            if (profile.Projects.Any())
            {
                column.Item().Text("PROJECTS").Bold().FontSize(11);
                foreach (var proj in profile.Projects)
                {
                    column.Item().Text(proj.Name).Bold().FontSize(9);
                    if (!string.IsNullOrEmpty(proj.Description))
                        column.Item().Text(proj.Description).FontSize(9);
                    if (proj.Technologies.Any())
                        column.Item().Text($"Technologies: {string.Join(", ", proj.Technologies)}").FontSize(9);
                }
            }
        });
    }
}
