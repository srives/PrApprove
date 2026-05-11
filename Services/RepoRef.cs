using System.Text.RegularExpressions;

namespace PrApprover.Services;

public sealed class RepoRef
{
    public string Owner { get; }
    public string Name { get; }

    public RepoRef(string owner, string name)
    {
        Owner = owner;
        Name = name;
    }

    private static readonly Regex PrUrlRegex = new(
        @"^https?://github\.com/(?<owner>[^/\s]+)/(?<repo>[^/\s]+)/pull/(?<num>\d+)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex RepoUrlRegex = new(
        @"^https?://github\.com/(?<owner>[^/\s]+)/(?<repo>[^/\s\.]+)(?:\.git)?/?$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex BareRegex = new(
        @"^(?<owner>[^/\s]+)/(?<repo>[^/\s]+)$",
        RegexOptions.Compiled);

    public static bool TryParseRepo(string? input, out RepoRef? result)
    {
        result = null;
        if (string.IsNullOrWhiteSpace(input)) return false;
        input = input.Trim();

        var m = RepoUrlRegex.Match(input);
        if (m.Success)
        {
            result = new RepoRef(m.Groups["owner"].Value, StripGit(m.Groups["repo"].Value));
            return true;
        }

        m = BareRegex.Match(input);
        if (m.Success)
        {
            result = new RepoRef(m.Groups["owner"].Value, StripGit(m.Groups["repo"].Value));
            return true;
        }

        return false;
    }

    public static bool TryParsePrUrl(string? input, out RepoRef? repo, out int number)
    {
        repo = null;
        number = 0;
        if (string.IsNullOrWhiteSpace(input)) return false;
        var m = PrUrlRegex.Match(input.Trim());
        if (!m.Success) return false;
        repo = new RepoRef(m.Groups["owner"].Value, StripGit(m.Groups["repo"].Value));
        number = int.Parse(m.Groups["num"].Value);
        return true;
    }

    private static string StripGit(string repo) =>
        repo.EndsWith(".git", StringComparison.OrdinalIgnoreCase) ? repo[..^4] : repo;
}
