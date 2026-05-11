using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace PrApprover.Services;

public sealed class GitHubUser
{
    [JsonPropertyName("login")] public string Login { get; set; } = "";
    [JsonPropertyName("avatar_url")] public string AvatarUrl { get; set; } = "";
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

    private static async Task EnsureOk(HttpResponseMessage res)
    {
        if (res.IsSuccessStatusCode) return;
        var body = await res.Content.ReadAsStringAsync();
        throw new HttpRequestException($"{(int)res.StatusCode} {res.ReasonPhrase}: {Truncate(body, 300)}");
    }

    private static string Truncate(string s, int max) =>
        string.IsNullOrEmpty(s) ? "" : s.Length <= max ? s : s[..max] + "...";
}
