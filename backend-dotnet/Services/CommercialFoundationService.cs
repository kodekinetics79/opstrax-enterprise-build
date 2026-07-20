using System.Globalization;
using System.Text.Json;
using Opstrax.Api.Data;

namespace Opstrax.Api.Services;

public sealed record CustomerSiteRecord(
    long Id,
    long CompanyId,
    long CustomerId,
    string SiteCode,
    string SiteName,
    string SiteType,
    string? AddressLine1,
    string? AddressLine2,
    string? City,
    string? State,
    string? PostalCode,
    string CountryCode,
    decimal? GeoLatitude,
    decimal? GeoLongitude,
    string? AccessInstructions,
    string? ExternalReference,
    string Status,
    string? SourceChannel,
    string? ClientGeneratedId,
    string? IdempotencyKey,
    string? CorrelationId,
    string? CausationId,
    string? MetadataJson,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt);

public sealed record ContractVersionRecord(
    long Id,
    long CompanyId,
    long ContractId,
    int VersionNo,
    string? VersionLabel,
    string Status,
    bool IsCurrent,
    DateOnly? EffectiveDate,
    DateOnly? ExpiryDate,
    string Currency,
    decimal BaseRate,
    string RateType,
    bool FuelSurchargeEnabled,
    decimal? FuelSurchargePercent,
    string? SlaTerms,
    string MarginRisk,
    string ContractSnapshotJson,
    string? PricingJson,
    string? TermsJson,
    string? Notes,
    string? SourceChannel,
    string? ClientGeneratedId,
    string? IdempotencyKey,
    string? CorrelationId,
    string? CausationId,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt);

public sealed class CommercialFoundationService(Database db)
{
    public async Task<IReadOnlyList<CustomerSiteRecord>> ListCustomerSitesAsync(long companyId, long customerId, CancellationToken ct = default)
    {
        var rows = await db.QueryAsync(
            @"SELECT *
              FROM customer_sites
              WHERE company_id=@companyId AND customer_id=@customerId
              ORDER BY status DESC, created_at DESC, id DESC",
            c =>
            {
                c.Parameters.AddWithValue("@companyId", companyId);
                c.Parameters.AddWithValue("@customerId", customerId);
            }, ct);
        return rows.Select(MapCustomerSite).ToList();
    }

    public async Task<CustomerSiteRecord> UpsertCustomerSiteAsync(
        long companyId,
        long customerId,
        string siteCode,
        string siteName,
        string? siteType,
        string? addressLine1,
        string? addressLine2,
        string? city,
        string? state,
        string? postalCode,
        string? countryCode,
        decimal? geoLatitude,
        decimal? geoLongitude,
        string? accessInstructions,
        string? externalReference,
        string? status,
        string? sourceChannel,
        string? clientGeneratedId,
        string? idempotencyKey,
        string? correlationId,
        string? causationId,
        string? metadataJson,
        CancellationToken ct = default)
    {
        if (!string.IsNullOrWhiteSpace(idempotencyKey))
        {
            var existing = await db.QuerySingleAsync(
                "SELECT * FROM customer_sites WHERE company_id=@companyId AND idempotency_key=@idempotencyKey LIMIT 1",
                c =>
                {
                    c.Parameters.AddWithValue("@companyId", companyId);
                    c.Parameters.AddWithValue("@idempotencyKey", idempotencyKey);
                }, ct);
            if (existing is not null)
            {
                return MapCustomerSite(existing);
            }
        }

        var normalizedSiteCode = Normalize(siteCode, $"SITE-{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}");
        var now = DateTimeOffset.UtcNow;
        await db.ExecuteAsync(
            @"INSERT INTO customer_sites
                (company_id, customer_id, site_code, site_name, site_type, address_line1, address_line2, city, state, postal_code,
                 country_code, geo_latitude, geo_longitude, access_instructions, external_reference, status, source_channel,
                 client_generated_id, idempotency_key, correlation_id, causation_id, metadata_json, created_at, updated_at)
              VALUES
                (@companyId, @customerId, @siteCode, @siteName, COALESCE(@siteType, 'service'), @addressLine1, @addressLine2,
                 @city, @state, @postalCode, COALESCE(@countryCode, 'US'), @geoLatitude, @geoLongitude, @accessInstructions,
                 @externalReference, COALESCE(@status, 'Active'), @sourceChannel, @clientGeneratedId, @idempotencyKey,
                 @correlationId, @causationId, @metadata::jsonb, @createdAt, @updatedAt)
              ON CONFLICT (company_id, customer_id, site_code) DO UPDATE SET
                site_name = EXCLUDED.site_name,
                site_type = EXCLUDED.site_type,
                address_line1 = EXCLUDED.address_line1,
                address_line2 = EXCLUDED.address_line2,
                city = EXCLUDED.city,
                state = EXCLUDED.state,
                postal_code = EXCLUDED.postal_code,
                country_code = EXCLUDED.country_code,
                geo_latitude = EXCLUDED.geo_latitude,
                geo_longitude = EXCLUDED.geo_longitude,
                access_instructions = EXCLUDED.access_instructions,
                external_reference = EXCLUDED.external_reference,
                status = EXCLUDED.status,
                source_channel = EXCLUDED.source_channel,
                client_generated_id = EXCLUDED.client_generated_id,
                idempotency_key = COALESCE(EXCLUDED.idempotency_key, customer_sites.idempotency_key),
                correlation_id = EXCLUDED.correlation_id,
                causation_id = EXCLUDED.causation_id,
                metadata_json = EXCLUDED.metadata_json,
                updated_at = EXCLUDED.updated_at",
            c =>
            {
                c.Parameters.AddWithValue("@companyId", companyId);
                c.Parameters.AddWithValue("@customerId", customerId);
                c.Parameters.AddWithValue("@siteCode", normalizedSiteCode);
                c.Parameters.AddWithValue("@siteName", Normalize(siteName, normalizedSiteCode));
                c.Parameters.AddWithValue("@siteType", (object?)siteType ?? DBNull.Value);
                c.Parameters.AddWithValue("@addressLine1", (object?)addressLine1 ?? DBNull.Value);
                c.Parameters.AddWithValue("@addressLine2", (object?)addressLine2 ?? DBNull.Value);
                c.Parameters.AddWithValue("@city", (object?)city ?? DBNull.Value);
                c.Parameters.AddWithValue("@state", (object?)state ?? DBNull.Value);
                c.Parameters.AddWithValue("@postalCode", (object?)postalCode ?? DBNull.Value);
                c.Parameters.AddWithValue("@countryCode", (object?)countryCode ?? DBNull.Value);
                c.Parameters.AddWithValue("@geoLatitude", (object?)geoLatitude ?? DBNull.Value);
                c.Parameters.AddWithValue("@geoLongitude", (object?)geoLongitude ?? DBNull.Value);
                c.Parameters.AddWithValue("@accessInstructions", (object?)accessInstructions ?? DBNull.Value);
                c.Parameters.AddWithValue("@externalReference", (object?)externalReference ?? DBNull.Value);
                c.Parameters.AddWithValue("@status", (object?)status ?? DBNull.Value);
                c.Parameters.AddWithValue("@sourceChannel", (object?)sourceChannel ?? DBNull.Value);
                c.Parameters.AddWithValue("@clientGeneratedId", (object?)clientGeneratedId ?? DBNull.Value);
                c.Parameters.AddWithValue("@idempotencyKey", (object?)idempotencyKey ?? DBNull.Value);
                c.Parameters.AddWithValue("@correlationId", (object?)correlationId ?? DBNull.Value);
                c.Parameters.AddWithValue("@causationId", (object?)causationId ?? DBNull.Value);
                c.Parameters.AddWithValue("@metadata", (object?)metadataJson ?? "{}");
                c.Parameters.AddWithValue("@createdAt", now);
                c.Parameters.AddWithValue("@updatedAt", now);
            }, ct);

        var row = await db.QuerySingleAsync(
            @"SELECT * FROM customer_sites WHERE company_id=@companyId AND customer_id=@customerId AND site_code=@siteCode LIMIT 1",
            c =>
            {
                c.Parameters.AddWithValue("@companyId", companyId);
                c.Parameters.AddWithValue("@customerId", customerId);
                c.Parameters.AddWithValue("@siteCode", normalizedSiteCode);
            }, ct);
        return row is null ? throw new InvalidOperationException("Customer site could not be loaded after save") : MapCustomerSite(row);
    }

    public async Task<IReadOnlyList<ContractVersionRecord>> ListContractVersionsAsync(long companyId, long contractId, CancellationToken ct = default)
    {
        var rows = await db.QueryAsync(
            @"SELECT *
              FROM contract_versions
              WHERE company_id=@companyId AND contract_id=@contractId
              ORDER BY version_no DESC, created_at DESC, id DESC",
            c =>
            {
                c.Parameters.AddWithValue("@companyId", companyId);
                c.Parameters.AddWithValue("@contractId", contractId);
            }, ct);
        return rows.Select(MapContractVersion).ToList();
    }

    public async Task<ContractVersionRecord?> CaptureContractVersionAsync(
        long companyId,
        long contractId,
        Dictionary<string, object?> contractRow,
        string? sourceChannel,
        string? clientGeneratedId,
        string? idempotencyKey,
        string? correlationId,
        string? causationId,
        string? versionLabel,
        CancellationToken ct = default)
    {
        if (!string.IsNullOrWhiteSpace(idempotencyKey))
        {
            var existing = await db.QuerySingleAsync(
                "SELECT * FROM contract_versions WHERE company_id=@companyId AND idempotency_key=@idempotencyKey LIMIT 1",
                c =>
                {
                    c.Parameters.AddWithValue("@companyId", companyId);
                    c.Parameters.AddWithValue("@idempotencyKey", idempotencyKey);
                }, ct);
            if (existing is not null)
            {
                return MapContractVersion(existing);
            }
        }

        var versionNo = (int)(await db.ScalarLongAsync(
            "SELECT COALESCE(MAX(version_no), 0) + 1 FROM contract_versions WHERE company_id=@companyId AND contract_id=@contractId",
            c =>
            {
                c.Parameters.AddWithValue("@companyId", companyId);
                c.Parameters.AddWithValue("@contractId", contractId);
            }, ct));

        await db.ExecuteAsync(
            @"UPDATE contract_versions
              SET is_current = FALSE,
                  updated_at = NOW()
              WHERE company_id=@companyId AND contract_id=@contractId AND is_current = TRUE",
            c =>
            {
                c.Parameters.AddWithValue("@companyId", companyId);
                c.Parameters.AddWithValue("@contractId", contractId);
            }, ct);

        var effectiveDate = DateOnlyN(contractRow, "effectiveDate") ?? DateOnlyN(contractRow, "effective_date");
        var expiryDate = DateOnlyN(contractRow, "expiryDate") ?? DateOnlyN(contractRow, "expiry_date");
        var currency = S(contractRow, "currency") ?? "USD";
        var baseRate = D(contractRow, "baseRate", D(contractRow, "base_rate"));
        var rateType = S(contractRow, "rateType") ?? S(contractRow, "rate_type") ?? "Per Mile";
        var fuelSurchargeEnabled = B(contractRow, "fuelSurchargeEnabled", B(contractRow, "fuel_surcharge_enabled"));
        var fuelSurchargePercent = DN(contractRow, "fuelSurchargePercent") ?? DN(contractRow, "fuel_surcharge_percent");
        var slaTerms = S(contractRow, "slaTerms") ?? S(contractRow, "sla_terms");
        var marginRisk = S(contractRow, "marginRisk") ?? S(contractRow, "margin_risk") ?? "Low";
        var status = S(contractRow, "status") ?? "draft";

        var snapshotJson = JsonSerializer.Serialize(contractRow);
        var pricingJson = JsonSerializer.Serialize(new
        {
            currency,
            baseRate,
            rateType,
            fuelSurchargeEnabled,
            fuelSurchargePercent
        });
        var termsJson = JsonSerializer.Serialize(new
        {
            contractNumber = S(contractRow, "contractNumber") ?? S(contractRow, "contract_number"),
            customerId = LN(contractRow, "customerId") ?? LN(contractRow, "customer_id"),
            carrierId = LN(contractRow, "carrierId") ?? LN(contractRow, "carrier_id"),
            contractType = S(contractRow, "contractType") ?? S(contractRow, "contract_type"),
            effectiveDate,
            expiryDate,
            marginRisk,
            slaTerms,
            status
        });

        var id = await db.InsertAsync(
            @"INSERT INTO contract_versions
                (company_id, contract_id, version_no, version_label, status, is_current, effective_date, expiry_date, currency, base_rate,
                 rate_type, fuel_surcharge_enabled, fuel_surcharge_percent, sla_terms, margin_risk, contract_snapshot_json, pricing_json,
                 terms_json, notes, source_channel, client_generated_id, idempotency_key, correlation_id, causation_id, created_at)
              VALUES
                (@companyId, @contractId, @versionNo, @versionLabel, COALESCE(@status, 'draft'), TRUE, @effectiveDate, @expiryDate,
                 @currency, @baseRate, @rateType, @fuelSurchargeEnabled, @fuelSurchargePercent, @slaTerms, @marginRisk,
                 @snapshot::jsonb, @pricing::jsonb, @terms::jsonb, @notes, @sourceChannel, @clientGeneratedId, @idempotencyKey,
                 @correlationId, @causationId, NOW())",
            c =>
            {
                c.Parameters.AddWithValue("@companyId", companyId);
                c.Parameters.AddWithValue("@contractId", contractId);
                c.Parameters.AddWithValue("@versionNo", versionNo);
                c.Parameters.AddWithValue("@versionLabel", (object?)versionLabel ?? DBNull.Value);
                c.Parameters.AddWithValue("@status", status);
                c.Parameters.AddWithValue("@effectiveDate", (object?)effectiveDate?.ToDateTime(TimeOnly.MinValue) ?? DBNull.Value);
                c.Parameters.AddWithValue("@expiryDate", (object?)expiryDate?.ToDateTime(TimeOnly.MinValue) ?? DBNull.Value);
                c.Parameters.AddWithValue("@currency", currency);
                c.Parameters.AddWithValue("@baseRate", baseRate);
                c.Parameters.AddWithValue("@rateType", rateType);
                c.Parameters.AddWithValue("@fuelSurchargeEnabled", fuelSurchargeEnabled);
                c.Parameters.AddWithValue("@fuelSurchargePercent", (object?)fuelSurchargePercent ?? DBNull.Value);
                c.Parameters.AddWithValue("@slaTerms", (object?)slaTerms ?? DBNull.Value);
                c.Parameters.AddWithValue("@marginRisk", marginRisk);
                c.Parameters.AddWithValue("@snapshot", snapshotJson);
                c.Parameters.AddWithValue("@pricing", pricingJson);
                c.Parameters.AddWithValue("@terms", termsJson);
                c.Parameters.AddWithValue("@notes", (object?)S(contractRow, "notes") ?? DBNull.Value);
                c.Parameters.AddWithValue("@sourceChannel", (object?)sourceChannel ?? DBNull.Value);
                c.Parameters.AddWithValue("@clientGeneratedId", (object?)clientGeneratedId ?? DBNull.Value);
                c.Parameters.AddWithValue("@idempotencyKey", (object?)idempotencyKey ?? DBNull.Value);
                c.Parameters.AddWithValue("@correlationId", (object?)correlationId ?? DBNull.Value);
                c.Parameters.AddWithValue("@causationId", (object?)causationId ?? DBNull.Value);
            }, ct);

        var row = await db.QuerySingleAsync(
            @"SELECT * FROM contract_versions WHERE company_id=@companyId AND id=@id LIMIT 1",
            c =>
            {
                c.Parameters.AddWithValue("@companyId", companyId);
                c.Parameters.AddWithValue("@id", id);
            }, ct);
        return row is null ? null : MapContractVersion(row);
    }

    private static CustomerSiteRecord MapCustomerSite(Dictionary<string, object?> row) => new(
        L(row, "id"),
        L(row, "companyId"),
        L(row, "customerId"),
        S(row, "siteCode") ?? string.Empty,
        S(row, "siteName") ?? string.Empty,
        S(row, "siteType") ?? "service",
        S(row, "addressLine1"),
        S(row, "addressLine2"),
        S(row, "city"),
        S(row, "state"),
        S(row, "postalCode"),
        S(row, "countryCode") ?? "US",
        DN(row, "geoLatitude"),
        DN(row, "geoLongitude"),
        S(row, "accessInstructions"),
        S(row, "externalReference"),
        S(row, "status") ?? "Active",
        S(row, "sourceChannel"),
        S(row, "clientGeneratedId"),
        S(row, "idempotencyKey"),
        S(row, "correlationId"),
        S(row, "causationId"),
        S(row, "metadataJson"),
        Dto(row, "createdAt"),
        DtoN(row, "updatedAt"));

    private static ContractVersionRecord MapContractVersion(Dictionary<string, object?> row) => new(
        L(row, "id"),
        L(row, "companyId"),
        L(row, "contractId"),
        Convert.ToInt32(L(row, "versionNo")),
        S(row, "versionLabel"),
        S(row, "status") ?? "draft",
        B(row, "isCurrent"),
        DateOnlyN(row, "effectiveDate"),
        DateOnlyN(row, "expiryDate"),
        S(row, "currency") ?? "USD",
        D(row, "baseRate"),
        S(row, "rateType") ?? "Per Mile",
        B(row, "fuelSurchargeEnabled"),
        DN(row, "fuelSurchargePercent"),
        S(row, "slaTerms"),
        S(row, "marginRisk") ?? "Low",
        S(row, "contractSnapshotJson") ?? "{}",
        S(row, "pricingJson"),
        S(row, "termsJson"),
        S(row, "notes"),
        S(row, "sourceChannel"),
        S(row, "clientGeneratedId"),
        S(row, "idempotencyKey"),
        S(row, "correlationId"),
        S(row, "causationId"),
        Dto(row, "createdAt"),
        DtoN(row, "updatedAt"));

    private static string Normalize(string? value, string fallback)
        => string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();

    private static string? S(Dictionary<string, object?> row, string key)
        => row.TryGetValue(key, out var value) && value is not null and not DBNull ? Convert.ToString(value, CultureInfo.InvariantCulture) : null;

    private static long L(Dictionary<string, object?> row, string key)
        => row.TryGetValue(key, out var value) && value is not null and not DBNull ? Convert.ToInt64(value, CultureInfo.InvariantCulture) : 0;

    private static long? LN(Dictionary<string, object?> row, string key)
        => row.TryGetValue(key, out var value) && value is not null and not DBNull ? Convert.ToInt64(value, CultureInfo.InvariantCulture) : null;

    private static decimal D(Dictionary<string, object?> row, string key, decimal fallback = 0m)
        => row.TryGetValue(key, out var value) && value is not null and not DBNull ? Convert.ToDecimal(value, CultureInfo.InvariantCulture) : fallback;

    private static decimal? DN(Dictionary<string, object?> row, string key)
        => row.TryGetValue(key, out var value) && value is not null and not DBNull ? Convert.ToDecimal(value, CultureInfo.InvariantCulture) : null;

    private static bool B(Dictionary<string, object?> row, string key, bool fallback = false)
        => row.TryGetValue(key, out var value) && value is not null and not DBNull ? Convert.ToBoolean(value, CultureInfo.InvariantCulture) : fallback;

    private static DateTimeOffset Dto(Dictionary<string, object?> row, string key)
    {
        if (!row.TryGetValue(key, out var value) || value is null || value is DBNull)
            return DateTimeOffset.UnixEpoch;
        return value switch
        {
            DateTimeOffset dto => dto,
            DateTime dt => new DateTimeOffset(DateTime.SpecifyKind(dt, DateTimeKind.Utc)),
            _ => new DateTimeOffset(Convert.ToDateTime(value, CultureInfo.InvariantCulture), TimeSpan.Zero)
        };
    }

    private static DateTimeOffset? DtoN(Dictionary<string, object?> row, string key)
        => row.TryGetValue(key, out var value) && value is not null and not DBNull ? Dto(row, key) : null;

    private static DateOnly? DateOnlyN(Dictionary<string, object?> row, string key)
        => row.TryGetValue(key, out var value) && value is not null and not DBNull
            ? DateOnly.FromDateTime(Convert.ToDateTime(value, CultureInfo.InvariantCulture))
            : null;
}
