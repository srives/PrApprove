using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PrApprover.Services;

namespace PrApprover;

public partial class MainWindow : Window
{
    private static readonly HttpClient _http = new();
    private readonly ConfigStore _store = new();

    public MainWindow()
    {
        InitializeComponent();
        Loaded += MainWindow_Loaded;
        ApiKeyBox.LostFocus += async (_, _) => await RefreshIdentityAsync();
        RepoUrlBox.LostFocus += (_, _) => SaveConfig();
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

        var prInput = PrInputBox.Text.Trim();
        if (string.IsNullOrEmpty(prInput))
        {
            SetStatus("Enter a PR URL or number.", isError: true);
            return;
        }

        // Resolve owner/repo/number. PR URL wins; otherwise use Repo URL + number.
        RepoRef? repo;
        int number;
        if (RepoRef.TryParsePrUrl(prInput, out var prRepo, out number))
        {
            repo = prRepo;
        }
        else if (int.TryParse(prInput, out number))
        {
            if (!RepoRef.TryParseRepo(RepoUrlBox.Text.Trim(), out repo))
            {
                SetStatus("Repo URL is missing or invalid.", isError: true);
                return;
            }
        }
        else
        {
            SetStatus("PR input must be a full PR URL or a number.", isError: true);
            return;
        }

        ApproveButton.IsEnabled = false;
        SetStatus($"Approving PR #{number} in {repo!.Owner}/{repo.Name}...", isError: false);
        try
        {
            var client = new GitHubClient(_http, token);
            await client.ApprovePullRequestAsync(repo.Owner, repo.Name, number);
            SetStatus($"Approved PR #{number} in {repo.Owner}/{repo.Name}.", isError: false, success: true);
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
