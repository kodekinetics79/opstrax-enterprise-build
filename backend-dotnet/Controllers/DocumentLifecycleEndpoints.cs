using System.Globalization;
using System.Text.Json;
using Npgsql;
using NpgsqlTypes;
using Opstrax.Api.Data;
using Opstrax.Api.DTOs;
using Opstrax.Api.Services;

namespace Opstrax.Api.Controllers;

public static partial class EndpointMappings
{
    private static Dictionary<string, object?> DocumentBody(JsonElement json)
    {
        var body = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var property in json.EnumerateObject()) body[property.Name] = property.Value.Clone();
        return body;
    }

    private static Task<IResult> DocumentJsonMutation(HttpContext http, JsonElement json, DocumentWriteKind kind,
        Func<Dictionary<string, object?>, Task<IResult>> handler)
    {
        if (RequirePermission(http, "compliance:manage") is { } denied) return Task.FromResult(denied);
        var errors = DocumentLifecyclePolicy.ValidateBoundary(json, kind);
        return errors.Count > 0
            ? Task.FromResult<IResult>(Results.BadRequest(ApiResponse<object>.Fail("Document validation failed", errors.ToArray())))
            : handler(DocumentBody(json));
    }

    private static DateOnly? DocumentDate(object? value) => value switch
    {
        null or DBNull => null,
        DateOnly date => date,
        DateTime date => DateOnly.FromDateTime(date),
        _ when DateTime.TryParse(value.ToString(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed) => DateOnly.FromDateTime(parsed),
        _ => null
    };

    private static DocumentLifecycleSnapshot DocumentSnapshot(Dictionary<string, object?> row)
        => new(row["lifecycleMode"]?.ToString() ?? "legacy_unknown", row["status"]?.ToString() ?? "Unknown",
            row.GetValueOrDefault("riskScore") is { } risk ? Convert.ToDecimal(risk, CultureInfo.InvariantCulture) : null,
            row["renewalStatus"]?.ToString() ?? "Unknown", row.GetValueOrDefault("recommendedAction")?.ToString(),
            DocumentDate(row.GetValueOrDefault("lifecycleAssessedOn")), DocumentDate(row.GetValueOrDefault("expiresAt")));

    private static void AddDocumentAssessment(Dictionary<string, object?> row, DateOnly today)
        => row["currentDateAssessment"] = DocumentLifecyclePolicy.Assess(DocumentDate(row.GetValueOrDefault("expiresAt")), today);

    private static async Task<IResult> DocumentRows(Database db, string sql, Action<NpgsqlCommand> bind, DateOnly today, CancellationToken ct)
    {
        var rows = await db.QueryAsync(sql, bind, ct);
        foreach (var row in rows) AddDocumentAssessment(row, today);
        return Results.Ok(ApiResponse<object>.Ok(rows));
    }

    private static async Task<IResult> ExecuteDocumentMutation(HttpContext http, Database db, Func<Task<IResult>> body,
        CancellationToken ct, Func<CancellationToken, Task>? compensate = null)
    {
        try { return await db.RunInDocumentTransactionAsync(GetCompanyId(http), body, compensate, ct); }
        catch (DocumentLifecycleException error)
        {
            return Results.Json(ApiResponse<object>.Fail(error.Message), statusCode: error.StatusCode);
        }
        catch (DocumentTransactionUncertainException error)
        {
            LogSafeEndpointFailure(http, error, "document.reconciliation_required");
            return Results.Json(ApiResponse<object>.Fail(error.Message), statusCode: 503);
        }
    }

    private static async Task<Dictionary<string, object?>> LockDocument(HttpContext http, long id, Database db, CancellationToken ct)
        => await db.QuerySingleAsync("SELECT d.*, d.xmin::text row_version FROM documents d WHERE d.id=@id AND d.company_id=@cid AND d.deleted_at IS NULL"
            + DocumentBranchScopeSql + " FOR UPDATE OF d", c => { c.Parameters.AddWithValue("@id", id); BindDocumentScope(c, http); }, ct)
            ?? throw new DocumentLifecycleException(404, "Document not found");

    private static async Task LockDocumentOwners(HttpContext http, Database db,
        IEnumerable<(string Type, long Id)> owners, CancellationToken ct, bool allowHistoricalOwner = false)
    {
        foreach (var owner in owners.Distinct().OrderBy(value => value.Type, StringComparer.Ordinal).ThenBy(value => value.Id))
        {
            var table = owner.Type switch
            {
                "vehicle" => "vehicles", "driver" => "drivers", "asset" => "fleet_tms_assets", "customer" => "customers",
                _ => throw new DocumentLifecycleException(400, "Choose a valid document owner.")
            };
            var branchId = GetBranchId(http);
            if (branchId is not null && owner.Type == "customer") throw new DocumentLifecycleException(404, "Document not found");
            var row = await db.QuerySingleAsync($"SELECT id FROM {table} WHERE id=@ownerId AND company_id=@cid"
                + (owner.Type is "vehicle" or "driver" && !(allowHistoricalOwner && branchId is null) ? " AND deleted_at IS NULL" : "")
                + (branchId is null ? "" : " AND branch_id=@branchId") + " FOR SHARE", c =>
                {
                    c.Parameters.AddWithValue("@ownerId", owner.Id);
                    BindDocumentScope(c, http);
                }, ct);
            if (row is null && !(allowHistoricalOwner && branchId is null))
                throw new DocumentLifecycleException(404, "The document owner is not available in your tenant and branch.");
        }
    }

    private static (string Type, long Id) DocumentOwner(Dictionary<string, object?> body, Dictionary<string, object?>? existing = null)
    {
        var type = !IsBlank(Get(body, "entityType")) ? Get(body, "entityType")?.ToString()?.Trim().ToLowerInvariant() : existing?.GetValueOrDefault("entityType")?.ToString();
        var id = !IsBlank(Get(body, "entityId")) ? ToNullableLong(Get(body, "entityId")?.ToString()) : ToNullableLong(existing?.GetValueOrDefault("entityId")?.ToString());
        if (type is not ("vehicle" or "driver" or "asset") || id is null or <= 0)
            throw new DocumentLifecycleException(400, "Choose a valid vehicle, driver, or asset.");
        return (type, id.Value);
    }

    private static void CheckDocumentDates(Dictionary<string, object?> body, Dictionary<string, object?>? existing = null)
    {
        var errors = ValidateDocumentDateFields(body);
        if (errors.Count > 0) throw new DocumentLifecycleException(400, errors[0]);
        NormalizeDocumentDates(body);
        var issued = DocumentDate(!IsBlank(Get(body, "issuedAt")) ? Get(body, "issuedAt") : existing?.GetValueOrDefault("issuedAt"));
        var expires = DocumentDate(!IsBlank(Get(body, "expiresAt")) ? Get(body, "expiresAt") : existing?.GetValueOrDefault("expiresAt"));
        if (issued is not null && expires is not null && expires < issued)
            throw new DocumentLifecycleException(400, "Document expiry date cannot be before issued date.");
    }

    private static void BindDocumentLifecycle(NpgsqlCommand command, DocumentLifecycleSnapshot snapshot)
    {
        command.Parameters.AddWithValue("@mode", snapshot.Mode);
        command.Parameters.AddWithValue("@assessed", NpgsqlDbType.Date, (object?)snapshot.AssessedOn ?? DBNull.Value);
        command.Parameters.AddWithValue("@lstatus", snapshot.Status);
        command.Parameters.AddWithValue("@lrisk", NpgsqlDbType.Numeric, (object?)snapshot.RiskScore ?? DBNull.Value);
        command.Parameters.AddWithValue("@lrenewal", snapshot.RenewalStatus);
        command.Parameters.AddWithValue("@laction", (object?)snapshot.RecommendedAction ?? DBNull.Value);
    }

    private static async Task AuditDocumentLifecycle(HttpContext http, Database db, AuditService audit, long id,
        string action, string eventTitle, DocumentLifecycleSnapshot? old, DocumentLifecycleChange change,
        string? oldVersion, string newVersion, DateOnly today, CancellationToken ct, object? file = null)
    {
        await audit.LogAsync(http, action, "Document", id, JsonSerializer.Serialize(new
        {
            lifecycleIntent = change.Intent, policyVersion = DocumentLifecyclePolicy.PolicyVersion,
            oldSnapshot = old, newSnapshot = change.Snapshot, oldVersion, returnedVersion = newVersion,
            assessmentDate = today, reason = change.Reason, replaceQueuedRenewal = change.ReplaceQueuedRenewal, file
        }), ct: ct);
        await AddDocumentEvent(db, GetCompanyId(http), id, eventTitle,
            $"Document lifecycle intent: {change.Intent}; policy: {DocumentLifecyclePolicy.PolicyVersion}", ct);
    }

    private static Task<IResult> CreateLifecycleDocument(HttpContext http, Dictionary<string, object?> body,
        Database db, AuditService audit, CancellationToken ct)
        => ExecuteDocumentMutation(http, db, async () =>
        {
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            var errors = DocumentLifecyclePolicy.ValidateBoundary(JsonSerializer.SerializeToElement(body), DocumentWriteKind.Create);
            if (errors.Count > 0) throw new DocumentLifecycleException(400, errors[0]);
            RemoveCustomerDocumentFileReference(body);
            CheckDocumentDates(body);
            var owner = DocumentOwner(body);
            await LockDocumentOwners(http, db, [owner], ct);
            body["entityType"] = owner.Type; body["entityId"] = owner.Id;
            var assessment = DocumentLifecyclePolicy.Assess(DocumentDate(Get(body, "expiresAt")), today);
            var snapshot = new DocumentLifecycleSnapshot("automatic", assessment.Status, assessment.RiskScore,
                assessment.RenewalStatus, assessment.RecommendedAction, today, DocumentDate(Get(body, "expiresAt")));
            var row = await InsertLifecycleDocument(http, db, body, snapshot, null, ct);
            var id = Convert.ToInt64(row["id"]);
            var version = row["rowVersion"]!.ToString()!;
            await AuditDocumentLifecycle(http, db, audit, id, "document.created", "Document created", null,
                new(snapshot, "create", null, false), null, version, today, ct);
            return Results.Created($"/api/documents/{id}", ApiResponse<object>.Ok(new { id, rowVersion = version }, "Document created"));
        }, ct);

    private static async Task<Dictionary<string, object?>> InsertLifecycleDocument(HttpContext http, Database db,
        Dictionary<string, object?> body, DocumentLifecycleSnapshot snapshot, string? fileReference, CancellationToken ct)
        => await db.QuerySingleAsync(@"INSERT INTO documents
            (company_id,title,document_number,entity_type,entity_id,document_type,category,country_code,issuing_authority,
             issued_at,expires_at,status,renewal_status,risk_score,recommended_action,notes,file_url,lifecycle_mode,lifecycle_assessed_on)
            VALUES (@cid,@title,@number,@entityType,@entityId,@type,@category,@country,@authority,@issued,@expires,
                    @lstatus,@lrenewal,@lrisk,@laction,@notes,@file,@mode,@assessed)
            RETURNING id,xmin::text row_version", c =>
            {
                c.Parameters.AddWithValue("@cid", GetCompanyId(http)); BindDocument(c, body);
                BindDocumentLifecycle(c, snapshot); c.Parameters.AddWithValue("@file", (object?)fileReference ?? DBNull.Value);
            }, ct) ?? throw new InvalidOperationException("Document insert returned no identity.");

    private static Task<IResult> UpdateLifecycleDocument(HttpContext http, long id, Dictionary<string, object?> body,
        Database db, AuditService audit, CancellationToken ct, bool renew = false)
        => ExecuteDocumentMutation(http, db, async () =>
        {
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            var existing = await LockDocument(http, id, db, ct);
            var errors = DocumentLifecyclePolicy.ValidateBoundary(JsonSerializer.SerializeToElement(body), renew ? DocumentWriteKind.Renew : DocumentWriteKind.Update);
            if (errors.Count > 0) throw new DocumentLifecycleException(400, errors[0]);
            var version = DocumentLifecyclePolicy.RequireExpectedVersion(body);
            if (version != existing["rowVersion"]?.ToString())
                throw new DocumentLifecycleException(409, "The document changed. Reload its current status before retrying.");
            RemoveCustomerDocumentFileReference(body);
            CheckDocumentDates(body, existing);
            var oldOwner = (existing["entityType"]?.ToString() ?? "", Convert.ToInt64(existing["entityId"]));
            var owner = renew ? oldOwner : DocumentOwner(body, existing);
            await LockDocumentOwners(http, db, [oldOwner, owner], ct);
            if (renew) body = new Dictionary<string, object?>(); // Queue accepts no metadata changes.
            body["entityType"] = owner.Item1; body["entityId"] = owner.Item2;
            var old = DocumentSnapshot(existing);
            var effectiveExpiry = !IsBlank(Get(body, "expiresAt")) ? DocumentDate(Get(body, "expiresAt")) : old.ExpiresAt;
            var change = renew ? DocumentLifecyclePolicy.QueueRenewal(old) : DocumentLifecyclePolicy.ApplyUpdate(old, effectiveExpiry, body, today);
            var row = await db.QuerySingleAsync(@"UPDATE documents d SET
                title=COALESCE(@title,d.title),document_number=COALESCE(@number,d.document_number),entity_type=@entityType,entity_id=@entityId,
                document_type=COALESCE(@type,d.document_type),category=COALESCE(@category,d.category),country_code=COALESCE(@country,d.country_code),
                issuing_authority=COALESCE(@authority,d.issuing_authority),issued_at=COALESCE(@issued,d.issued_at),expires_at=COALESCE(@expires,d.expires_at),
                status=@lstatus,renewal_status=@lrenewal,risk_score=@lrisk,recommended_action=@laction,notes=COALESCE(@notes,d.notes),
                lifecycle_mode=@mode,lifecycle_assessed_on=@assessed
                WHERE d.id=@id AND d.company_id=@cid AND d.deleted_at IS NULL AND d.xmin::text=@version"
                + DocumentBranchScopeSql + " RETURNING d.*, d.xmin::text row_version", c =>
                {
                    c.Parameters.AddWithValue("@id", id); c.Parameters.AddWithValue("@version", version); BindDocumentScope(c, http);
                    BindDocument(c, body, generateDocumentNumber: false); BindDocumentLifecycle(c, change.Snapshot);
                }, ct);
            if (row is null) throw new DocumentLifecycleException(409, "The document changed. Reload its current status before retrying.");
            var nextVersion = row["rowVersion"]!.ToString()!;
            // Audit persisted numeric precision, not an unrounded client-side decimal.
            change = change with { Snapshot = DocumentSnapshot(row) };
            await AuditDocumentLifecycle(http, db, audit, id, renew ? "document.renewal.queued" : "document.updated",
                renew ? "Renewal queued" : "Document updated", old, change, version, nextVersion, today, ct);
            return Results.Ok(ApiResponse<object>.Ok(new { id, rowVersion = nextVersion }, renew ? "Document renewal queued" : "Document updated"));
        }, ct);

    private static Task<IResult> DeleteLifecycleDocument(HttpContext http, long id, Database db, AuditService audit, CancellationToken ct)
        => ExecuteDocumentMutation(http, db, async () =>
        {
            var existing = await LockDocument(http, id, db, ct);
            var ownerType = existing.GetValueOrDefault("entityType")?.ToString();
            var ownerId = ToNullableLong(existing.GetValueOrDefault("entityId")?.ToString());
            // Tenant-wide historical documents can have no typed owner. Preserve
            // their existing archive behavior; branch-bound access was already
            // excluded by LockDocument's unchanged typed-ownership predicate.
            if (ownerType is "vehicle" or "driver" or "asset" or "customer" && ownerId is > 0)
                await LockDocumentOwners(http, db, [(ownerType, ownerId.Value)], ct, allowHistoricalOwner: true);
            var row = await db.QuerySingleAsync("UPDATE documents d SET deleted_at=NOW() WHERE d.id=@id AND d.company_id=@cid AND d.deleted_at IS NULL"
                + DocumentBranchScopeSql + " RETURNING d.xmin::text row_version", c => { c.Parameters.AddWithValue("@id", id); BindDocumentScope(c, http); }, ct);
            if (row is null) throw new DocumentLifecycleException(404, "Document not found");
            await audit.LogAsync(http, "document.deleted", "Document", id, JsonSerializer.Serialize(new
            {
                lifecycleIntent = "delete", oldVersion = existing["rowVersion"], returnedVersion = row["rowVersion"]
            }), ct: ct);
            await AddDocumentEvent(db, GetCompanyId(http), id, "Document deleted", "Document archived from active vault", ct);
            return Results.Ok(ApiResponse<object>.Ok(new { id, rowVersion = row["rowVersion"] }, "Document deleted"));
        }, ct);

    private static async Task<IResult> UploadLifecycleDocument(HttpContext http, Opstrax.Api.Storage.FileStorageService files,
        Database db, AuditService audit, CancellationToken ct)
    {
        if (RequirePermission(http, "compliance:manage") is { } denied) return denied;
        if (!http.Request.HasFormContentType)
            return Results.BadRequest(ApiResponse<object>.Fail("multipart/form-data with a 'file' field is required"));
        var form = await http.Request.ReadFormAsync(ct);
        var errors = DocumentLifecyclePolicy.ValidateFormBoundary(form, DocumentWriteKind.Create);
        if (errors.Count > 0) return Results.BadRequest(ApiResponse<object>.Fail("Document validation failed", errors.ToArray()));
        var file = form.Files["file"] ?? form.Files.FirstOrDefault();
        if (file is null || file.Length == 0) return Results.BadRequest(ApiResponse<object>.Fail("No file uploaded"));
        Opstrax.Api.Storage.FileStorageService.UploadResult? stored = null;
        return await ExecuteDocumentMutation(http, db, async () =>
        {
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            var body = form.ToDictionary(field => field.Key, field => (object?)field.Value.FirstOrDefault(), StringComparer.Ordinal);
            RemoveCustomerDocumentFileReference(body);
            CheckDocumentDates(body);
            var owner = DocumentOwner(body);
            await LockDocumentOwners(http, db, [owner], ct);
            body["entityType"] = owner.Type; body["entityId"] = owner.Id;
            if (Get(body, "title") is DBNull) body["title"] = file.FileName;
            if (Get(body, "documentType") is DBNull) body["documentType"] = "General";
            if (Get(body, "category") is DBNull) body["category"] = "Uploaded";
            var assessment = DocumentLifecyclePolicy.Assess(DocumentDate(Get(body, "expiresAt")), today);
            var snapshot = new DocumentLifecycleSnapshot("automatic", assessment.Status, assessment.RiskScore,
                assessment.RenewalStatus, assessment.RecommendedAction, today, DocumentDate(Get(body, "expiresAt")));
            try
            {
                await using var stream = file.OpenReadStream();
                stored = await files.UploadAsync(GetCompanyId(http), "documents", file.FileName,
                    file.ContentType ?? "application/octet-stream", stream, ct);
            }
            catch (ArgumentException error)
            {
                LogSafeEndpointFailure(http, error, "document.upload");
                throw new DocumentLifecycleException(400, "Upload rejected");
            }
            if (Get(body, "notes") is DBNull) body["notes"] = $"Uploaded {stored.Size} bytes ({stored.ContentType})";
            var row = await InsertLifecycleDocument(http, db, body, snapshot, stored.Reference, ct);
            var id = Convert.ToInt64(row["id"]);
            var version = row["rowVersion"]!.ToString()!;
            await AuditDocumentLifecycle(http, db, audit, id, "document.uploaded", "Document uploaded", null,
                new(snapshot, "upload", null, false), null, version, today, ct,
                new { size = stored.Size, contentType = stored.ContentType, provider = files.Provider });
            return Results.Created($"/api/documents/{id}", ApiResponse<object>.Ok(
                new { id, rowVersion = version, size = stored.Size, contentType = stored.ContentType }, "Document uploaded"));
        }, ct, async cleanupToken =>
        {
            // Called only after the fresh transaction acknowledged rollback before commit.
            if (stored is not null) await files.DeleteAsync(stored.Reference, cleanupToken);
        });
    }
}
