namespace RepoIndexer;

internal sealed class ParsedReviewSummary
{
    public ParsedReviewSummary(string path,
                               DateTimeOffset date,
                               IEnumerable<ParsedReviewItem> items)
    {
        Path = path;
        Date = date;
        Items = items.ToArray();
    }

    public string Path { get; }

    public DateTimeOffset Date { get; }

    public IReadOnlyList<ParsedReviewItem> Items { get; }
}
