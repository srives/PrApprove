using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PrApprover.Services;

public sealed class GitHubUser
{
    [JsonPropertyName("login")] public string Login { get; set; } = "";
    [JsonPropertyName("avatar_url")] public string AvatarUrl { get; set; } = "";
}

public sealed class PullRequestInfo
{
    public string State { get; set; } = "";
    public bool Merged { get; set; }
    public DateTime? MergedAt { get; set; }
    public bool IsDraft { get; set; }
    public string Mergeable { get; set; } = "";
    public string MergeStateStatus { get; set; } = "";
    public string? ReviewDecision { get; set; }
    public int UnresolvedThreads { get; set; }
    public string BaseRef { get; set; } = "";
    public string Author { get; set; } = "";
    public int ApprovalCount { get; set; }
}

public sealed class GitHubClient
{
    private readonly HttpClient _http;
    private readonly string _token;

    public GitHubClient(HttpClient http, string token)
    {
        _http = http;
        _token = token;
    }

    private HttpRequestMessage NewRequest(HttpMethod method, string url)
    {
        var req = new HttpRequestMessage(method, url);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token);
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        req.Headers.UserAgent.ParseAdd("PrApprover/1.0");
        req.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
        return req;
    }

    public async Task<GitHubUser> GetCurrentUserAsync()
    {
        using var req = NewRequest(HttpMethod.Get, "https://api.github.com/user");
        using var res = await _http.SendAsync(req);
        await EnsureOk(res);
        return await res.Content.ReadFromJsonAsync<GitHubUser>() ?? new GitHubUser();
    }

    public async Task ApprovePullRequestAsync(string owner, string repo, int number)
    {
        var url = $"https://api.github.com/repos/{owner}/{repo}/pulls/{number}/reviews";
        using var req = NewRequest(HttpMethod.Post, url);
        req.Content = JsonContent.Create(new { @event = "APPROVE", body = "Approved" });
        using var res = await _http.SendAsync(req);
        await EnsureOk(res);
    }

    private const string PrInfoQuery = @"
query Q($owner: String!, $name: String!, $number: Int!) {
  repository(owner: $owner, name: $name) {
    pullRequest(number: $number) {
      state
      merged
      mergedAt
      isDraft
      mergeable
      mergeStateStatus
      reviewDecision
      baseRefName
      author { login }
      latestOpinionatedReviews(first: 100) {
        nodes { state }
      }
      reviewThreads(first: 100) {
        nodes { isResolved }
      }
    }
  }
}";

    public async Task<PullRequestInfo> GetPullRequestInfoAsync(string owner, string repo, int number)
    {
        using var req = NewRequest(HttpMethod.Post, "https://api.github.com/graphql");
        req.Content = JsonContent.Create(new
        {
            query = PrInfoQuery,
            variables = new { owner, name = repo, number }
        });
        using var res = await _http.SendAsync(req);
        await EnsureOk(res);

        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
        var root = doc.RootElement;

        if (root.TryGetProperty("errors", out var errors) && errors.ValueKind == JsonValueKind.Array && errors.GetArrayLength() > 0)
        {
            var first = errors[0];
            var msg = first.TryGetProperty("message", out var m) ? m.GetString() : "GraphQL error";
            throw new HttpRequestException($"GraphQL: {msg}");
        }

        var pr = root.GetProperty("data").GetProperty("repository").GetProperty("pullRequest");
        if (pr.ValueKind == JsonValueKind.Null)
            throw new HttpRequestException("PR not found.");

        var info = new PullRequestInfo
        {
            State = GetStr(pr, "state"),
            Merged = pr.GetProperty("merged").GetBoolean(),
            IsDraft = pr.GetProperty("isDraft").GetBoolean(),
            Mergeable = GetStr(pr, "mergeable"),
            MergeStateStatus = GetStr(pr, "mergeStateStatus"),
            ReviewDecision = pr.TryGetProperty("reviewDecision", out var rd) && rd.ValueKind == JsonValueKind.String ? rd.GetString() : null,
            BaseRef = GetStr(pr, "baseRefName"),
        };

        if (pr.TryGetProperty("mergedAt", out var ma) && ma.ValueKind == JsonValueKind.String)
            info.MergedAt = ma.GetDateTime();

        if (pr.TryGetProperty("reviewThreads", out var rt) && rt.TryGetProperty("nodes", out var nodes))
        {
            int unresolved = 0;
            foreach (var n in nodes.EnumerateArray())
            {
                if (n.TryGetProperty("isResolved", out var r) && !r.GetBoolean()) unresolved++;
            }
            info.UnresolvedThreads = unresolved;
        }

        if (pr.TryGetProperty("author", out var auth) && auth.ValueKind == JsonValueKind.Object
            && auth.TryGetProperty("login", out var login) && login.ValueKind == JsonValueKind.String)
        {
            info.Author = login.GetString() ?? "";
        }

        if (pr.TryGetProperty("latestOpinionatedReviews", out var lor)
            && lor.TryGetProperty("nodes", out var reviewNodes))
        {
            int approvals = 0;
            foreach (var rev in reviewNodes.EnumerateArray())
            {
                if (rev.TryGetProperty("state", out var st)
                    && st.ValueKind == JsonValueKind.String
                    && st.GetString() == "APPROVED")
                {
                    approvals++;
                }
            }
            info.ApprovalCount = approvals;
        }

        return info;
    }

    private static string GetStr(JsonElement el, string prop) =>
        el.TryGetProperty(prop, out var p) && p.ValueKind == JsonValueKind.String ? (p.GetString() ?? "") : "";

    private static async Task EnsureOk(HttpResponseMessage res)
    {
        if (res.IsSuccessStatusCode) return;
        var body = await res.Content.ReadAsStringAsync();
        throw new HttpRequestException($"{(int)res.StatusCode} {res.ReasonPhrase}: {Truncate(body, 300)}");
    }

    private static string Truncate(string s, int max) =>
        string.IsNullOrEmpty(s) ? "" : s.Length <= max ? s : s[..max] + "...";
}
