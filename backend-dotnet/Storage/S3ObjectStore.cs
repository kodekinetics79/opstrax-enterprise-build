using Amazon.S3;
using Amazon.S3.Model;

namespace Opstrax.Api.Storage;

// ─────────────────────────────────────────────────────────────────────────────
// S3ObjectStore — S3-compatible backing store.
//
// Works unchanged against Cloudflare R2, AWS S3, Backblaze B2, Wasabi, or MinIO —
// they all speak the S3 API. Which one is chosen purely by config (endpoint +
// credentials); no code change. Objects are PRIVATE; access is only ever granted
// via short-lived presigned GET URLs. Server-side encryption is requested on write.
// ─────────────────────────────────────────────────────────────────────────────

public sealed class S3ObjectStore : IObjectStore
{
    private readonly IAmazonS3 _s3;
    private readonly string _bucket;
    private readonly bool _sse;
    private readonly ILogger<S3ObjectStore> _logger;

    public string Provider => "s3";
    public bool IsConfigured => true;

    public S3ObjectStore(S3Options options, ILogger<S3ObjectStore> logger)
    {
        _bucket = options.Bucket;
        _sse = options.ServerSideEncryption;
        _logger = logger;

        var config = new AmazonS3Config
        {
            // ForcePathStyle is required for R2/MinIO/B2 (bucket in path, not host).
            ForcePathStyle = options.ForcePathStyle,
        };
        if (!string.IsNullOrWhiteSpace(options.ServiceUrl))
            config.ServiceURL = options.ServiceUrl;        // R2/MinIO/B2 custom endpoint
        else if (!string.IsNullOrWhiteSpace(options.Region))
            config.RegionEndpoint = Amazon.RegionEndpoint.GetBySystemName(options.Region); // AWS

        _s3 = new AmazonS3Client(options.AccessKey, options.SecretKey, config);
    }

    public async Task<string> PutAsync(string key, Stream content, string contentType, CancellationToken ct = default)
    {
        var req = new PutObjectRequest
        {
            BucketName = _bucket,
            Key = key,
            InputStream = content,
            ContentType = contentType,
            DisablePayloadSigning = true, // R2 compatibility for streaming uploads
        };
        if (_sse) req.ServerSideEncryptionMethod = ServerSideEncryptionMethod.AES256;
        await _s3.PutObjectAsync(req, ct);
        return key;
    }

    public async Task<Stream> GetAsync(string key, CancellationToken ct = default)
    {
        var resp = await _s3.GetObjectAsync(_bucket, key, ct);
        return resp.ResponseStream;
    }

    public async Task DeleteAsync(string key, CancellationToken ct = default)
    {
        await _s3.DeleteObjectAsync(_bucket, key, ct);
    }

    public async Task<int> DeletePrefixAsync(string prefix, CancellationToken ct = default)
    {
        var deleted = 0;
        string? continuationToken = null;
        do
        {
            var list = await _s3.ListObjectsV2Async(new ListObjectsV2Request
            {
                BucketName = _bucket,
                Prefix = prefix,
                ContinuationToken = continuationToken,
            }, ct);

            if (list.S3Objects.Count > 0)
            {
                await _s3.DeleteObjectsAsync(new DeleteObjectsRequest
                {
                    BucketName = _bucket,
                    Objects = list.S3Objects.Select(o => new KeyVersion { Key = o.Key }).ToList(),
                }, ct);
                deleted += list.S3Objects.Count;
            }
            continuationToken = list.IsTruncated == true ? list.NextContinuationToken : null;
        } while (continuationToken is not null);

        return deleted;
    }

    public Task<string?> SignedUrlAsync(string key, TimeSpan ttl, CancellationToken ct = default)
    {
        var url = _s3.GetPreSignedURL(new GetPreSignedUrlRequest
        {
            BucketName = _bucket,
            Key = key,
            Expires = DateTime.UtcNow.Add(ttl),
            Verb = HttpVerb.GET,
        });
        return Task.FromResult<string?>(url);
    }

    public async Task<bool> HealthCheckAsync(CancellationToken ct = default)
    {
        try
        {
            // Cheap: list at most 1 object. Confirms creds + bucket + reachability.
            await _s3.ListObjectsV2Async(new ListObjectsV2Request { BucketName = _bucket, MaxKeys = 1 }, ct);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(new EventId(0, "object_store_unhealthy"), ex, "Object store health check failed");
            return false;
        }
    }
}

public sealed class S3Options
{
    public string Bucket { get; init; } = "";
    public string? ServiceUrl { get; init; }   // R2/MinIO/B2 endpoint; null for AWS
    public string? Region { get; init; }        // AWS region; ignored when ServiceUrl set
    public string AccessKey { get; init; } = "";
    public string SecretKey { get; init; } = "";
    public bool ForcePathStyle { get; init; } = true;
    public bool ServerSideEncryption { get; init; } = true;
}
