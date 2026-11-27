using System.Text.RegularExpressions;

namespace WorkerCSharp;

internal sealed record ReportSection(string Heading, int Words, int Bullets);

internal sealed record Report(string Title, IReadOnlyList<ReportSection> Sections, IReadOnlyList<string> ActionItems, IReadOnlyList<string> Highlights, int WordCount)
{
    public int ReadingMinutes => Math.Max(1, (int)Math.Ceiling(WordCount / 200.0));

    public TaskResult ToResult() => new(
        Headline: $"{Title} ({Sections.Count} sections, {ActionItems.Count} action items)",
        Facts:
        [
            new("Sections", Sections.Count.ToString()),
            new("Words", WordCount.ToString("N0")),
            new("Reading time", $"{ReadingMinutes} min"),
            new("Action items", ActionItems.Count.ToString())
        ],
        Details: new { Title, Sections, ActionItems, Highlights, WordCount, ReadingMinutes });
}

// Report generation: the C# worker's job. Turns Markdown meeting notes ("# Title", "## Section",
// "- bullet", "- [ ] action item") into a structured report.
internal static partial class ReportGenerator
{
    public const int MinimumWords = 25;

    public static Report Generate(string markdown)
    {
        var lines = markdown.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var words = Word().Count(markdown);
        if (words < MinimumWords)
        {
            throw new InvalidDataException($"The notes have {words} words; a report needs at least {MinimumWords}.");
        }

        var title = lines.FirstOrDefault(line => line.StartsWith("# ", StringComparison.Ordinal))?[2..].Trim()
            ?? lines[0].TrimStart('#', ' ');

        var sections = new List<ReportSection>();
        var actionItems = new List<string>();
        var highlights = new List<string>();
        string? heading = null;
        int sectionWords = 0, sectionBullets = 0;

        void CloseSection()
        {
            if (heading is not null)
            {
                sections.Add(new ReportSection(heading, sectionWords, sectionBullets));
            }
        }

        foreach (var line in lines)
        {
            if (line.StartsWith("## ", StringComparison.Ordinal))
            {
                CloseSection();
                (heading, sectionWords, sectionBullets) = (line[3..].Trim(), 0, 0);
                continue;
            }

            if (line.StartsWith("# ", StringComparison.Ordinal))
            {
                continue;
            }

            sectionWords += Word().Count(line);
            if (ActionItem().Match(line) is { Success: true } action)
            {
                actionItems.Add(action.Groups["text"].Value.Trim());
                sectionBullets++;
            }
            else if (Bullet().Match(line) is { Success: true } bullet)
            {
                sectionBullets++;
                if (Figure().IsMatch(bullet.Groups["text"].Value) && highlights.Count < 3)
                {
                    highlights.Add(bullet.Groups["text"].Value.Trim());
                }
            }
        }

        CloseSection();
        return new Report(title, sections, actionItems, highlights, words);
    }

    [GeneratedRegex(@"[\p{L}\p{N}][\p{L}\p{N}'’.,%$-]*")]
    private static partial Regex Word();

    [GeneratedRegex(@"^[-*]\s+\[[ xX]\]\s+(?<text>.+)$")]
    private static partial Regex ActionItem();

    [GeneratedRegex(@"^[-*]\s+(?<text>.+)$")]
    private static partial Regex Bullet();

    // Bullets with a percentage, a dollar amount or a number of hours make good highlights.
    [GeneratedRegex(@"\d+(?:\.\d+)?\s?%|\$\s?\d|\d+(?:\.\d+)?\s?h\b")]
    private static partial Regex Figure();
}
