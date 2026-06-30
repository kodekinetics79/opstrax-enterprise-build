using System.Globalization;
using Opstrax.Api.Data;

namespace Opstrax.Api.Services;

public sealed record BusinessSurfaceProfileRecord(
    long Id,
    long CompanyId,
    string VerticalKey,
    string CustomerLabelSingular,
    string CustomerLabelPlural,
    string ContractLabelSingular,
    string ContractLabelPlural,
    string RateCardLabelSingular,
    string RateCardLabelPlural,
    string JobLabelSingular,
    string JobLabelPlural,
    string TripLabelSingular,
    string TripLabelPlural,
    string ChargeLabelSingular,
    string ChargeLabelPlural,
    bool UseGenericLabels,
    string? Notes,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt);

public sealed record RateCardRecord(
    long Id,
    long CompanyId,
    long? CustomerId,
    long? ContractId,
    string SourceTable,
    string RateCardCode,
    string RateCardName,
    string BillingBasis,
    string? ServiceScope,
    string? OriginZone,
    string? DestinationZone,
    string? VehicleType,
    string Currency,
    decimal BaseRate,
    decimal? MinimumCharge,
    decimal? FuelSurchargePercent,
    string? AccessorialType,
    DateOnly EffectiveDate,
    DateOnly? ExpiryDate,
    string Status,
    string? CorrelationId,
    string? CausationId,
    string? Notes,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt);

public sealed record JobChargeRecord(
    long Id,
    long CompanyId,
    long JobId,
    long? TripId,
    long? RateCardId,
    string ChargeCode,
    string ChargeName,
    string ChargeType,
    string? Description,
    decimal Quantity,
    decimal UnitRate,
    decimal Amount,
    string Currency,
    string Status,
    string? CorrelationId,
    string? CausationId,
    long? ApprovedByUserId,
    DateTimeOffset? ApprovedAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt);

public sealed class BusinessSpineService(Database db)
{
    public async Task<BusinessSurfaceProfileRecord> GetOrCreateProfileAsync(long companyId, CancellationToken ct = default)
    {
        var existing = await LoadProfileAsync(companyId, ct);
        if (existing is not null)
        {
            return existing;
        }

        var createdAt = DateTimeOffset.UtcNow;
        var id = await db.InsertAsync(
            @"INSERT INTO business_surface_profiles
                (company_id, vertical_key, customer_label_singular, customer_label_plural,
                 contract_label_singular, contract_label_plural, rate_card_label_singular, rate_card_label_plural,
                 job_label_singular, job_label_plural, trip_label_singular, trip_label_plural,
                 charge_label_singular, charge_label_plural, use_generic_labels, created_at)
              VALUES
                (@companyId, 'generic', 'Customer', 'Customers',
                 'Contract', 'Contracts', 'Rate Card', 'Rate Cards',
                 'Job', 'Jobs', 'Trip', 'Trips',
                 'Charge', 'Charges', TRUE, @createdAt)",
            c =>
            {
                c.Parameters.AddWithValue("@companyId", companyId);
                c.Parameters.AddWithValue("@createdAt", createdAt);
            }, ct);

        return (await LoadProfileByIdAsync(id, ct))!;
    }

    public async Task<BusinessSurfaceProfileRecord> UpsertProfileAsync(
        long companyId,
        string verticalKey,
        string customerLabelSingular,
        string customerLabelPlural,
        string contractLabelSingular,
        string contractLabelPlural,
        string rateCardLabelSingular,
        string rateCardLabelPlural,
        string jobLabelSingular,
        string jobLabelPlural,
        string tripLabelSingular,
        string tripLabelPlural,
        string chargeLabelSingular,
        string chargeLabelPlural,
        bool useGenericLabels,
        string? notes,
        CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        await db.ExecuteAsync(
            @"INSERT INTO business_surface_profiles
                (company_id, vertical_key, customer_label_singular, customer_label_plural,
                 contract_label_singular, contract_label_plural, rate_card_label_singular, rate_card_label_plural,
                 job_label_singular, job_label_plural, trip_label_singular, trip_label_plural,
                 charge_label_singular, charge_label_plural, use_generic_labels, notes, created_at, updated_at)
              VALUES
                (@companyId, @verticalKey, @customerLabelSingular, @customerLabelPlural,
                 @contractLabelSingular, @contractLabelPlural, @rateCardLabelSingular, @rateCardLabelPlural,
                 @jobLabelSingular, @jobLabelPlural, @tripLabelSingular, @tripLabelPlural,
                 @chargeLabelSingular, @chargeLabelPlural, @useGenericLabels, @notes, @createdAt, @updatedAt)
              ON CONFLICT (company_id) DO UPDATE SET
                vertical_key = EXCLUDED.vertical_key,
                customer_label_singular = EXCLUDED.customer_label_singular,
                customer_label_plural = EXCLUDED.customer_label_plural,
                contract_label_singular = EXCLUDED.contract_label_singular,
                contract_label_plural = EXCLUDED.contract_label_plural,
                rate_card_label_singular = EXCLUDED.rate_card_label_singular,
                rate_card_label_plural = EXCLUDED.rate_card_label_plural,
                job_label_singular = EXCLUDED.job_label_singular,
                job_label_plural = EXCLUDED.job_label_plural,
                trip_label_singular = EXCLUDED.trip_label_singular,
                trip_label_plural = EXCLUDED.trip_label_plural,
                charge_label_singular = EXCLUDED.charge_label_singular,
                charge_label_plural = EXCLUDED.charge_label_plural,
                use_generic_labels = EXCLUDED.use_generic_labels,
                notes = EXCLUDED.notes,
                updated_at = EXCLUDED.updated_at",
            c =>
            {
                c.Parameters.AddWithValue("@companyId", companyId);
                c.Parameters.AddWithValue("@verticalKey", Normalize(verticalKey, "generic"));
                c.Parameters.AddWithValue("@customerLabelSingular", Normalize(customerLabelSingular, "Customer"));
                c.Parameters.AddWithValue("@customerLabelPlural", Normalize(customerLabelPlural, "Customers"));
                c.Parameters.AddWithValue("@contractLabelSingular", Normalize(contractLabelSingular, "Contract"));
                c.Parameters.AddWithValue("@contractLabelPlural", Normalize(contractLabelPlural, "Contracts"));
                c.Parameters.AddWithValue("@rateCardLabelSingular", Normalize(rateCardLabelSingular, "Rate Card"));
                c.Parameters.AddWithValue("@rateCardLabelPlural", Normalize(rateCardLabelPlural, "Rate Cards"));
                c.Parameters.AddWithValue("@jobLabelSingular", Normalize(jobLabelSingular, "Job"));
                c.Parameters.AddWithValue("@jobLabelPlural", Normalize(jobLabelPlural, "Jobs"));
                c.Parameters.AddWithValue("@tripLabelSingular", Normalize(tripLabelSingular, "Trip"));
                c.Parameters.AddWithValue("@tripLabelPlural", Normalize(tripLabelPlural, "Trips"));
                c.Parameters.AddWithValue("@chargeLabelSingular", Normalize(chargeLabelSingular, "Charge"));
                c.Parameters.AddWithValue("@chargeLabelPlural", Normalize(chargeLabelPlural, "Charges"));
                c.Parameters.AddWithValue("@useGenericLabels", useGenericLabels);
                c.Parameters.AddWithValue("@notes", (object?)notes ?? DBNull.Value);
                c.Parameters.AddWithValue("@createdAt", now);
                c.Parameters.AddWithValue("@updatedAt", now);
            }, ct);

        return (await LoadProfileAsync(companyId, ct))!;
    }

    public async Task<IReadOnlyList<RateCardRecord>> ListRateCardsAsync(long companyId, long? contractId = null, CancellationToken ct = default)
    {
        var includeLegacyRates = await TableExistsAsync("contract_rates", ct);
        var sql = @"
            SELECT 'rate_cards' AS source_table, rc.*
            FROM rate_cards rc
            WHERE rc.company_id=@companyId";
        if (contractId.HasValue)
        {
            sql += " AND rc.contract_id=@contractId";
        }
        if (includeLegacyRates)
        {
            sql += @"
            UNION ALL
            SELECT 'contract_rates' AS source_table,
                   cr.id,
                   cr.company_id,
                   NULL::BIGINT AS customer_id,
                   cr.contract_id,
                   cr.rate_code AS rate_card_code,
                   cr.rate_code AS rate_card_name,
                   cr.rate_type AS billing_basis,
                   cr.accessorial_type AS service_scope,
                   cr.origin_zone,
                   cr.destination_zone,
                   cr.vehicle_type,
                   COALESCE(cr.currency, 'USD') AS currency,
                   cr.base_rate,
                   cr.minimum_charge,
                   cr.fuel_surcharge_percent,
                   cr.accessorial_type,
                   cr.effective_date,
                   cr.expiry_date,
                   cr.status,
                   NULL::VARCHAR(120) AS correlation_id,
                   NULL::VARCHAR(120) AS causation_id,
                   NULL::TEXT AS notes,
                   cr.created_at,
                   cr.updated_at
            FROM contract_rates cr
            WHERE cr.company_id=@companyId
              AND NOT EXISTS (
                SELECT 1
                FROM rate_cards rc
                WHERE rc.company_id = cr.company_id
                  AND COALESCE(rc.contract_id, -1) = COALESCE(cr.contract_id, -1)
                  AND rc.rate_card_code = cr.rate_code
              )";
        if (contractId.HasValue)
        {
            sql += " AND cr.contract_id=@contractId";
        }
            sql += " ORDER BY effective_date DESC, id DESC LIMIT 200";
        }
        else
        {
            sql += " ORDER BY effective_date DESC, id DESC LIMIT 200";
        }

        return (await db.QueryAsync(sql, c =>
        {
            c.Parameters.AddWithValue("@companyId", companyId);
            if (contractId.HasValue)
            {
                c.Parameters.AddWithValue("@contractId", contractId.Value);
            }
        }, ct)).Select(MapRateCard).ToList();
    }

    public async Task<RateCardRecord> CreateRateCardAsync(
        long companyId,
        string rateCardCode,
        string rateCardName,
        long? customerId,
        long? contractId,
        string? billingBasis,
        string? serviceScope,
        string? originZone,
        string? destinationZone,
        string? vehicleType,
        string? currency,
        decimal baseRate,
        decimal? minimumCharge,
        decimal? fuelSurchargePercent,
        string? accessorialType,
        DateOnly effectiveDate,
        DateOnly? expiryDate,
        string? status,
        string? correlationId = null,
        string? causationId = null,
        string? notes = null,
        CancellationToken ct = default)
    {
        var createdAt = DateTimeOffset.UtcNow;
        var id = await db.InsertAsync(
            @"INSERT INTO rate_cards
                (company_id, customer_id, contract_id, rate_card_code, rate_card_name,
                 billing_basis, service_scope, origin_zone, destination_zone, vehicle_type,
                 currency, base_rate, minimum_charge, fuel_surcharge_percent, accessorial_type,
                 effective_date, expiry_date, status, correlation_id, causation_id, notes, created_at)
              VALUES
                (@companyId, @customerId, @contractId, @rateCardCode, @rateCardName,
                 COALESCE(@billingBasis, 'Per Unit'), @serviceScope, @originZone, @destinationZone, @vehicleType,
                 COALESCE(@currency, 'USD'), @baseRate, @minimumCharge, @fuelSurchargePercent, @accessorialType,
                 @effectiveDate, @expiryDate, COALESCE(@status, 'Active'), @correlationId, @causationId, @notes, @createdAt)",
            c =>
            {
                c.Parameters.AddWithValue("@companyId", companyId);
                c.Parameters.AddWithValue("@customerId", (object?)customerId ?? DBNull.Value);
                c.Parameters.AddWithValue("@contractId", (object?)contractId ?? DBNull.Value);
                c.Parameters.AddWithValue("@rateCardCode", rateCardCode);
                c.Parameters.AddWithValue("@rateCardName", rateCardName);
                c.Parameters.AddWithValue("@billingBasis", (object?)billingBasis ?? DBNull.Value);
                c.Parameters.AddWithValue("@serviceScope", (object?)serviceScope ?? DBNull.Value);
                c.Parameters.AddWithValue("@originZone", (object?)originZone ?? DBNull.Value);
                c.Parameters.AddWithValue("@destinationZone", (object?)destinationZone ?? DBNull.Value);
                c.Parameters.AddWithValue("@vehicleType", (object?)vehicleType ?? DBNull.Value);
                c.Parameters.AddWithValue("@currency", (object?)currency ?? DBNull.Value);
                c.Parameters.AddWithValue("@baseRate", baseRate);
                c.Parameters.AddWithValue("@minimumCharge", (object?)minimumCharge ?? DBNull.Value);
                c.Parameters.AddWithValue("@fuelSurchargePercent", (object?)fuelSurchargePercent ?? DBNull.Value);
                c.Parameters.AddWithValue("@accessorialType", (object?)accessorialType ?? DBNull.Value);
                c.Parameters.AddWithValue("@effectiveDate", effectiveDate);
                c.Parameters.AddWithValue("@expiryDate", expiryDate.HasValue ? expiryDate.Value.ToDateTime(TimeOnly.MinValue) : DBNull.Value);
                c.Parameters.AddWithValue("@status", (object?)status ?? DBNull.Value);
                c.Parameters.AddWithValue("@correlationId", (object?)correlationId ?? DBNull.Value);
                c.Parameters.AddWithValue("@causationId", (object?)causationId ?? DBNull.Value);
                c.Parameters.AddWithValue("@notes", (object?)notes ?? DBNull.Value);
                c.Parameters.AddWithValue("@createdAt", createdAt);
            }, ct);

        return (await LoadRateCardByIdAsync(companyId, id, ct))!;
    }

    public Task<RateCardRecord?> GetRateCardByIdAsync(long companyId, long id, CancellationToken ct = default)
        => LoadRateCardByIdAsync(companyId, id, ct);

    public async Task<RateCardRecord?> UpsertRateCardMirrorAsync(
        long companyId,
        string rateCardCode,
        string rateCardName,
        long? customerId,
        long? contractId,
        string? billingBasis,
        string? serviceScope,
        string? originZone,
        string? destinationZone,
        string? vehicleType,
        string? currency,
        decimal baseRate,
        decimal? minimumCharge,
        decimal? fuelSurchargePercent,
        string? accessorialType,
        DateOnly effectiveDate,
        DateOnly? expiryDate,
        string? status,
        string? correlationId = null,
        string? causationId = null,
        string? notes = null,
        CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        var id = await db.InsertAsync(
            @"INSERT INTO rate_cards
                (company_id, customer_id, contract_id, rate_card_code, rate_card_name,
                 billing_basis, service_scope, origin_zone, destination_zone, vehicle_type,
                 currency, base_rate, minimum_charge, fuel_surcharge_percent, accessorial_type,
                 effective_date, expiry_date, status, correlation_id, causation_id, notes, created_at, updated_at)
              VALUES
                (@companyId, @customerId, @contractId, @rateCardCode, @rateCardName,
                 COALESCE(@billingBasis, 'Per Unit'), @serviceScope, @originZone, @destinationZone, @vehicleType,
                 COALESCE(@currency, 'USD'), @baseRate, @minimumCharge, @fuelSurchargePercent, @accessorialType,
                 @effectiveDate, @expiryDate, COALESCE(@status, 'Active'), @correlationId, @causationId, @notes, @createdAt, @updatedAt)
              ON CONFLICT (company_id, rate_card_code) DO UPDATE SET
                 customer_id = EXCLUDED.customer_id,
                 contract_id = EXCLUDED.contract_id,
                 rate_card_name = EXCLUDED.rate_card_name,
                 billing_basis = EXCLUDED.billing_basis,
                 service_scope = EXCLUDED.service_scope,
                 origin_zone = EXCLUDED.origin_zone,
                 destination_zone = EXCLUDED.destination_zone,
                 vehicle_type = EXCLUDED.vehicle_type,
                 currency = EXCLUDED.currency,
                 base_rate = EXCLUDED.base_rate,
                 minimum_charge = EXCLUDED.minimum_charge,
                 fuel_surcharge_percent = EXCLUDED.fuel_surcharge_percent,
                 accessorial_type = EXCLUDED.accessorial_type,
                 effective_date = EXCLUDED.effective_date,
                 expiry_date = EXCLUDED.expiry_date,
                 status = EXCLUDED.status,
                 correlation_id = EXCLUDED.correlation_id,
                 causation_id = EXCLUDED.causation_id,
                 notes = EXCLUDED.notes,
                 updated_at = EXCLUDED.updated_at
              RETURNING id",
            c =>
            {
                c.Parameters.AddWithValue("@companyId", companyId);
                c.Parameters.AddWithValue("@customerId", (object?)customerId ?? DBNull.Value);
                c.Parameters.AddWithValue("@contractId", (object?)contractId ?? DBNull.Value);
                c.Parameters.AddWithValue("@rateCardCode", rateCardCode);
                c.Parameters.AddWithValue("@rateCardName", rateCardName);
                c.Parameters.AddWithValue("@billingBasis", (object?)billingBasis ?? DBNull.Value);
                c.Parameters.AddWithValue("@serviceScope", (object?)serviceScope ?? DBNull.Value);
                c.Parameters.AddWithValue("@originZone", (object?)originZone ?? DBNull.Value);
                c.Parameters.AddWithValue("@destinationZone", (object?)destinationZone ?? DBNull.Value);
                c.Parameters.AddWithValue("@vehicleType", (object?)vehicleType ?? DBNull.Value);
                c.Parameters.AddWithValue("@currency", (object?)currency ?? DBNull.Value);
                c.Parameters.AddWithValue("@baseRate", baseRate);
                c.Parameters.AddWithValue("@minimumCharge", (object?)minimumCharge ?? DBNull.Value);
                c.Parameters.AddWithValue("@fuelSurchargePercent", (object?)fuelSurchargePercent ?? DBNull.Value);
                c.Parameters.AddWithValue("@accessorialType", (object?)accessorialType ?? DBNull.Value);
                c.Parameters.AddWithValue("@effectiveDate", effectiveDate);
                c.Parameters.AddWithValue("@expiryDate", expiryDate.HasValue ? expiryDate.Value.ToDateTime(TimeOnly.MinValue) : DBNull.Value);
                c.Parameters.AddWithValue("@status", (object?)status ?? DBNull.Value);
                c.Parameters.AddWithValue("@correlationId", (object?)correlationId ?? DBNull.Value);
                c.Parameters.AddWithValue("@causationId", (object?)causationId ?? DBNull.Value);
                c.Parameters.AddWithValue("@notes", (object?)notes ?? DBNull.Value);
                c.Parameters.AddWithValue("@createdAt", now);
                c.Parameters.AddWithValue("@updatedAt", now);
            }, ct);

        return await LoadRateCardByIdAsync(companyId, id, ct);
    }

    public async Task<RateCardRecord?> UpdateRateCardAsync(
        long companyId,
        long id,
        string? rateCardCode = null,
        string? rateCardName = null,
        long? customerId = null,
        long? contractId = null,
        string? billingBasis = null,
        string? serviceScope = null,
        string? originZone = null,
        string? destinationZone = null,
        string? vehicleType = null,
        string? currency = null,
        decimal? baseRate = null,
        decimal? minimumCharge = null,
        decimal? fuelSurchargePercent = null,
        string? accessorialType = null,
        DateOnly? effectiveDate = null,
        DateOnly? expiryDate = null,
        string? status = null,
        string? correlationId = null,
        string? causationId = null,
        string? notes = null,
        CancellationToken ct = default)
    {
        var affected = await db.ExecuteAsync(
            @"UPDATE rate_cards SET
                rate_card_code=COALESCE(@rateCardCode, rate_card_code),
                rate_card_name=COALESCE(@rateCardName, rate_card_name),
                customer_id=COALESCE(@customerId, customer_id),
                contract_id=COALESCE(@contractId, contract_id),
                billing_basis=COALESCE(@billingBasis, billing_basis),
                service_scope=COALESCE(@serviceScope, service_scope),
                origin_zone=COALESCE(@originZone, origin_zone),
                destination_zone=COALESCE(@destinationZone, destination_zone),
                vehicle_type=COALESCE(@vehicleType, vehicle_type),
                currency=COALESCE(@currency, currency),
                base_rate=COALESCE(@baseRate, base_rate),
                minimum_charge=COALESCE(@minimumCharge, minimum_charge),
                fuel_surcharge_percent=COALESCE(@fuelSurchargePercent, fuel_surcharge_percent),
                accessorial_type=COALESCE(@accessorialType, accessorial_type),
                effective_date=COALESCE(@effectiveDate, effective_date),
                expiry_date=COALESCE(@expiryDate, expiry_date),
                status=COALESCE(@status, status),
                correlation_id=COALESCE(@correlationId, correlation_id),
                causation_id=COALESCE(@causationId, causation_id),
                notes=COALESCE(@notes, notes),
                updated_at=NOW()
              WHERE id=@id AND company_id=@companyId",
            c =>
            {
                c.Parameters.AddWithValue("@id", id);
                c.Parameters.AddWithValue("@companyId", companyId);
                c.Parameters.AddWithValue("@rateCardCode", (object?)rateCardCode ?? DBNull.Value);
                c.Parameters.AddWithValue("@rateCardName", (object?)rateCardName ?? DBNull.Value);
                c.Parameters.AddWithValue("@customerId", (object?)customerId ?? DBNull.Value);
                c.Parameters.AddWithValue("@contractId", (object?)contractId ?? DBNull.Value);
                c.Parameters.AddWithValue("@billingBasis", (object?)billingBasis ?? DBNull.Value);
                c.Parameters.AddWithValue("@serviceScope", (object?)serviceScope ?? DBNull.Value);
                c.Parameters.AddWithValue("@originZone", (object?)originZone ?? DBNull.Value);
                c.Parameters.AddWithValue("@destinationZone", (object?)destinationZone ?? DBNull.Value);
                c.Parameters.AddWithValue("@vehicleType", (object?)vehicleType ?? DBNull.Value);
                c.Parameters.AddWithValue("@currency", (object?)currency ?? DBNull.Value);
                c.Parameters.AddWithValue("@baseRate", (object?)baseRate ?? DBNull.Value);
                c.Parameters.AddWithValue("@minimumCharge", (object?)minimumCharge ?? DBNull.Value);
                c.Parameters.AddWithValue("@fuelSurchargePercent", (object?)fuelSurchargePercent ?? DBNull.Value);
                c.Parameters.AddWithValue("@accessorialType", (object?)accessorialType ?? DBNull.Value);
                c.Parameters.AddWithValue("@effectiveDate", effectiveDate.HasValue ? effectiveDate.Value.ToDateTime(TimeOnly.MinValue) : DBNull.Value);
                c.Parameters.AddWithValue("@expiryDate", expiryDate.HasValue ? expiryDate.Value.ToDateTime(TimeOnly.MinValue) : DBNull.Value);
                c.Parameters.AddWithValue("@status", (object?)status ?? DBNull.Value);
                c.Parameters.AddWithValue("@correlationId", (object?)correlationId ?? DBNull.Value);
                c.Parameters.AddWithValue("@causationId", (object?)causationId ?? DBNull.Value);
                c.Parameters.AddWithValue("@notes", (object?)notes ?? DBNull.Value);
            }, ct);

        return affected > 0 ? await LoadRateCardByIdAsync(companyId, id, ct) : null;
    }

    public async Task<IReadOnlyList<JobChargeRecord>> ListJobChargesAsync(long companyId, long? jobId = null, CancellationToken ct = default)
    {
        var sql = @"SELECT * FROM job_charges WHERE company_id=@companyId";
        if (jobId.HasValue)
        {
            sql += " AND job_id=@jobId";
        }
        sql += " ORDER BY created_at DESC, id DESC LIMIT 200";

        return (await db.QueryAsync(sql, c =>
        {
            c.Parameters.AddWithValue("@companyId", companyId);
            if (jobId.HasValue)
            {
                c.Parameters.AddWithValue("@jobId", jobId.Value);
            }
        }, ct)).Select(MapJobCharge).ToList();
    }

    public async Task<JobChargeRecord> CreateJobChargeAsync(
        long companyId,
        long jobId,
        long? tripId,
        long? rateCardId,
        string chargeCode,
        string chargeName,
        string? chargeType,
        string? description,
        decimal quantity,
        decimal unitRate,
        decimal amount,
        string? currency,
        string? status,
        string? correlationId = null,
        string? causationId = null,
        long? approvedByUserId = null,
        DateTimeOffset? approvedAt = null,
        CancellationToken ct = default)
    {
        var createdAt = DateTimeOffset.UtcNow;
        var id = await db.InsertAsync(
            @"INSERT INTO job_charges
                (company_id, job_id, trip_id, rate_card_id, charge_code, charge_name, charge_type, description,
                 quantity, unit_rate, amount, currency, status, correlation_id, causation_id, approved_by_user_id,
                 approved_at, created_at)
              VALUES
                (@companyId, @jobId, @tripId, @rateCardId, @chargeCode, @chargeName, COALESCE(@chargeType, 'base'), @description,
                 @quantity, @unitRate, @amount, COALESCE(@currency, 'USD'), COALESCE(@status, 'pending'),
                 @correlationId, @causationId, @approvedByUserId, @approvedAt, @createdAt)",
            c =>
            {
                c.Parameters.AddWithValue("@companyId", companyId);
                c.Parameters.AddWithValue("@jobId", jobId);
                c.Parameters.AddWithValue("@tripId", (object?)tripId ?? DBNull.Value);
                c.Parameters.AddWithValue("@rateCardId", (object?)rateCardId ?? DBNull.Value);
                c.Parameters.AddWithValue("@chargeCode", chargeCode);
                c.Parameters.AddWithValue("@chargeName", chargeName);
                c.Parameters.AddWithValue("@chargeType", (object?)chargeType ?? DBNull.Value);
                c.Parameters.AddWithValue("@description", (object?)description ?? DBNull.Value);
                c.Parameters.AddWithValue("@quantity", quantity);
                c.Parameters.AddWithValue("@unitRate", unitRate);
                c.Parameters.AddWithValue("@amount", amount);
                c.Parameters.AddWithValue("@currency", (object?)currency ?? DBNull.Value);
                c.Parameters.AddWithValue("@status", (object?)status ?? DBNull.Value);
                c.Parameters.AddWithValue("@correlationId", (object?)correlationId ?? DBNull.Value);
                c.Parameters.AddWithValue("@causationId", (object?)causationId ?? DBNull.Value);
                c.Parameters.AddWithValue("@approvedByUserId", (object?)approvedByUserId ?? DBNull.Value);
                c.Parameters.AddWithValue("@approvedAt", (object?)approvedAt ?? DBNull.Value);
                c.Parameters.AddWithValue("@createdAt", createdAt);
            }, ct);

        return (await LoadJobChargeByIdAsync(companyId, id, ct))!;
    }

    public Task<JobChargeRecord?> GetJobChargeByIdAsync(long companyId, long id, CancellationToken ct = default)
        => LoadJobChargeByIdAsync(companyId, id, ct);

    public async Task<JobChargeRecord?> UpdateJobChargeAsync(
        long companyId,
        long id,
        long? jobId = null,
        long? tripId = null,
        long? rateCardId = null,
        string? chargeCode = null,
        string? chargeName = null,
        string? chargeType = null,
        string? description = null,
        decimal? quantity = null,
        decimal? unitRate = null,
        decimal? amount = null,
        string? currency = null,
        string? status = null,
        string? correlationId = null,
        string? causationId = null,
        long? approvedByUserId = null,
        DateTimeOffset? approvedAt = null,
        CancellationToken ct = default)
    {
        var affected = await db.ExecuteAsync(
            @"UPDATE job_charges SET
                job_id=COALESCE(@jobId, job_id),
                trip_id=COALESCE(@tripId, trip_id),
                rate_card_id=COALESCE(@rateCardId, rate_card_id),
                charge_code=COALESCE(@chargeCode, charge_code),
                charge_name=COALESCE(@chargeName, charge_name),
                charge_type=COALESCE(@chargeType, charge_type),
                description=COALESCE(@description, description),
                quantity=COALESCE(@quantity, quantity),
                unit_rate=COALESCE(@unitRate, unit_rate),
                amount=COALESCE(@amount, amount),
                currency=COALESCE(@currency, currency),
                status=COALESCE(@status, status),
                correlation_id=COALESCE(@correlationId, correlation_id),
                causation_id=COALESCE(@causationId, causation_id),
                approved_by_user_id=COALESCE(@approvedByUserId, approved_by_user_id),
                approved_at=COALESCE(@approvedAt, approved_at),
                updated_at=NOW()
              WHERE id=@id AND company_id=@companyId",
            c =>
            {
                c.Parameters.AddWithValue("@id", id);
                c.Parameters.AddWithValue("@companyId", companyId);
                c.Parameters.AddWithValue("@jobId", (object?)jobId ?? DBNull.Value);
                c.Parameters.AddWithValue("@tripId", (object?)tripId ?? DBNull.Value);
                c.Parameters.AddWithValue("@rateCardId", (object?)rateCardId ?? DBNull.Value);
                c.Parameters.AddWithValue("@chargeCode", (object?)chargeCode ?? DBNull.Value);
                c.Parameters.AddWithValue("@chargeName", (object?)chargeName ?? DBNull.Value);
                c.Parameters.AddWithValue("@chargeType", (object?)chargeType ?? DBNull.Value);
                c.Parameters.AddWithValue("@description", (object?)description ?? DBNull.Value);
                c.Parameters.AddWithValue("@quantity", (object?)quantity ?? DBNull.Value);
                c.Parameters.AddWithValue("@unitRate", (object?)unitRate ?? DBNull.Value);
                c.Parameters.AddWithValue("@amount", (object?)amount ?? DBNull.Value);
                c.Parameters.AddWithValue("@currency", (object?)currency ?? DBNull.Value);
                c.Parameters.AddWithValue("@status", (object?)status ?? DBNull.Value);
                c.Parameters.AddWithValue("@correlationId", (object?)correlationId ?? DBNull.Value);
                c.Parameters.AddWithValue("@causationId", (object?)causationId ?? DBNull.Value);
                c.Parameters.AddWithValue("@approvedByUserId", (object?)approvedByUserId ?? DBNull.Value);
                c.Parameters.AddWithValue("@approvedAt", (object?)approvedAt ?? DBNull.Value);
            }, ct);

        return affected > 0 ? await LoadJobChargeByIdAsync(companyId, id, ct) : null;
    }

    private async Task<BusinessSurfaceProfileRecord?> LoadProfileAsync(long companyId, CancellationToken ct)
    {
        var row = await db.QuerySingleAsync("SELECT * FROM business_surface_profiles WHERE company_id=@companyId LIMIT 1",
            c => c.Parameters.AddWithValue("@companyId", companyId), ct);
        return row is null ? null : MapProfile(row);
    }

    private async Task<BusinessSurfaceProfileRecord?> LoadProfileByIdAsync(long id, CancellationToken ct)
    {
        var row = await db.QuerySingleAsync("SELECT * FROM business_surface_profiles WHERE id=@id LIMIT 1",
            c => c.Parameters.AddWithValue("@id", id), ct);
        return row is null ? null : MapProfile(row);
    }

    private async Task<RateCardRecord?> LoadRateCardByIdAsync(long companyId, long id, CancellationToken ct)
    {
        var includeLegacyRates = await TableExistsAsync("contract_rates", ct);
        var sql = @"
            SELECT 'rate_cards' AS source_table, rc.*
            FROM rate_cards rc
            WHERE rc.id=@id AND rc.company_id=@companyId";
        if (includeLegacyRates)
        {
            sql += @"
            UNION ALL
            SELECT 'contract_rates' AS source_table,
                   cr.id,
                   cr.company_id,
                   NULL::BIGINT AS customer_id,
                   cr.contract_id,
                   cr.rate_code AS rate_card_code,
                   cr.rate_code AS rate_card_name,
                   cr.rate_type AS billing_basis,
                   cr.accessorial_type AS service_scope,
                   cr.origin_zone,
                   cr.destination_zone,
                   cr.vehicle_type,
                   COALESCE(cr.currency, 'USD') AS currency,
                   cr.base_rate,
                   cr.minimum_charge,
                   cr.fuel_surcharge_percent,
                   cr.accessorial_type,
                   cr.effective_date,
                   cr.expiry_date,
                   cr.status,
                   NULL::VARCHAR(120) AS correlation_id,
                   NULL::VARCHAR(120) AS causation_id,
                   NULL::TEXT AS notes,
                   cr.created_at,
                   cr.updated_at
            FROM contract_rates cr
            WHERE cr.id=@id AND cr.company_id=@companyId";
        }
        sql += " LIMIT 1";

        var row = await db.QuerySingleAsync(sql, c =>
        {
            c.Parameters.AddWithValue("@id", id);
            c.Parameters.AddWithValue("@companyId", companyId);
        }, ct);
        return row is null ? null : MapRateCard(row);
    }

    private async Task<JobChargeRecord?> LoadJobChargeByIdAsync(long companyId, long id, CancellationToken ct)
    {
        var row = await db.QuerySingleAsync("SELECT * FROM job_charges WHERE id=@id AND company_id=@companyId LIMIT 1",
            c =>
            {
                c.Parameters.AddWithValue("@id", id);
                c.Parameters.AddWithValue("@companyId", companyId);
            }, ct);
        return row is null ? null : MapJobCharge(row);
    }

    private static BusinessSurfaceProfileRecord MapProfile(Dictionary<string, object?> row) => new(
        L(row, "id"),
        L(row, "companyId"),
        S(row, "verticalKey") ?? "generic",
        S(row, "customerLabelSingular") ?? "Customer",
        S(row, "customerLabelPlural") ?? "Customers",
        S(row, "contractLabelSingular") ?? "Contract",
        S(row, "contractLabelPlural") ?? "Contracts",
        S(row, "rateCardLabelSingular") ?? "Rate Card",
        S(row, "rateCardLabelPlural") ?? "Rate Cards",
        S(row, "jobLabelSingular") ?? "Job",
        S(row, "jobLabelPlural") ?? "Jobs",
        S(row, "tripLabelSingular") ?? "Trip",
        S(row, "tripLabelPlural") ?? "Trips",
        S(row, "chargeLabelSingular") ?? "Charge",
        S(row, "chargeLabelPlural") ?? "Charges",
        B(row, "useGenericLabels", true),
        S(row, "notes"),
        Dto(row, "createdAt"),
        DtoN(row, "updatedAt"));

    private static RateCardRecord MapRateCard(Dictionary<string, object?> row) => new(
        L(row, "id"),
        L(row, "companyId"),
        LN(row, "customerId"),
        LN(row, "contractId"),
        S(row, "sourceTable") ?? "rate_cards",
        S(row, "rateCardCode") ?? string.Empty,
        S(row, "rateCardName") ?? string.Empty,
        S(row, "billingBasis") ?? "Per Unit",
        S(row, "serviceScope"),
        S(row, "originZone"),
        S(row, "destinationZone"),
        S(row, "vehicleType"),
        S(row, "currency") ?? "USD",
        D(row, "baseRate"),
        DN(row, "minimumCharge"),
        DN(row, "fuelSurchargePercent"),
        S(row, "accessorialType"),
        DateOnly.FromDateTime(Dto(row, "effectiveDate").UtcDateTime),
        DateOnlyN(row, "expiryDate"),
        S(row, "status") ?? "Active",
        S(row, "correlationId"),
        S(row, "causationId"),
        S(row, "notes"),
        Dto(row, "createdAt"),
        DtoN(row, "updatedAt"));

    private static JobChargeRecord MapJobCharge(Dictionary<string, object?> row) => new(
        L(row, "id"),
        L(row, "companyId"),
        L(row, "jobId"),
        LN(row, "tripId"),
        LN(row, "rateCardId"),
        S(row, "chargeCode") ?? string.Empty,
        S(row, "chargeName") ?? string.Empty,
        S(row, "chargeType") ?? "base",
        S(row, "description"),
        D(row, "quantity"),
        D(row, "unitRate"),
        D(row, "amount"),
        S(row, "currency") ?? "USD",
        S(row, "status") ?? "pending",
        S(row, "correlationId"),
        S(row, "causationId"),
        LN(row, "approvedByUserId"),
        DtoN(row, "approvedAt"),
        Dto(row, "createdAt"),
        DtoN(row, "updatedAt"));

    private static string Normalize(string? value, string fallback)
        => string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();

    private async Task<bool> TableExistsAsync(string tableName, CancellationToken ct)
    {
        var row = await db.QuerySingleAsync(
            "SELECT to_regclass(@qualified_name) IS NOT NULL AS table_exists",
            c => c.Parameters.AddWithValue("@qualified_name", $"public.{tableName}"),
            ct);
        return row is not null && row.TryGetValue("table_exists", out var existsValue) && existsValue is bool exists && exists;
    }

    private static string? S(Dictionary<string, object?> row, string key)
        => row.TryGetValue(key, out var value) && value is not null and not DBNull ? Convert.ToString(value, CultureInfo.InvariantCulture) : null;

    private static long L(Dictionary<string, object?> row, string key)
        => row.TryGetValue(key, out var value) && value is not null and not DBNull ? Convert.ToInt64(value, CultureInfo.InvariantCulture) : 0;

    private static long? LN(Dictionary<string, object?> row, string key)
        => row.TryGetValue(key, out var value) && value is not null and not DBNull ? Convert.ToInt64(value, CultureInfo.InvariantCulture) : null;

    private static decimal D(Dictionary<string, object?> row, string key)
        => row.TryGetValue(key, out var value) && value is not null and not DBNull ? Convert.ToDecimal(value, CultureInfo.InvariantCulture) : 0m;

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
