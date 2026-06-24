using Opstrax.Api.Data;

namespace Opstrax.Api.Services;

public sealed class Batch5SchemaService(Database db)
{
    public async Task EnsureAsync(CancellationToken ct = default)
    {
        foreach (var col in Columns) await EnsureColumnAsync(col.Table, col.Name, col.Definition, ct);
        foreach (var sql in Tables) await db.ExecuteAsync(sql, ct: ct);
        foreach (var sql in Indexes) { try { await db.ExecuteAsync(sql, ct: ct); } catch { } }
        foreach (var sql in Seeds) await db.ExecuteAsync(sql, ct: ct);
    }

    private async Task EnsureColumnAsync(string table, string column, string definition, CancellationToken ct)
    {
        var exists = await db.ScalarLongAsync(
            @"SELECT COUNT(*) FROM information_schema.columns WHERE table_schema=current_schema() AND table_name=@table AND column_name=@column",
            c => { c.Parameters.AddWithValue("@table", table); c.Parameters.AddWithValue("@column", column); }, ct);
        if (exists == 0) await db.ExecuteAsync($"ALTER TABLE {table} ADD COLUMN {column} {definition}", ct: ct);
    }

    private sealed record ColumnDefinition(string Table, string Name, string Definition);

    private static readonly ColumnDefinition[] Columns =
    [
        new("fuel_transactions", "transaction_number",  "VARCHAR(80) NULL"),
        new("fuel_transactions", "driver_id",           "BIGINT NULL"),
        new("fuel_transactions", "job_id",              "BIGINT NULL"),
        new("fuel_transactions", "route_id",            "BIGINT NULL"),
        new("fuel_transactions", "fuel_date",           "DATE NULL"),
        new("fuel_transactions", "fuel_type",           "VARCHAR(80) NOT NULL DEFAULT 'Diesel'"),
        new("fuel_transactions", "quantity",            "DECIMAL(10,3) NOT NULL DEFAULT 0"),
        new("fuel_transactions", "unit",                "VARCHAR(30) NOT NULL DEFAULT 'Gallons'"),
        new("fuel_transactions", "unit_price",          "DECIMAL(10,4) NOT NULL DEFAULT 0"),
        new("fuel_transactions", "currency",            "VARCHAR(10) NOT NULL DEFAULT 'USD'"),
        new("fuel_transactions", "odometer",            "DECIMAL(12,2) NULL"),
        new("fuel_transactions", "payment_method",      "VARCHAR(80) NOT NULL DEFAULT 'Fleet Card'"),
        new("fuel_transactions", "fuel_card_number",    "VARCHAR(80) NULL"),
        new("fuel_transactions", "region",              "VARCHAR(120) NULL"),
        new("fuel_transactions", "anomaly_status",      "VARCHAR(80) NOT NULL DEFAULT 'Normal'"),
        new("fuel_transactions", "recommended_action",  "VARCHAR(260) NULL"),
        new("fuel_transactions", "notes",               "TEXT NULL"),
        new("fuel_transactions", "updated_at",          "TIMESTAMPTZ NULL"),
        new("fuel_transactions", "deleted_at",          "TIMESTAMPTZ NULL"),

        new("expenses", "expense_number",       "VARCHAR(80) NULL"),
        new("expenses", "category_id",          "BIGINT NULL"),
        new("expenses", "category_name",        "VARCHAR(120) NULL"),
        new("expenses", "currency",             "VARCHAR(10) NOT NULL DEFAULT 'USD'"),
        new("expenses", "vehicle_id",           "BIGINT NULL"),
        new("expenses", "driver_id",            "BIGINT NULL"),
        new("expenses", "job_id",               "BIGINT NULL"),
        new("expenses", "route_id",             "BIGINT NULL"),
        new("expenses", "customer_id",          "BIGINT NULL"),
        new("expenses", "carrier_id",           "BIGINT NULL"),
        new("expenses", "vendor_name",          "VARCHAR(180) NULL"),
        new("expenses", "approval_status",      "VARCHAR(80) NOT NULL DEFAULT 'Pending'"),
        new("expenses", "receipt_status",       "VARCHAR(80) NOT NULL DEFAULT 'Missing'"),
        new("expenses", "document_id",          "BIGINT NULL"),
        new("expenses", "risk_score",           "DECIMAL(6,2) NOT NULL DEFAULT 20"),
        new("expenses", "recommended_action",   "VARCHAR(260) NULL"),
        new("expenses", "notes",                "TEXT NULL"),
        new("expenses", "updated_at",           "TIMESTAMPTZ NULL"),
        new("expenses", "deleted_at",           "TIMESTAMPTZ NULL"),

        new("carriers", "carrier_number",       "VARCHAR(80) NULL"),
        new("carriers", "contact_name",         "VARCHAR(160) NULL"),
        new("carriers", "phone",                "VARCHAR(50) NULL"),
        new("carriers", "email",                "VARCHAR(220) NULL"),
        new("carriers", "region",               "VARCHAR(120) NULL"),
        new("carriers", "compliance_status",    "VARCHAR(80) NOT NULL DEFAULT 'Compliant'"),
        new("carriers", "insurance_expiry",     "DATE NULL"),
        new("carriers", "contract_status",      "VARCHAR(80) NOT NULL DEFAULT 'Active'"),
        new("carriers", "on_time_percent",      "DECIMAL(6,2) NOT NULL DEFAULT 90"),
        new("carriers", "safety_score",         "DECIMAL(6,2) NOT NULL DEFAULT 88"),
        new("carriers", "cost_score",           "DECIMAL(6,2) NOT NULL DEFAULT 82"),
        new("carriers", "performance_score",    "DECIMAL(6,2) NOT NULL DEFAULT 86"),
        new("carriers", "risk_score",           "DECIMAL(6,2) NOT NULL DEFAULT 20"),
        new("carriers", "recommended_action",   "VARCHAR(260) NULL"),
        new("carriers", "notes",                "TEXT NULL"),
        new("carriers", "updated_at",           "TIMESTAMPTZ NULL"),
        new("carriers", "deleted_at",           "TIMESTAMPTZ NULL"),

        new("contracts", "contract_number",         "VARCHAR(80) NULL"),
        new("contracts", "carrier_id",              "BIGINT NULL"),
        new("contracts", "contract_type",           "VARCHAR(80) NOT NULL DEFAULT 'Customer'"),
        new("contracts", "expiry_date",             "DATE NULL"),
        new("contracts", "currency",                "VARCHAR(10) NOT NULL DEFAULT 'USD'"),
        new("contracts", "base_rate",               "DECIMAL(12,4) NOT NULL DEFAULT 0"),
        new("contracts", "fuel_surcharge_enabled",  "BOOLEAN NOT NULL DEFAULT FALSE"),
        new("contracts", "fuel_surcharge_percent",  "DECIMAL(6,2) NULL"),
        new("contracts", "sla_terms",               "TEXT NULL"),
        new("contracts", "margin_risk",             "VARCHAR(50) NOT NULL DEFAULT 'Low'"),
        new("contracts", "notes",                   "TEXT NULL"),
        new("contracts", "updated_at",              "TIMESTAMPTZ NULL"),
        new("contracts", "deleted_at",              "TIMESTAMPTZ NULL"),

        new("audit_logs", "entity_type",            "VARCHAR(100) NULL"),
        new("idling_events", "threshold_status",     "VARCHAR(80) NOT NULL DEFAULT 'Normal'"),
        new("idling_events", "risk_score",           "DECIMAL(6,2) NOT NULL DEFAULT 20"),
        new("idling_events", "recommended_action",   "VARCHAR(260) NULL")
    ];

    private static readonly string[] Tables =
    [
        @"CREATE TABLE IF NOT EXISTS idling_events (
            id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY, company_id BIGINT NOT NULL DEFAULT 1,
            event_number VARCHAR(80) NOT NULL, vehicle_id BIGINT NOT NULL, driver_id BIGINT NULL,
            job_id BIGINT NULL, route_id BIGINT NULL, location_description VARCHAR(220) NULL,
            started_at TIMESTAMPTZ NOT NULL DEFAULT NOW(), ended_at TIMESTAMPTZ NULL,
            duration_minutes DECIMAL(8,2) NOT NULL DEFAULT 0, estimated_fuel_burn DECIMAL(10,3) NOT NULL DEFAULT 0,
            estimated_cost DECIMAL(12,2) NOT NULL DEFAULT 0, currency VARCHAR(10) NOT NULL DEFAULT 'USD',
            threshold_status VARCHAR(80) NOT NULL DEFAULT 'Normal', risk_score DECIMAL(6,2) NOT NULL DEFAULT 20,
            recommended_action VARCHAR(260) NULL,
            created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
            updated_at TIMESTAMPTZ NULL)",

        @"CREATE TABLE IF NOT EXISTS fuel_anomalies (
            id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY, company_id BIGINT NOT NULL DEFAULT 1,
            fuel_transaction_id BIGINT NULL, vehicle_id BIGINT NULL, driver_id BIGINT NULL,
            anomaly_type VARCHAR(120) NOT NULL, severity VARCHAR(50) NOT NULL DEFAULT 'Medium',
            description TEXT NULL, estimated_loss DECIMAL(12,2) NOT NULL DEFAULT 0,
            status VARCHAR(80) NOT NULL DEFAULT 'Open',
            reviewed_at TIMESTAMPTZ NULL, created_at TIMESTAMPTZ NOT NULL DEFAULT NOW())",

        @"CREATE TABLE IF NOT EXISTS expense_categories (
            id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY, company_id BIGINT NOT NULL DEFAULT 1,
            category_name VARCHAR(120) NOT NULL, category_type VARCHAR(80) NOT NULL DEFAULT 'Operating',
            status VARCHAR(50) NOT NULL DEFAULT 'Active',
            created_at TIMESTAMPTZ NOT NULL DEFAULT NOW())",

        @"CREATE TABLE IF NOT EXISTS contract_rates (
            id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY, company_id BIGINT NOT NULL DEFAULT 1,
            contract_id BIGINT NOT NULL, rate_code VARCHAR(80) NOT NULL,
            rate_type VARCHAR(80) NOT NULL DEFAULT 'Per Mile', origin_zone VARCHAR(120) NULL,
            destination_zone VARCHAR(120) NULL, vehicle_type VARCHAR(80) NULL,
            base_rate DECIMAL(12,4) NOT NULL DEFAULT 0, minimum_charge DECIMAL(12,2) NULL,
            fuel_surcharge_percent DECIMAL(6,2) NULL, accessorial_type VARCHAR(120) NULL,
            effective_date DATE NOT NULL, expiry_date DATE NULL, status VARCHAR(50) NOT NULL DEFAULT 'Active',
            created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
            updated_at TIMESTAMPTZ NULL)",

        @"CREATE TABLE IF NOT EXISTS carrier_documents (
            id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY, company_id BIGINT NOT NULL DEFAULT 1,
            carrier_id BIGINT NOT NULL, document_type VARCHAR(120) NOT NULL,
            document_number VARCHAR(120) NULL, expiry_date DATE NULL,
            status VARCHAR(50) NOT NULL DEFAULT 'Active', file_url VARCHAR(400) NULL,
            created_at TIMESTAMPTZ NOT NULL DEFAULT NOW())",

        @"CREATE TABLE IF NOT EXISTS carrier_performance (
            id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY, company_id BIGINT NOT NULL DEFAULT 1,
            carrier_id BIGINT NOT NULL, period_start DATE NOT NULL, period_end DATE NOT NULL,
            jobs_handled INT NOT NULL DEFAULT 0, on_time_percent DECIMAL(6,2) NOT NULL DEFAULT 90,
            incident_count INT NOT NULL DEFAULT 0, expense_total DECIMAL(12,2) NOT NULL DEFAULT 0,
            performance_score DECIMAL(6,2) NOT NULL DEFAULT 85,
            created_at TIMESTAMPTZ NOT NULL DEFAULT NOW())",

        @"CREATE TABLE IF NOT EXISTS cost_margin_records (
            id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY, company_id BIGINT NOT NULL DEFAULT 1,
            entity_type VARCHAR(80) NOT NULL, entity_id BIGINT NOT NULL,
            customer_id BIGINT NULL, job_id BIGINT NULL, route_id BIGINT NULL,
            vehicle_id BIGINT NULL, driver_id BIGINT NULL,
            revenue_estimate DECIMAL(12,2) NOT NULL DEFAULT 0,
            fuel_cost DECIMAL(12,2) NOT NULL DEFAULT 0, driver_cost DECIMAL(12,2) NOT NULL DEFAULT 0,
            maintenance_cost DECIMAL(12,2) NOT NULL DEFAULT 0, carrier_cost DECIMAL(12,2) NOT NULL DEFAULT 0,
            expense_total DECIMAL(12,2) NOT NULL DEFAULT 0, delay_cost DECIMAL(12,2) NOT NULL DEFAULT 0,
            idle_cost DECIMAL(12,2) NOT NULL DEFAULT 0, total_cost DECIMAL(12,2) NOT NULL DEFAULT 0,
            margin_estimate DECIMAL(12,2) NOT NULL DEFAULT 0, margin_percent DECIMAL(6,2) NOT NULL DEFAULT 0,
            margin_risk VARCHAR(50) NOT NULL DEFAULT 'Low',
            calculated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
            created_at TIMESTAMPTZ NOT NULL DEFAULT NOW())",

        @"CREATE TABLE IF NOT EXISTS cost_margin_predictions (
            id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY, company_id BIGINT NOT NULL DEFAULT 1,
            entity_type VARCHAR(80) NOT NULL, entity_id BIGINT NOT NULL,
            prediction_type VARCHAR(80) NOT NULL DEFAULT 'Margin Forecast',
            predicted_revenue DECIMAL(12,2) NOT NULL DEFAULT 0, predicted_cost DECIMAL(12,2) NOT NULL DEFAULT 0,
            predicted_margin DECIMAL(12,2) NOT NULL DEFAULT 0, predicted_margin_percent DECIMAL(6,2) NOT NULL DEFAULT 0,
            confidence_level VARCHAR(50) NOT NULL DEFAULT 'Medium', risk_level VARCHAR(50) NOT NULL DEFAULT 'Low',
            recommendation TEXT NULL,
            created_at TIMESTAMPTZ NOT NULL DEFAULT NOW())",

        @"CREATE TABLE IF NOT EXISTS cost_leakage_items (
            id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY, company_id BIGINT NOT NULL DEFAULT 1,
            leakage_number VARCHAR(80) NOT NULL, category VARCHAR(120) NOT NULL,
            entity_type VARCHAR(80) NULL, entity_id BIGINT NULL, title VARCHAR(220) NOT NULL,
            description TEXT NULL, estimated_loss DECIMAL(12,2) NOT NULL DEFAULT 0,
            projected_monthly_loss DECIMAL(12,2) NOT NULL DEFAULT 0,
            severity VARCHAR(50) NOT NULL DEFAULT 'Medium', status VARCHAR(80) NOT NULL DEFAULT 'Open',
            owner_role VARCHAR(120) NULL, recommended_action VARCHAR(260) NULL,
            created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
            updated_at TIMESTAMPTZ NULL)",

        @"CREATE TABLE IF NOT EXISTS cost_leakage_actions (
            id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY, company_id BIGINT NOT NULL DEFAULT 1,
            cost_leakage_item_id BIGINT NOT NULL, action_title VARCHAR(220) NOT NULL,
            action_description TEXT NULL, estimated_savings DECIMAL(12,2) NOT NULL DEFAULT 0,
            status VARCHAR(80) NOT NULL DEFAULT 'Open', assigned_to_user_id BIGINT NULL,
            due_at TIMESTAMPTZ NULL,
            created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
            updated_at TIMESTAMPTZ NULL)",
    ];

    private static readonly string[] Indexes =
    [
        "CREATE INDEX IF NOT EXISTS ix_b5_fuel_tx ON fuel_transactions(company_id, vehicle_id, driver_id, anomaly_status, fuel_date)",
        "CREATE INDEX IF NOT EXISTS ix_b5_idling ON idling_events(company_id, vehicle_id, driver_id, threshold_status, started_at)",
        "CREATE INDEX IF NOT EXISTS ix_b5_fuel_anomalies ON fuel_anomalies(company_id, vehicle_id, status, severity)",
        "CREATE INDEX IF NOT EXISTS ix_b5_expenses ON expenses(company_id, vehicle_id, driver_id, approval_status, expense_date)",
        "CREATE INDEX IF NOT EXISTS ix_b5_expense_categories ON expense_categories(company_id, status)",
        "CREATE INDEX IF NOT EXISTS ix_b5_contracts ON contracts(company_id, customer_id, status, margin_risk)",
        "CREATE INDEX IF NOT EXISTS ix_b5_contract_rates ON contract_rates(company_id, contract_id, status)",
        "CREATE INDEX IF NOT EXISTS ix_b5_carriers ON carriers(company_id, status, compliance_status, risk_score)",
        "CREATE INDEX IF NOT EXISTS ix_b5_carrier_docs ON carrier_documents(company_id, carrier_id, status, expiry_date)",
        "CREATE INDEX IF NOT EXISTS ix_b5_carrier_perf ON carrier_performance(company_id, carrier_id, period_start)",
        "CREATE INDEX IF NOT EXISTS ix_b5_cost_margin ON cost_margin_records(company_id, entity_type, entity_id, margin_risk)",
        "CREATE INDEX IF NOT EXISTS ix_b5_cost_leakage ON cost_leakage_items(company_id, category, severity, status)",
    ];

    private static readonly string[] Seeds =
    [
        @"UPDATE fuel_transactions
          SET transaction_number = COALESCE(transaction_number, 'FT-' || LPAD(id::TEXT,5,'0')),
              fuel_date          = COALESCE(fuel_date, DATE(transaction_time)),
              quantity           = CASE WHEN quantity=0 THEN gallons ELSE quantity END,
              unit               = COALESCE(unit, 'Gallons'),
              fuel_type          = COALESCE(fuel_type, (ARRAY['Diesel','Gasoline','DEF'])[(id%3)+1]),
              unit_price         = CASE WHEN unit_price=0 THEN ROUND(total_cost / NULLIF(gallons,0), 4) ELSE unit_price END,
              payment_method     = COALESCE(payment_method, (ARRAY['Fleet Card','Credit Card','Cash'])[(id%3)+1]),
              region             = COALESCE(region, (ARRAY['Northern VA','DC Metro','Southern VA','Maryland','West VA'])[(id%5)+1]),
              anomaly_status     = COALESCE(anomaly_status, CASE WHEN id%8=0 THEN 'Anomaly Detected' ELSE 'Normal' END),
              driver_id          = COALESCE(driver_id, ((id-1)%20)+1),
              recommended_action = COALESCE(recommended_action, CASE WHEN id%8=0 THEN 'Investigate fuel quantity discrepancy' ELSE 'Normal operating transaction' END)
          WHERE transaction_number IS NULL OR unit_price = 0",

        @"INSERT INTO fuel_transactions
            (company_id, transaction_number, vehicle_id, driver_id, job_id, route_id,
             fuel_date, fuel_type, gallons, quantity, unit, unit_price, total_cost,
             currency, odometer, fuel_station, payment_method, fuel_card_number, region, anomaly_status, notes)
          WITH RECURSIVE seq(n) AS (SELECT 1 UNION ALL SELECT n+1 FROM seq WHERE n<50)
          SELECT 1,
            'FT-B5-' || (1000+n),
            ((n-1)%20)+1, ((n-1)%20)+1, ((n-1)%50)+1, ((n-1)%12)+1,
            CURRENT_DATE - n * INTERVAL '1 day',
            (ARRAY['Diesel','Gasoline','DEF','Diesel'])[(n%4)+1],
            ROUND((12 + (n%38))::NUMERIC, 2),
            ROUND((12 + (n%38))::NUMERIC, 2),
            CASE WHEN n%4=0 THEN 'Liters' ELSE 'Gallons' END,
            ROUND((3.45 + (n%100)*0.012)::NUMERIC, 4),
            ROUND(((12+(n%38)) * (3.45+(n%100)*0.012))::NUMERIC, 2),
            'USD',
            ROUND((45000 + n*112)::NUMERIC, 2),
            (ARRAY['Shell - Manassas VA','BP - Woodbridge VA','Exxon - Alexandria VA','Pilot - Dulles VA','7-Eleven - Fairfax VA','Wawa - Arlington VA','Sunoco - DC'])[(n%7)+1],
            (ARRAY['Fleet Card','Company Card','Cash'])[(n%3)+1],
            CASE WHEN n%3=0 THEN 'FC-' || LPAD((n)::TEXT,6,'0') ELSE NULL END,
            (ARRAY['Northern VA','DC Metro','Southern VA','Maryland','West VA'])[(n%5)+1],
            CASE WHEN n%9=0 THEN 'Anomaly Detected' WHEN n%7=0 THEN 'Under Review' ELSE 'Normal' END,
            CASE WHEN n%9=0 THEN 'AI detected possible quantity discrepancy vs odometer reading.' ELSE NULL END
          FROM seq
          WHERE (SELECT COUNT(*) FROM fuel_transactions WHERE transaction_number LIKE 'FT-B5-%') < 50",

        @"INSERT INTO idling_events
            (company_id, event_number, vehicle_id, driver_id, job_id, route_id,
             location_description, started_at, ended_at, duration_minutes,
             estimated_fuel_burn, estimated_cost, currency, threshold_status, risk_score, recommended_action)
          WITH RECURSIVE seq(n) AS (SELECT 1 UNION ALL SELECT n+1 FROM seq WHERE n<30)
          SELECT 1,
            'IDLE-' || (1000+n),
            ((n-1)%20)+1, ((n-1)%20)+1, ((n-1)%50)+1, ((n-1)%12)+1,
            (ARRAY['Manassas Yard','Woodbridge I-95','Alexandria Medical Zone','Dulles Toll Road','Fairfax Delivery Zone','Arlington Urban Core','Washington DC Service Zone'])[(n%7)+1],
            NOW() - n*2 * INTERVAL '1 hour',
            NOW() - (n*2-1) * INTERVAL '1 hour',
            ROUND((8 + (n%45))::NUMERIC, 1),
            ROUND(((8+(n%45)) * 0.006)::NUMERIC, 3),
            ROUND(((8+(n%45)) * 0.006 * 3.75)::NUMERIC, 2),
            'USD',
            CASE WHEN n%3=0 THEN 'Excessive' WHEN n%5=0 THEN 'Warning' ELSE 'Normal' END,
            CASE WHEN n%3=0 THEN 72+(n%20) ELSE 18+(n%30) END,
            CASE WHEN n%3=0 THEN 'Idle cost leakage detected — coach driver on idling policy'
                 WHEN n%5=0 THEN 'Review idle duration — approaching threshold'
                 ELSE 'Normal idle within policy' END
          FROM seq
          WHERE (SELECT COUNT(*) FROM idling_events) < 30",

        @"INSERT INTO fuel_anomalies
            (company_id, fuel_transaction_id, vehicle_id, driver_id, anomaly_type, severity, description, estimated_loss, status)
          WITH RECURSIVE seq(n) AS (SELECT 1 UNION ALL SELECT n+1 FROM seq WHERE n<12)
          SELECT 1, ((n-1)%50)+1, ((n-1)%20)+1, ((n-1)%20)+1,
            (ARRAY['Quantity vs Odometer Mismatch','Unusual Station','High Unit Price','Repeated Fill-up','High Cost per Mile','Off-route Purchase'])[(n%6)+1],
            (ARRAY['Low','Medium','High','Critical'])[(n%4)+1],
            'AI fuel advisor detected anomaly: ' || (ARRAY['fuel quantity inconsistent with odometer delta','transaction at unusual station outside normal route corridor','unit price 18% above regional average — possible mis-key','duplicate fill-up within 4 hours of prior transaction','cost per mile significantly above fleet average','fuel purchase recorded outside active job route'])[(n%6)+1],
            ROUND((28 + (n%120))::NUMERIC, 2),
            (ARRAY['Open','Under Review','Resolved','Closed'])[(n%4)+1]
          FROM seq
          WHERE (SELECT COUNT(*) FROM fuel_anomalies) < 12",

        @"INSERT INTO expense_categories (company_id, category_name, category_type, status)
          SELECT 1, x.n, x.t, 'Active' FROM (
            SELECT 'Fuel' n, 'Operating' t UNION ALL SELECT 'Maintenance','Operating' UNION ALL SELECT 'Toll','Operating'
            UNION ALL SELECT 'Parking','Operating' UNION ALL SELECT 'Driver Reimbursement','HR'
            UNION ALL SELECT 'Carrier Charge','Operations' UNION ALL SELECT 'Insurance','Finance'
            UNION ALL SELECT 'Permit','Compliance' UNION ALL SELECT 'Inspection','Compliance'
            UNION ALL SELECT 'Miscellaneous','Other'
          ) x WHERE NOT EXISTS (SELECT 1 FROM expense_categories WHERE category_name=x.n AND company_id=1)",

        @"UPDATE expenses
          SET expense_number    = COALESCE(expense_number, 'EXP-' || LPAD(id::TEXT,5,'0')),
              category_name     = COALESCE(category_name, category),
              approval_status   = COALESCE(approval_status, CASE WHEN status='Approved' THEN 'Approved' WHEN id%4=0 THEN 'Rejected' ELSE 'Pending' END),
              receipt_status    = COALESCE(receipt_status, CASE WHEN id%3=0 THEN 'Uploaded' ELSE 'Missing' END),
              risk_score        = CASE WHEN risk_score=20 AND id%5=0 THEN 65+(id%25) ELSE risk_score END,
              recommended_action= COALESCE(recommended_action, CASE WHEN receipt_status='Missing' THEN 'Upload receipt before approval' ELSE 'Review and approve expense' END)
          WHERE expense_number IS NULL",

        @"INSERT INTO expenses
            (company_id, expense_number, category, title, category_id, category_name, amount, currency,
             expense_date, vehicle_id, driver_id, job_id, route_id, customer_id,
             vendor_name, status, approval_status, receipt_status, risk_score, recommended_action, notes)
          WITH RECURSIVE seq(n) AS (SELECT 1 UNION ALL SELECT n+1 FROM seq WHERE n<40)
          SELECT 1,
            'EXP-B5-' || (2000+n),
            (ARRAY['Fuel','Maintenance','Toll','Parking','Driver Reimbursement','Carrier Charge','Insurance','Permit','Inspection','Miscellaneous'])[(n%10)+1],
            (ARRAY['Fuel','Maintenance','Toll','Parking','Driver Reimbursement','Carrier Charge','Insurance','Permit','Inspection','Miscellaneous'])[(n%10)+1] || ' expense review ' || n,
            ((n-1)%10)+1,
            (ARRAY['Fuel','Maintenance','Toll','Parking','Driver Reimbursement','Carrier Charge','Insurance','Permit','Inspection','Miscellaneous'])[(n%10)+1],
            ROUND((45 + (n%480))::NUMERIC, 2),
            'USD',
            CURRENT_DATE - n * INTERVAL '1 day',
            ((n-1)%20)+1, ((n-1)%20)+1, ((n-1)%50)+1, ((n-1)%12)+1, ((n-1)%10)+1,
            (ARRAY['Shell Fuel Network','NOVA Fleet Care','E-ZPass VA','Diamond Parking','Driver Pool','FastFreight Logistics','AIG Fleet Insurance'])[(n%7)+1],
            CASE WHEN n%6=0 THEN 'Rejected' WHEN n%4=0 THEN 'Approved' ELSE 'Pending' END,
            CASE WHEN n%6=0 THEN 'Rejected' WHEN n%4=0 THEN 'Approved' ELSE 'Pending' END,
            CASE WHEN n%3=0 THEN 'Uploaded' WHEN n%5=0 THEN 'Pending' ELSE 'Missing' END,
            CASE WHEN n%7=0 THEN 70+(n%20) ELSE 15+(n%35) END,
            CASE WHEN n%7=0 THEN 'Anomaly detected — review amount and receipt' ELSE 'Review and approve expense' END,
            CASE WHEN n%7=0 THEN 'AI flagged as unusual amount for category.' ELSE NULL END
          FROM seq
          WHERE (SELECT COUNT(*) FROM expenses WHERE expense_number LIKE 'EXP-B5-%') < 40",

        @"UPDATE contracts
          SET contract_number         = COALESCE(contract_number, 'CON-' || LPAD(id::TEXT,5,'0')),
              base_rate               = CASE WHEN base_rate=0 THEN ROUND((1.85 + (id%40)*0.08)::NUMERIC, 4) ELSE base_rate END,
              currency                = COALESCE(currency, 'USD'),
              fuel_surcharge_enabled  = CASE WHEN id%3=0 THEN TRUE ELSE fuel_surcharge_enabled END,
              fuel_surcharge_percent  = CASE WHEN id%3=0 THEN ROUND((3+(id%5))::NUMERIC,2) ELSE fuel_surcharge_percent END,
              margin_risk             = COALESCE(CASE WHEN base_rate < 2.2 THEN 'High' WHEN base_rate < 2.8 THEN 'Medium' ELSE 'Low' END, 'Low'),
              contract_type           = COALESCE(contract_type, (ARRAY['Customer','Carrier','Internal'])[(id%3)+1])
          WHERE contract_number IS NULL",

        @"INSERT INTO contracts
            (company_id, contract_code, title, contract_number, customer_id, carrier_id, contract_type, effective_date, expiry_date,
             status, currency, base_rate, rate_type, fuel_surcharge_enabled, fuel_surcharge_percent,
             sla_terms, margin_risk, notes)
          WITH RECURSIVE seq(n) AS (SELECT 1 UNION ALL SELECT n+1 FROM seq WHERE n<12)
          SELECT 1,
            'CON-B5-' || (3000+n),
            'CON-B5-' || (3000+n),
            (ARRAY['Customer','Carrier','Internal'])[(n%3)+1] || ' rate agreement ' || n,
            ((n-1)%10)+1,
            CASE WHEN n%3=0 THEN ((n-1)%5)+1 ELSE NULL END,
            (ARRAY['Customer','Carrier','Internal'])[(n%3)+1],
            CURRENT_DATE - n*30 * INTERVAL '1 day',
            CASE WHEN n%4=0 THEN CURRENT_DATE - 10 * INTERVAL '1 day'
                 ELSE CURRENT_DATE + (13-n)*30 * INTERVAL '1 day' END,
            CASE WHEN n%4=0 THEN 'Expired' WHEN n%8=0 THEN 'Expiring Soon' ELSE 'Active' END,
            'USD',
            ROUND((1.75 + (n%40)*0.09)::NUMERIC, 4),
            (ARRAY['Per Mile','Per Kilometer','Flat Rate','Hourly','Per Stop','Weight Based'])[(n%6)+1],
            n%3=0,
            CASE WHEN n%3=0 THEN ROUND((3+(n%5))::NUMERIC,2) ELSE NULL END,
            CASE WHEN n%2=0 THEN 'On-time delivery 96%, 4h ETA window, damage liability per cargo value.' ELSE NULL END,
            CASE WHEN 1.75+(n%40)*0.09 < 2.20 THEN 'High'
                 WHEN 1.75+(n%40)*0.09 < 2.80 THEN 'Medium' ELSE 'Low' END,
            CASE WHEN n%4=0 THEN 'Contract nearing expiry — schedule renewal review.' ELSE NULL END
          FROM seq
          WHERE (SELECT COUNT(*) FROM contracts WHERE contract_number LIKE 'CON-B5-%') < 12",

        @"INSERT INTO contract_rates
            (company_id, contract_id, rate_code, rate_type, origin_zone, destination_zone,
             vehicle_type, base_rate, minimum_charge, fuel_surcharge_percent, accessorial_type,
             effective_date, expiry_date, status)
          WITH RECURSIVE seq(n) AS (SELECT 1 UNION ALL SELECT n+1 FROM seq WHERE n<30)
          SELECT 1, ((n-1)%12)+1,
            'RATE-' || LPAD(n::TEXT,4,'0'),
            (ARRAY['Per Mile','Per Kilometer','Flat Rate','Hourly','Per Stop','Weight Based'])[(n%6)+1],
            (ARRAY['Northern VA','DC Metro','Southern VA','Maryland','West VA'])[(n%5)+1],
            (ARRAY['DC Metro','Northern VA','Maryland','West VA','Southern VA'])[(n%5)+1],
            (ARRAY['Truck','Van','Reefer','Flatbed'])[(n%4)+1],
            ROUND((1.45 + (n%60)*0.045)::NUMERIC, 4),
            ROUND((85 + (n%90))::NUMERIC, 2),
            CASE WHEN n%3=0 THEN ROUND((3+(n%5))::NUMERIC,2) ELSE NULL END,
            CASE WHEN n%4=0 THEN (ARRAY['Liftgate','Fuel Surcharge','Detention'])[(n%3)+1] ELSE NULL END,
            CURRENT_DATE - n*15 * INTERVAL '1 day',
            CURRENT_DATE + (31-n)*20 * INTERVAL '1 day',
            CASE WHEN n%8=0 THEN 'Inactive' ELSE 'Active' END
          FROM seq
          WHERE (SELECT COUNT(*) FROM contract_rates) < 30",

        @"UPDATE carriers
          SET carrier_number      = COALESCE(carrier_number, 'CAR-' || LPAD(id::TEXT,5,'0')),
              region              = COALESCE(region, (ARRAY['Northern VA','DC Metro','Southern VA','Maryland','National'])[(id%5)+1]),
              compliance_status   = COALESCE(compliance_status, CASE WHEN id%5=0 THEN 'Non-Compliant' WHEN id%4=0 THEN 'At Risk' ELSE 'Compliant' END),
              insurance_expiry    = COALESCE(insurance_expiry, CURRENT_DATE + (id%18) * INTERVAL '1 month'),
              contract_status     = COALESCE(contract_status, CASE WHEN id%6=0 THEN 'Expired' ELSE 'Active' END),
              on_time_percent     = CASE WHEN on_time_percent=90 THEN GREATEST(72,97-(id%3)*8) ELSE on_time_percent END,
              safety_score        = CASE WHEN safety_score=88 THEN GREATEST(68,96-(id%4)*7) ELSE safety_score END,
              performance_score   = CASE WHEN performance_score=86 THEN GREATEST(70,95-(id%4)*6) ELSE performance_score END,
              risk_score          = CASE WHEN risk_score=20 THEN 12+(id%6)*10 ELSE risk_score END,
              recommended_action  = COALESCE(recommended_action, CASE WHEN compliance_status='Non-Compliant' THEN 'Suspend carrier — compliance risk' WHEN insurance_expiry < CURRENT_DATE + 60 * INTERVAL '1 day' THEN 'Renew insurance immediately' ELSE 'Monitor performance' END)
          WHERE carrier_number IS NULL",

        @"INSERT INTO carriers
            (company_id, carrier_number, name, mc_number, contact_name, phone, email, region, status,
             compliance_status, insurance_expiry, contract_status, on_time_percent, safety_score,
             cost_score, performance_score, risk_score, recommended_action, notes)
          WITH RECURSIVE seq(n) AS (SELECT 1 UNION ALL SELECT n+1 FROM seq WHERE n<10)
          SELECT 1,
            'CAR-B5-' || (4000+n),
            (ARRAY['FastLane','PrimeHaul','NovaTrans','BlueStar','EagleFreight'])[(n%5)+1] || ' Logistics',
            'MC-' || (100000+n*7),
            (ARRAY['Mike Harrison','Susan Park','David Wright','Amy Torres','James Clark'])[(n%5)+1],
            '571-' || LPAD((n*37)::TEXT,3,'0') || '-' || LPAD((n*91)::TEXT,4,'0'),
            LOWER((ARRAY['fastlane','primehavl','novatrans','bluestar','eaglefreight'])[(n%5)+1] || '@carrier.example.com'),
            (ARRAY['Northern VA','DC Metro','Southern VA','Maryland','National'])[(n%5)+1],
            CASE WHEN n%5=0 THEN 'Suspended' WHEN n%4=0 THEN 'Pending' ELSE 'Active' END,
            CASE WHEN n%5=0 THEN 'Non-Compliant' WHEN n%4=0 THEN 'At Risk' ELSE 'Compliant' END,
            CURRENT_DATE + (n%18) * INTERVAL '1 month',
            CASE WHEN n%6=0 THEN 'Expired' ELSE 'Active' END,
            GREATEST(72, 97-(n%4)*7),
            GREATEST(68, 95-(n%4)*6),
            GREATEST(70, 94-(n%3)*6),
            GREATEST(70, 93-(n%4)*6),
            12+(n%6)*10,
            CASE WHEN n%5=0 THEN 'Suspend carrier — compliance risk'
                 WHEN n%4=0 THEN 'Review carrier compliance before next tender'
                 ELSE 'Monitor carrier performance quarterly' END,
            CASE WHEN n%5=0 THEN 'Carrier suspended pending compliance review.' ELSE NULL END
          FROM seq
          WHERE (SELECT COUNT(*) FROM carriers WHERE carrier_number LIKE 'CAR-B5-%') < 10",

        @"INSERT INTO carrier_documents
            (company_id, carrier_id, document_type, document_number, expiry_date, status)
          WITH RECURSIVE seq(n) AS (SELECT 1 UNION ALL SELECT n+1 FROM seq WHERE n<15)
          SELECT 1, ((n-1)%10)+1,
            (ARRAY['Operating Authority','Insurance Certificate','Safety Rating','MC Number','DOT Registration'])[(n%5)+1],
            'DOC-CAR-' || LPAD(n::TEXT,4,'0'),
            CURRENT_DATE + (n%18-3) * INTERVAL '1 month',
            CASE WHEN n%5=0 THEN 'Expired' WHEN n%6=0 THEN 'Expiring' ELSE 'Active' END
          FROM seq
          WHERE (SELECT COUNT(*) FROM carrier_documents) < 15",

        @"INSERT INTO carrier_performance
            (company_id, carrier_id, period_start, period_end, jobs_handled, on_time_percent, incident_count, expense_total, performance_score)
          WITH RECURSIVE seq(n) AS (SELECT 1 UNION ALL SELECT n+1 FROM seq WHERE n<10)
          SELECT 1, ((n-1)%10)+1,
            CURRENT_DATE - n * INTERVAL '1 month',
            CURRENT_DATE - (n-1) * INTERVAL '1 month',
            12+(n%28), GREATEST(70, 97-(n%4)*6), n%4, ROUND((2800+(n%3200))::NUMERIC, 2), GREATEST(68, 95-(n%5)*5)
          FROM seq
          WHERE (SELECT COUNT(*) FROM carrier_performance) < 10",

        @"INSERT INTO cost_margin_records
            (company_id, entity_type, entity_id, customer_id, job_id, route_id, vehicle_id, driver_id,
             revenue_estimate, fuel_cost, driver_cost, maintenance_cost, carrier_cost, expense_total,
             delay_cost, idle_cost, total_cost, margin_estimate, margin_percent, margin_risk)
          WITH RECURSIVE seq(n) AS (SELECT 1 UNION ALL SELECT n+1 FROM seq WHERE n<40)
          SELECT 1,
            (ARRAY['job','route','vehicle','customer'])[(n%4)+1],
            ((n-1)%50)+1, ((n-1)%10)+1,
            CASE WHEN n%4=1 THEN ((n-1)%50)+1 ELSE NULL END,
            CASE WHEN n%4=2 THEN ((n-1)%12)+1 ELSE NULL END,
            CASE WHEN n%4=3 THEN ((n-1)%20)+1 ELSE NULL END,
            ((n-1)%20)+1,
            ROUND((480 + (n%520))::NUMERIC, 2),
            ROUND((88  + (n%120))::NUMERIC, 2),
            ROUND((120 + (n%80))::NUMERIC,  2),
            ROUND((22  + (n%55))::NUMERIC,  2),
            ROUND(CASE WHEN n%3=0 THEN (85+(n%120)) ELSE 0 END::NUMERIC, 2),
            ROUND((38  + (n%75))::NUMERIC,  2),
            ROUND(CASE WHEN n%5=0 THEN (45+(n%80)) ELSE 0 END::NUMERIC, 2),
            ROUND(CASE WHEN n%4=0 THEN (18+(n%35)) ELSE 0 END::NUMERIC, 2),
            ROUND(((88+(n%120)) + (120+(n%80)) + (22+(n%55)) + (38+(n%75)))::NUMERIC, 2),
            ROUND(((480+(n%520)) - ((88+(n%120))+(120+(n%80))+(22+(n%55))+(38+(n%75))))::NUMERIC, 2),
            ROUND((((480+(n%520)) - ((88+(n%120))+(120+(n%80))+(22+(n%55))+(38+(n%75)))) / NULLIF(480+(n%520),0) * 100)::NUMERIC, 2),
            CASE WHEN ((480+(n%520)) - ((88+(n%120))+(120+(n%80))+(22+(n%55))+(38+(n%75)))) / NULLIF(480+(n%520),0) < 0.15 THEN 'High'
                 WHEN ((480+(n%520)) - ((88+(n%120))+(120+(n%80))+(22+(n%55))+(38+(n%75)))) / NULLIF(480+(n%520),0) < 0.28 THEN 'Medium'
                 ELSE 'Low' END
          FROM seq
          WHERE (SELECT COUNT(*) FROM cost_margin_records) < 40",

        @"INSERT INTO cost_margin_predictions
            (company_id, entity_type, entity_id, prediction_type, predicted_revenue, predicted_cost,
             predicted_margin, predicted_margin_percent, confidence_level, risk_level, recommendation)
          WITH RECURSIVE seq(n) AS (SELECT 1 UNION ALL SELECT n+1 FROM seq WHERE n<20)
          SELECT 1,
            (ARRAY['job','route','vehicle','customer'])[(n%4)+1], ((n-1)%50)+1,
            (ARRAY['Margin Forecast','Cost Trend','Revenue Risk'])[(n%3)+1],
            ROUND((500+(n%480))::NUMERIC, 2),
            ROUND((320+(n%280))::NUMERIC, 2),
            ROUND(((500+(n%480)) - (320+(n%280)))::NUMERIC, 2),
            ROUND((((500+(n%480)) - (320+(n%280))) / NULLIF(500+(n%480),0) * 100)::NUMERIC, 2),
            (ARRAY['High','Medium','Low'])[(n%3)+1],
            CASE WHEN ((500+(n%480)) - (320+(n%280))) / NULLIF(500+(n%480),0) < 0.12 THEN 'High'
                 WHEN ((500+(n%480)) - (320+(n%280))) / NULLIF(500+(n%480),0) < 0.25 THEN 'Medium' ELSE 'Low' END,
            (ARRAY['Review pricing with customer to recover margin','Reduce idle time to lower cost burden','Optimize route sequence to cut fuel spend','Renegotiate carrier rate for this lane','Review accessorial charges for unbilled items'])[(n%5)+1]
          FROM seq
          WHERE (SELECT COUNT(*) FROM cost_margin_predictions) < 20",

        @"INSERT INTO cost_leakage_items
            (company_id, leakage_number, category, entity_type, entity_id, title, description,
             estimated_loss, projected_monthly_loss, severity, status, owner_role, recommended_action)
          WITH RECURSIVE seq(n) AS (SELECT 1 UNION ALL SELECT n+1 FROM seq WHERE n<25)
          SELECT 1,
            'LEAK-' || (1000+n),
            (ARRAY['Idle Time','Fuel Anomaly','Delay / SLA Penalty','Repeated Maintenance','Underpriced Contract','Unbilled Accessorial','Carrier Overcharge','High-Cost Driver','High-Cost Vehicle','Route Inefficiency','Missing Customer Update','Document/Compliance Risk'])[(n%12)+1],
            (ARRAY['vehicle','driver','job','route'])[(n%4)+1],
            ((n-1)%20)+1,
            (ARRAY['Excessive idle on','Fuel anomaly on','Delay penalty on','Repeat brake job on','Underpriced contract — ','Missing liftgate fee on','Carrier overcharge on','High fuel cost driver — ','High maintenance cost — ','Inefficient stop sequence on','ETA update missing for','Expired document risk —'])[(n%12)+1] || ' ' || (ARRAY['vehicle','driver','job','route'])[(n%4)+1] || ' #' || ((n-1)%20+1),
            (ARRAY['AI cost advisor identified leakage pattern from operational data — action recommended to recover margin.','Idle cost accumulation detected above threshold — coaching and scheduling adjustment advised.','Recurring charge pattern requires finance review and corrective pricing.','Carrier invoice variance exceeds contracted rate — dispute or renegotiation required.','Compliance document approaching expiry — potential fine risk and operational exposure.'])[(n%5)+1],
            ROUND((85+(n%820))::NUMERIC, 2),
            ROUND(((85+(n%820))*0.95)::NUMERIC, 2),
            (ARRAY['Low','Medium','High','Critical'])[(n%4)+1],
            (ARRAY['Open','Acknowledged','In Progress','Resolved'])[(n%4)+1],
            (ARRAY['Fleet Manager','Finance Manager','Compliance Manager','Dispatch Manager','Company Admin'])[(n%5)+1],
            (ARRAY['Coach driver on idle policy and set threshold alerts','Investigate fuel transaction and review driver log','Dispute carrier invoice and update rate schedule','Review contract pricing against actual cost per mile','Add accessorial fee to next customer invoice','Schedule compliance renewal immediately'])[(n%6)+1]
          FROM seq
          WHERE (SELECT COUNT(*) FROM cost_leakage_items) < 25",

        @"INSERT INTO cost_leakage_actions
            (company_id, cost_leakage_item_id, action_title, action_description, estimated_savings,
             status, assigned_to_user_id, due_at)
          WITH RECURSIVE seq(n) AS (SELECT 1 UNION ALL SELECT n+1 FROM seq WHERE n<25)
          SELECT 1, ((n-1)%25)+1,
            'Recovery action ' || n || ' — ' || (ARRAY['Reduce idle time','Dispute overcharge','Renegotiate rate','Add accessorial fee','Schedule compliance renewal','Coach driver'])[(n%6)+1],
            'Implement corrective action to recover estimated $' || ROUND(120+(n%680),0)::TEXT || ' monthly leakage. Review evidence and assign owner.',
            ROUND((120+(n%680))::NUMERIC, 2),
            (ARRAY['Open','In Progress','Completed','Cancelled'])[(n%4)+1],
            1,
            NOW() + n*3 * INTERVAL '1 day'
          FROM seq
          WHERE (SELECT COUNT(*) FROM cost_leakage_actions) < 25",

        @"INSERT INTO ai_recommendations (company_id, module_key, title, body, score, status)
          SELECT 1, x.module_key, x.title, x.body, x.score, 'Recommended' FROM (
            SELECT 'fuel-idling' module_key, 'Fuel Cost Leakage Radar' title, 'AI detected 4 vehicles with fuel spend 18%+ above fleet average — investigate anomalies and review driver coaching queue.' body, 96 score
            UNION ALL SELECT 'fuel-idling','Idle Cost Accumulation','3 vehicles accumulated excessive idle time in Northern VA this week — estimated $680 recoverable idle cost leakage.',94
            UNION ALL SELECT 'fuel-idling','Fuel Anomaly: Quantity vs Odometer','Transaction FT-B5-1009 shows fuel quantity inconsistent with odometer delta — investigate for possible card mis-use.',92
            UNION ALL SELECT 'expenses','Expense Anomaly Queue','7 expenses flagged as unusual — amount, timing, or category mismatch. Review before end-of-month close.',95
            UNION ALL SELECT 'expenses','Missing Receipt Risk','12 approved expenses are missing receipts — finance audit risk if not resolved before next cycle.',93
            UNION ALL SELECT 'expenses','Approval Delay Warning','9 expenses pending approval beyond 72h threshold — fleet manager review queue recommended.',91
            UNION ALL SELECT 'contracts-rates','Underpriced Contract Alert','2 customer contracts show base rate below estimated cost per mile — immediate renegotiation recommended.',97
            UNION ALL SELECT 'contracts-rates','Contract Expiry Queue','3 contracts expiring within 30 days — renewal workflow should be initiated to maintain continuous service coverage.',94
            UNION ALL SELECT 'contracts-rates','Fuel Surcharge Opportunity','5 contracts have no fuel surcharge enabled despite diesel price increase — update rate cards.',92
            UNION ALL SELECT 'carrier-management','Carrier Compliance Risk','CarrierB5-4005 shows Non-Compliant status — suspend from active tenders until resolved.',96
            UNION ALL SELECT 'carrier-management','Insurance Expiry Watch','2 carriers have insurance expiring within 60 days — notify procurement for renewal.',93
            UNION ALL SELECT 'carrier-management','Carrier Cost Efficiency','Top 3 carriers show cost per mile 14% above contracted rate — audit invoices and initiate dispute process.',91
            UNION ALL SELECT 'predictive-margin','Margin Risk Predictor','6 active jobs projected below 15% margin — review pricing, fuel allocation and carrier cost.',97
            UNION ALL SELECT 'predictive-margin','Route Profitability Score','Route 8 shows negative margin this month due to delay costs and idle accumulation — restructure stop sequence.',95
            UNION ALL SELECT 'predictive-margin','Customer Profitability Watch','Customer #3 shows consistent margin below threshold — initiate contract review and rate adjustment discussion.',93
            UNION ALL SELECT 'cost-leakage','Savings Opportunity: $12,400 This Month','AI identified $12,400 recoverable cost leakage across idle time, fuel anomalies, carrier overcharges and unbilled accessorials.',98
            UNION ALL SELECT 'cost-leakage','Carrier Invoice Dispute Queue','3 carrier invoices exceed contracted rates by $340+ each — dispute and recover before payment cycle.',96
            UNION ALL SELECT 'cost-leakage','Accessorial Revenue Gap','Estimated $2,800/month in liftgate and detention charges going unbilled — update invoicing workflow.',94
          ) x WHERE NOT EXISTS (SELECT 1 FROM ai_recommendations ar WHERE ar.module_key=x.module_key AND ar.title=x.title)",

        @"INSERT INTO notifications (company_id, title, body, severity, module_key, status)
          SELECT 1, x.title, x.body, x.severity, x.module_key, 'Unread' FROM (
            SELECT 'Fuel anomaly detected' title,'AI detected possible fuel card misuse — review FT-B5-1009.' body,'High' severity,'fuel-idling' module_key
            UNION ALL SELECT 'Expense approval needed','9 expenses pending manager approval.','Warning','expenses'
            UNION ALL SELECT 'Contract expiring','CON-B5-3004 expires in 8 days — initiate renewal.','High','contracts-rates'
            UNION ALL SELECT 'Carrier compliance risk','CAR-B5-4005 is Non-Compliant — review before next tender.','Critical','carrier-management'
            UNION ALL SELECT 'Margin risk alert','6 jobs below 15% margin target.','High','predictive-margin'
            UNION ALL SELECT 'Cost leakage detected','$12,400 recoverable leakage identified this month.','Warning','cost-leakage'
          ) x WHERE NOT EXISTS (SELECT 1 FROM notifications n WHERE n.title=x.title AND n.module_key=x.module_key)",

        @"INSERT INTO audit_logs (company_id, action_name, entity_type, entity_id, actor_name, created_at)
          SELECT 1, x.action_name, x.entity_type, x.entity_id, 'OpsTrax System Seed', x.ts FROM (
            SELECT 'fuel.transaction.created' action_name,'FuelTransaction' entity_type,1 entity_id, NOW() - 2 * INTERVAL '1 hour' ts
            UNION ALL SELECT 'expense.created','Expense',1, NOW() - 3 * INTERVAL '1 hour'
            UNION ALL SELECT 'contract.created','Contract',1, NOW() - 4 * INTERVAL '1 hour'
            UNION ALL SELECT 'carrier.created','Carrier',1, NOW() - 5 * INTERVAL '1 hour'
            UNION ALL SELECT 'cost.leakage.detected','CostLeakage',1, NOW() - 6 * INTERVAL '1 hour'
          ) x WHERE (SELECT COUNT(*) FROM audit_logs WHERE action_name LIKE 'fuel.%' OR action_name LIKE 'expense.%') < 5",
    ];
}
