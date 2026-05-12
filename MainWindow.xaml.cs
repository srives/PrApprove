using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using PrApprover.Services;

namespace PrApprover;

public partial class MainWindow : Window
{
    private static readonly HttpClient _http = new();
    private static readonly Regex WorkItemRegex = new(@"\bWAT\s*-?\s*\d+\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private readonly ConfigStore _store = new();
    private readonly DispatcherTimer _prDebounce;

    public MainWindow()
    {
        InitializeComponent();
        Loaded += MainWindow_Loaded;

        _prDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _prDebounce.Tick += async (_, _) =>
        {
            _prDebounce.Stop();
            await RefreshPrInfoAsync();
        };

        ApiKeyBox.LostFocus += async (_, _) => await RefreshIdentityAsync();
        RepoUrlBox.LostFocus += async (_, _) => { SaveConfig(); await RefreshPrInfoAsync(); };
        PrInputBox.TextChanged += (_, _) =>
        {
            _prDebounce.Stop();
            _prDebounce.Start();
        };
        PrInputBox.KeyDown += async (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                _prDebounce.Stop();
                await RefreshPrInfoAsync();
            }
        };
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        var cfg = _store.Load();
        ApiKeyBox.Text = cfg.ApiKey ?? string.Empty;
        RepoUrlBox.Text = cfg.RepoUrl ?? string.Empty;

        if (!string.IsNullOrWhiteSpace(ApiKeyBox.Text))
        {
            await RefreshIdentityAsync();
        }
    }

    private void GetTokenButton_Click(object sender, RoutedEventArgs e)
    {
        var instructions =
            "GitHub will open in your browser. Follow these steps:\n\n" +
            "1. Token name: anything (e.g. \"PR Approver\")\n" +
            "2. Expiration: pick what you want (max 1 year)\n" +
            "3. Repository access: select \"Only select repositories\"\n" +
            "   then pick the ONE repo you entered above.\n" +
            "4. Repository permissions:\n" +
            "       Pull requests  -->  Read and write\n" +
            "   Leave everything else as \"No access\".\n" +
            "5. Click \"Generate token\" at the bottom.\n" +
            "6. Copy the token (starts with github_pat_...) and paste\n" +
            "   it into the API Key box in this app.\n\n" +
            "Open GitHub now?";

        var result = MessageBox.Show(this, instructions, "Get a GitHub Token",
            MessageBoxButton.OKCancel, MessageBoxImage.Information);

        if (result != MessageBoxResult.OK) return;

        var psi = new ProcessStartInfo("https://github.com/settings/personal-access-tokens/new")
        {
            UseShellExecute = true
        };
        Process.Start(psi);
        SetStatus("GitHub opened. Paste the new token into the API Key box when done.", isError: false);
    }

    private async void ApproveButton_Click(object sender, RoutedEventArgs e)
    {
        SaveConfig();

        var token = ApiKeyBox.Text.Trim();
        if (string.IsNullOrEmpty(token))
        {
            SetStatus("Enter an API key.", isError: true);
            return;
        }

        if (!TryResolvePr(out var owner, out var name, out var number, out var error))
        {
            SetStatus(error!, isError: true);
            return;
        }

        ApproveButton.IsEnabled = false;
        SetStatus($"Approving PR #{number} in {owner}/{name}...", isError: false);
        try
        {
            var client = new GitHubClient(_http, token);
            await client.ApprovePullRequestAsync(owner, name, number);
            SetStatus($"Approved PR #{number} in {owner}/{name}.", isError: false, success: true);
            await RefreshPrInfoAsync();
        }
        catch (Exception ex)
        {
            SetStatus($"Failed: {ex.Message}", isError: true);
        }
        finally
        {
            ApproveButton.IsEnabled = true;
        }
    }

    private bool TryResolvePr(out string owner, out string name, out int number, out string? error)
    {
        owner = ""; name = ""; number = 0; error = null;
        var prInput = PrInputBox.Text.Trim();
        if (string.IsNullOrEmpty(prInput))
        {
            error = "Enter a PR URL or number.";
            return false;
        }

        if (RepoRef.TryParsePrUrl(prInput, out var prRepo, out number))
        {
            owner = prRepo!.Owner; name = prRepo.Name;
            return true;
        }

        if (int.TryParse(prInput, out number))
        {
            if (!RepoRef.TryParseRepo(RepoUrlBox.Text.Trim(), out var repo))
            {
                error = "Repo URL is missing or invalid.";
                return false;
            }
            owner = repo!.Owner; name = repo.Name;
            return true;
        }

        error = "PR input must be a full PR URL or a number.";
        return false;
    }

    private async Task RefreshPrInfoAsync()
    {
        var token = ApiKeyBox.Text.Trim();
        if (string.IsNullOrEmpty(token))
        {
            PrInfoText.Text = "";
            BranchText.Inlines.Clear();
            return;
        }
        if (string.IsNullOrWhiteSpace(PrInputBox.Text))
        {
            PrInfoText.Text = "";
            BranchText.Inlines.Clear();
            return;
        }
        if (!TryResolvePr(out var owner, out var name, out var number, out _))
        {
            PrInfoText.Text = "";
            BranchText.Inlines.Clear();
            return;
        }

        PrInfoText.Text = $"Loading PR #{number}...";
        BranchText.Inlines.Clear();
        PrInfoText.Foreground = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88));
        try
        {
            var client = new GitHubClient(_http, token);
            var info = await client.GetPullRequestInfoAsync(owner, name, number);
            PrInfoText.Text = $"PR #{number}: {FormatPrInfo(info)}";
            SetBranchText(info.HeadRef);
            PrInfoText.Foreground = info.Merged
                ? new SolidColorBrush(Color.FromRgb(0x8A, 0x4F, 0xBE))
                : info.MergeStateStatus == "CLEAN" && info.ReviewDecision == "APPROVED"
                    ? new SolidColorBrush(Color.FromRgb(0x1A, 0x7F, 0x37))
                    : new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55));
        }
        catch (Exception ex)
        {
            PrInfoText.Text = $"PR #{number}: {ex.Message}";
            BranchText.Inlines.Clear();
            PrInfoText.Foreground = new SolidColorBrush(Color.FromRgb(0xCF, 0x22, 0x2E));
        }
    }

    private void SetBranchText(string branch)
    {
        BranchText.Inlines.Clear();
        if (string.IsNullOrWhiteSpace(branch)) return;

        BranchText.Inlines.Add(new Run("Branch: ") { Foreground = new SolidColorBrush(Color.FromRgb(0x66, 0x66, 0x66)) });

        var last = 0;
        foreach (Match match in WorkItemRegex.Matches(branch))
        {
            if (match.Index > last)
                BranchText.Inlines.Add(new Run(branch[last..match.Index]));

            BranchText.Inlines.Add(new Run(match.Value)
            {
                Foreground = new SolidColorBrush(Color.FromRgb(0xB0, 0x31, 0x7D)),
                FontWeight = FontWeights.SemiBold
            });
            last = match.Index + match.Length;
        }

        if (last < branch.Length)
            BranchText.Inlines.Add(new Run(branch[last..]));
    }

    private static string FormatPrInfo(PullRequestInfo info)
    {
        var author = string.IsNullOrEmpty(info.Author) ? "" : $"by @{info.Author}";
        var approvals = info.ApprovalCount == 1 ? "1 approval" : $"{info.ApprovalCount} approvals";

        if (info.Merged)
        {
            var date = info.MergedAt?.ToLocalTime().ToString("yyyy-MM-dd") ?? "";
            var into = string.IsNullOrEmpty(info.BaseRef) ? "" : $" into {info.BaseRef}";
            var head = string.IsNullOrEmpty(date) ? $"Merged{into}" : $"Merged {date}{into}";
            return JoinNonEmpty(author, head, approvals);
        }

        if (string.Equals(info.State, "CLOSED", StringComparison.OrdinalIgnoreCase))
            return JoinNonEmpty(author, "Closed (not merged)", approvals);

        string? decision = info.ReviewDecision switch
        {
            "CHANGES_REQUESTED" => "Changes requested",
            "REVIEW_REQUIRED" => "Review required",
            _ => null
        };

        string? mergeStatus = info.IsDraft ? "Draft" : info.MergeStateStatus switch
        {
            "CLEAN" => "Ready to merge",
            "DIRTY" => "Conflicts",
            "BLOCKED" => "Blocked",
            "BEHIND" => "Behind base",
            "UNSTABLE" => "Checks failing",
            "HAS_HOOKS" => "Awaiting hooks",
            "UNKNOWN" => "Mergeability pending",
            _ => null
        };

        var comments = info.UnresolvedThreads == 1 ? "1 open comment" : $"{info.UnresolvedThreads} open comments";

        return JoinNonEmpty(author, approvals, decision, mergeStatus, comments);
    }

    private static string JoinNonEmpty(params string?[] parts) =>
        string.Join(" · ", parts.Where(p => !string.IsNullOrEmpty(p)));

    private async Task RefreshIdentityAsync()
    {
        SaveConfig();
        var token = ApiKeyBox.Text.Trim();
        if (string.IsNullOrEmpty(token))
        {
            UserNameText.Text = "(not signed in)";
            AvatarImage.Source = null;
            return;
        }

        try
        {
            var client = new GitHubClient(_http, token);
            var user = await client.GetCurrentUserAsync();
            UserNameText.Text = $"@{user.Login}";

            if (!string.IsNullOrEmpty(user.AvatarUrl))
            {
                var bytes = await _http.GetByteArrayAsync(user.AvatarUrl);
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.StreamSource = new MemoryStream(bytes);
                bmp.EndInit();
                bmp.Freeze();
                AvatarImage.Source = bmp;
            }
            SetStatus("Token validated.", isError: false);
        }
        catch (Exception ex)
        {
            UserNameText.Text = "(token invalid)";
            AvatarImage.Source = null;
            SetStatus($"Token check failed: {ex.Message}", isError: true);
        }
    }

    private void SaveConfig()
    {
        _store.Save(new Config
        {
            ApiKey = ApiKeyBox.Text,
            RepoUrl = RepoUrlBox.Text
        });
    }

    private void SetStatus(string message, bool isError, bool success = false)
    {
        StatusText.Text = message;
        StatusText.Foreground = isError
            ? new SolidColorBrush(Color.FromRgb(0xCF, 0x22, 0x2E))
            : success
                ? new SolidColorBrush(Color.FromRgb(0x1A, 0x7F, 0x37))
                : new SolidColorBrush(Color.FromRgb(0x66, 0x66, 0x66));
    }
}
