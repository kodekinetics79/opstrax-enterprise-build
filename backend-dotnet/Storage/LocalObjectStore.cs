namespace Opstrax.Api.Storage;

// ─────────────────────────────────────────────────────────────────────────────
// LocalObjectStore — filesystem-backed store for local dev + tests.
//
// Used when no S3 credentials are configured. Keys map to files under a base
// directory (default: a temp dir). Never signs URLs (returns null) so the app
// falls back to the authenticated proxy endpoint — the same code path as prod.
// NOT for production: single-node, no durability guarantees, no residency control.
// IsConfigured is false so /health/deep and the Reliability Center flag it.
// ─────────────────────────────────────────────────────────────────────────────

public sealed class LocalObjectStore : IObjectStore
{
    private readonly string _root;

    public string Provider => "local";
    public bool IsConfigured => false; // dev fallback — surfaced as not-production

    public LocalObjectStore(string? root = null)
    {
        _root = root ?? Path.Combine(Path.GetTempPath(), "opstrax-objectstore");
        Directory.CreateDirectory(_root);
    }

    private string PathFor(string key)
    {
        // Prevent traversal; keys are app-generated but be defensive.
        var safe = key.Replace("..", "_").Replace('\\', '/');
        var full = Path.GetFullPath(Path.Combine(_root, safe));
        if (!full.StartsWith(Path.GetFullPath(_root), StringComparison.Ordinal))
            throw new InvalidOperationException("Invalid object key");
        return full;
    }

    public async Task<string> PutAsync(string key, Stream content, string contentType, CancellationToken ct = default)
    {
        var path = PathFor(key);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await using var fs = File.Create(path);
        await content.CopyToAsync(fs, ct);
        return key;
    }

    public Task<Stream> GetAsync(string key, CancellationToken ct = default)
    {
        var path = PathFor(key);
        if (!File.Exists(path)) throw new FileNotFoundException("Object not found", key);
        return Task.FromResult<Stream>(File.OpenRead(path));
    }

    public Task DeleteAsync(string key, CancellationToken ct = default)
    {
        var path = PathFor(key);
        if (File.Exists(path)) File.Delete(path);
        return Task.CompletedTask;
    }

    public Task<int> DeletePrefixAsync(string prefix, CancellationToken ct = default)
    {
        var dir = PathFor(prefix);
        var count = 0;
        if (Directory.Exists(dir))
        {
            count = Directory.GetFiles(dir, "*", SearchOption.AllDirectories).Length;
            Directory.Delete(dir, recursive: true);
        }
        return Task.FromResult(count);
    }

    public Task<string?> SignedUrlAsync(string key, TimeSpan ttl, CancellationToken ct = default)
        => Task.FromResult<string?>(null); // no signing locally — use the proxy

    public Task<bool> HealthCheckAsync(CancellationToken ct = default)
        => Task.FromResult(Directory.Exists(_root));
}

// ── Factory ─────────────────────────────────────────────────────────────────────
// Chooses the provider from config. If S3 creds are present → S3ObjectStore
// (R2/S3/B2/MinIO); otherwise LocalObjectStore (dev/tests). One switch, no code
// change to move between providers.
public static class ObjectStoreFactory
{
    public static IObjectStore Create(IConfiguration config, ILoggerFactory loggerFactory)
    {
        string? Get(string key, string env) =>
            config[$"Storage:{key}"] ?? Environment.GetEnvironmentVariable(env);

        var bucket    = Get("Bucket", "STORAGE_BUCKET");
        var accessKey = Get("AccessKey", "STORAGE_ACCESS_KEY");
        var secretKey = Get("SecretKey", "STORAGE_SECRET_KEY");

        if (!string.IsNullOrWhiteSpace(bucket) &&
            !string.IsNullOrWhiteSpace(accessKey) &&
            !string.IsNullOrWhiteSpace(secretKey))
        {
            var options = new S3Options
            {
                Bucket     = bucket,
                AccessKey  = accessKey,
                SecretKey  = secretKey,
                ServiceUrl = Get("ServiceUrl", "STORAGE_ENDPOINT"),        // R2/MinIO/B2
                Region     = Get("Region", "STORAGE_REGION"),              // AWS
                ForcePathStyle = (Get("ForcePathStyle", "STORAGE_FORCE_PATH_STYLE") ?? "true")
                    .Equals("true", StringComparison.OrdinalIgnoreCase),
                ServerSideEncryption = (Get("Sse", "STORAGE_SSE") ?? "true")
                    .Equals("true", StringComparison.OrdinalIgnoreCase),
            };
            return new S3ObjectStore(options, loggerFactory.CreateLogger<S3ObjectStore>());
        }

        return new LocalObjectStore(Get("LocalRoot", "STORAGE_LOCAL_ROOT"));
    }
}
