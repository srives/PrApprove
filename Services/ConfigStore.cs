using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace PrApprover.Services;

public sealed class Config
{
    public string? ApiKey { get; set; }
    public string? RepoUrl { get; set; }
}

public sealed class ConfigStore
{
    private static readonly string Dir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PrApprover");

    private static readonly string File = Path.Combine(Dir, "config.dat");

    public Config Load()
    {
        try
        {
            if (!System.IO.File.Exists(File)) return new Config();
            var encrypted = System.IO.File.ReadAllBytes(File);
            var plain = ProtectedData.Unprotect(encrypted, null, DataProtectionScope.CurrentUser);
            var json = Encoding.UTF8.GetString(plain);
            return JsonSerializer.Deserialize<Config>(json) ?? new Config();
        }
        catch
        {
            return new Config();
        }
    }

    public void Save(Config config)
    {
        Directory.CreateDirectory(Dir);
        var json = JsonSerializer.Serialize(config);
        var plain = Encoding.UTF8.GetBytes(json);
        var encrypted = ProtectedData.Protect(plain, null, DataProtectionScope.CurrentUser);
        System.IO.File.WriteAllBytes(File, encrypted);
    }
}
