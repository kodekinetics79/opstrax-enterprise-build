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
            @"SELECT COUNT(*) FROM information_schema.columns WHERE table_schema=DATABASE() AND table_name=@table AND column_name=@column",
            c => { c.Parameters.AddWithValue("@table", table); c.Parameters.AddWithValue("@column", column); }, ct);
        if (exists == 0) await db.ExecuteAsync($"ALTER TABLE {table} ADD COLUMN {column} {definition}", ct: ct);
    }

    private sealed record ColumnDefinition(string Table, string Name, string Definition);

    private static readonly ColumnDefinition[] Columns =
    [
        // fuel_transactions enhancements
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
        new("fuel_transactions", "updated_at",          "TIMESTAMP NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP"),
        new("fuel_transactions", "deleted_at",          "TIMESTAMP NULL"),

        // expenses enhancements
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
        new("expenses", "updated_at",           "TIMESTAMP NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP"),
        new("expenses", "deleted_at",           "TIMESTAMP NULL"),

        // carriers enhancements
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
        new("carriers", "updated_at",           "TIMESTAMP NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP"),
        new("carriers", "deleted_at",           "TIMESTAMP NULL"),

        // contracts enhancements
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
        new("contracts", "updated_at",              "TIMESTAMP NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP"),
        new("contracts", "deleted_at",              "TIMESTAMP NULL"),

        // audit log compatibility for later batch seeds/actions
        new("audit_logs", "entity_type",            "VARCHAR(100) NULL"),
    ];

    private static readonly string[] Tables =
    [
        @"CREATE TABLE IF NOT EXISTS idling_events (
            id BIGINT PRIMARY KEY AUTO_INCREMENT, company_id BIGINT NOT NULL DEFAULT 1,
            event_number VARCHAR(80) NOT NULL, vehicle_id BIGINT NOT NULL, driver_id BIGINT NULL,
            job_id BIGINT NULL, route_id BIGINT NULL, location_description VARCHAR(220) NULL,
            started_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP, ended_at DATETIME NULL,
            duration_minutes DECIMAL(8,2) NOT NULL DEFAULT 0, estimated_fuel_burn DECIMAL(10,3) NOT NULL DEFAULT 0,
            estimated_cost DECIMAL(12,2) NOT NULL DEFAULT 0, currency VARCHAR(10) NOT NULL DEFAULT 'USD',
            threshold_status VARCHAR(80) NOT NULL DEFAULT 'Normal', risk_score DECIMAL(6,2) NOT NULL DEFAULT 20,
            recommended_action VARCHAR(260) NULL,
            created_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
            updated_at TIMESTAMP NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP)",

        @"CREATE TABLE IF NOT EXISTS fuel_anomalies (
            id BIGINT PRIMARY KEY AUTO_INCREMENT, company_id BIGINT NOT NULL DEFAULT 1,
            fuel_transaction_id BIGINT NULL, vehicle_id BIGINT NULL, driver_id BIGINT NULL,
            anomaly_type VARCHAR(120) NOT NULL, severity VARCHAR(50) NOT NULL DEFAULT 'Medium',
            description TEXT NULL, estimated_loss DECIMAL(12,2) NOT NULL DEFAULT 0,
            status VARCHAR(80) NOT NULL DEFAULT 'Open',
            reviewed_at DATETIME NULL, created_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP)",

        @"CREATE TABLE IF NOT EXISTS expense_categories (
            id BIGINT PRIMARY KEY AUTO_INCREMENT, company_id BIGINT NOT NULL DEFAULT 1,
            category_name VARCHAR(120) NOT NULL, category_type VARCHAR(80) NOT NULL DEFAULT 'Operating',
            status VARCHAR(50) NOT NULL DEFAULT 'Active',
            created_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP)",

        @"CREATE TABLE IF NOT EXISTS contract_rates (
            id BIGINT PRIMARY KEY AUTO_INCREMENT, company_id BIGINT NOT NULL DEFAULT 1,
            contract_id BIGINT NOT NULL, rate_code VARCHAR(80) NOT NULL,
            rate_type VARCHAR(80) NOT NULL DEFAULT 'Per Mile', origin_zone VARCHAR(120) NULL,
            destination_zone VARCHAR(120) NULL, vehicle_type VARCHAR(80) NULL,
            base_rate DECIMAL(12,4) NOT NULL DEFAULT 0, minimum_charge DECIMAL(12,2) NULL,
            fuel_surcharge_percent DECIMAL(6,2) NULL, accessorial_type VARCHAR(120) NULL,
            effective_date DATE NOT NULL, expiry_date DATE NULL, status VARCHAR(50) NOT NULL DEFAULT 'Active',
            created_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
            updated_at TIMESTAMP NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP)",

        @"CREATE TABLE IF NOT EXISTS carrier_documents (
            id BIGINT PRIMARY KEY AUTO_INCREMENT, company_id BIGINT NOT NULL DEFAULT 1,
            carrier_id BIGINT NOT NULL, document_type VARCHAR(120) NOT NULL,
            document_number VARCHAR(120) NULL, expiry_date DATE NULL,
            status VARCHAR(50) NOT NULL DEFAULT 'Active', file_url VARCHAR(400) NULL,
            created_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP)",

        @"CREATE TABLE IF NOT EXISTS carrier_performance (
            id BIGINT PRIMARY KEY AUTO_INCREMENT, company_id BIGINT NOT NULL DEFAULT 1,
            carrier_id BIGINT NOT NULL, period_start DATE NOT NULL, period_end DATE NOT NULL,
            jobs_handled INT NOT NULL DEFAULT 0, on_time_percent DECIMAL(6,2) NOT NULL DEFAULT 90,
            incident_count INT NOT NULL DEFAULT 0, expense_total DECIMAL(12,2) NOT NULL DEFAULT 0,
            performance_score DECIMAL(6,2) NOT NULL DEFAULT 85,
            created_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP)",

        @"CREATE TABLE IF NOT EXISTS cost_margin_records (
            id BIGINT PRIMARY KEY AUTO_INCREMENT, company_id BIGINT NOT NULL DEFAULT 1,
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
            calculated_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
            created_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP)",

        @"CREATE TABLE IF NOT EXISTS cost_margin_predictions (
            id BIGINT PRIMARY KEY AUTO_INCREMENT, company_id BIGINT NOT NULL DEFAULT 1,
            entity_type VARCHAR(80) NOT NULL, entity_id BIGINT NOT NULL,
            prediction_type VARCHAR(80) NOT NULL DEFAULT 'Margin Forecast',
            predicted_revenue DECIMAL(12,2) NOT NULL DEFAULT 0, predicted_cost DECIMAL(12,2) NOT NULL DEFAULT 0,
            predicted_margin DECIMAL(12,2) NOT NULL DEFAULT 0, predicted_margin_percent DECIMAL(6,2) NOT NULL DEFAULT 0,
            confidence_level VARCHAR(50) NOT NULL DEFAULT 'Medium', risk_level VARCHAR(50) NOT NULL DEFAULT 'Low',
            recommendation TEXT NULL,
            created_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP)",

        @"CREATE TABLE IF NOT EXISTS cost_leakage_items (
            id BIGINT PRIMARY KEY AUTO_INCREMENT, company_id BIGINT NOT NULL DEFAULT 1,
            leakage_number VARCHAR(80) NOT NULL, category VARCHAR(120) NOT NULL,
            entity_type VARCHAR(80) NULL, entity_id BIGINT NULL, title VARCHAR(220) NOT NULL,
            description TEXT NULL, estimated_loss DECIMAL(12,2) NOT NULL DEFAULT 0,
            projected_monthly_loss DECIMAL(12,2) NOT NULL DEFAULT 0,
            severity VARCHAR(50) NOT NULL DEFAULT 'Medium', status VARCHAR(80) NOT NULL DEFAULT 'Open',
            owner_role VARCHAR(120) NULL, recommended_action VARCHAR(260) NULL,
            created_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
            updated_at TIMESTAMP NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP)",

        @"CREATE TABLE IF NOT EXISTS cost_leakage_actions (
            id BIGINT PRIMARY KEY AUTO_INCREMENT, company_id BIGINT NOT NULL DEFAULT 1,
            cost_leakage_item_id BIGINT NOT NULL, action_title VARCHAR(220) NOT NULL,
            action_description TEXT NULL, estimated_savings DECIMAL(12,2) NOT NULL DEFAULT 0,
            status VARCHAR(80) NOT NULL DEFAULT 'Open', assigned_to_user_id BIGINT NULL,
            due_at DATETIME NULL,
            created_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
            updated_at TIMESTAMP NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP)",
    ];

    private static readonly string[] Indexes =
    [
        "CREATE INDEX ix_b5_fuel_tx ON fuel_transactions(company_id, vehicle_id, driver_id, anomaly_status, fuel_date)",
        "CREATE INDEX ix_b5_idling ON idling_events(company_id, vehicle_id, driver_id, threshold_status, started_at)",
        "CREATE INDEX ix_b5_fuel_anomalies ON fuel_anomalies(company_id, vehicle_id, status, severity)",
        "CREATE INDEX ix_b5_expenses ON expenses(company_id, vehicle_id, driver_id, approval_status, expense_date)",
        "CREATE INDEX ix_b5_expense_categories ON expense_categories(company_id, status)",
        "CREATE INDEX ix_b5_contracts ON contracts(company_id, customer_id, status, margin_risk)",
        "CREATE INDEX ix_b5_contract_rates ON contract_rates(company_id, contract_id, status)",
        "CREATE INDEX ix_b5_carriers ON carriers(company_id, status, compliance_status, risk_score)",
        "CREATE INDEX ix_b5_carrier_docs ON carrier_documents(company_id, carrier_id, status, expiry_date)",
        "CREATE INDEX ix_b5_carrier_perf ON carrier_performance(company_id, carrier_id, period_start)",
        "CREATE INDEX ix_b5_cost_margin ON cost_margin_records(company_id, entity_type, entity_id, margin_risk)",
        "CREATE INDEX ix_b5_cost_leakage ON cost_leakage_items(company_id, category, severity, status)",
    ];

    private static readonly string[] Seeds =
    [
        // ---------- FUEL TRANSACTIONS: backfill existing stubs ----------------
        @"UPDATE fuel_transactions
          SET transaction_number = COALESCE(transaction_number, CONCAT('FT-', LPAD(id,5,'0'))),
              fuel_date          = COALESCE(fuel_date, DATE(transaction_time)),
              quantity           = IF(quantity=0, gallons, quantity),
              unit               = COALESCE(unit, 'Gallons'),
              fuel_type          = COALESCE(fuel_type, ELT((id%3)+1,'Diesel','Gasoline','DEF')),
              unit_price         = IF(unit_price=0, ROUND(total_cost / NULLIF(gallons,0), 4), unit_price),
              payment_method     = COALESCE(payment_method, ELT((id%3)+1,'Fleet Card','Credit Card','Cash')),
              region             = COALESCE(region, ELT((id%5)+1,'Northern VA','DC Metro','Southern VA','Maryland','West VA')),
              anomaly_status     = COALESCE(anomaly_status, IF(id%8=0,'Anomaly Detected','Normal')),
              driver_id          = COALESCE(driver_id, ((id-1)%20)+1),
              recommended_action = COALESCE(recommended_action, IF(id%8=0,'Investigate fuel quantity discrepancy','Normal operating transaction'))
          WHERE transaction_number IS NULL OR unit_price = 0",

        // ---------- NEW FUEL TRANSACTIONS (50 records) ----------------------
        @"INSERT INTO fuel_transactions
            (company_id, transaction_number, vehicle_id, driver_id, job_id, route_id,
             fuel_date, fuel_type, gallons, quantity, unit, unit_price, total_cost,
             currency, odometer, fuel_station, payment_method, fuel_card_number, region, anomaly_status, notes)
          WITH RECURSIVE seq(n) AS (SELECT 1 UNION ALL SELECT n+1 FROM seq WHERE n<50)
          SELECT 1,
            CONCAT('FT-B5-',1000+n),
            ((n-1)%20)+1,
            ((n-1)%20)+1,
            ((n-1)%50)+1,
            ((n-1)%12)+1,
            DATE_SUB(CURDATE(), INTERVAL n DAY),
            ELT((n%4)+1,'Diesel','Gasoline','DEF','Diesel'),
            ROUND(12 + (n%38), 2),
            ROUND(12 + (n%38), 2),
            IF(n%4=0,'Liters','Gallons'),
            ROUND(3.45 + (n%100)*0.012, 4),
            ROUND((12+(n%38)) * (3.45+(n%100)*0.012), 2),
            'USD',
            ROUND(45000 + n*112, 2),
            ELT((n%7)+1,'Shell - Manassas VA','BP - Woodbridge VA','Exxon - Alexandria VA','Pilot - Dulles VA','7-Eleven - Fairfax VA','Wawa - Arlington VA','Sunoco - DC'),
            ELT((n%3)+1,'Fleet Card','Company Card','Cash'),
            IF(n%3=0, CONCAT('FC-',LPAD(n,6,'0')), NULL),
            ELT((n%5)+1,'Northern VA','DC Metro','Southern VA','Maryland','West VA'),
            IF(n%9=0,'Anomaly Detected', IF(n%7=0,'Under Review','Normal')),
            IF(n%9=0,'AI detected possible quantity discrepancy vs odometer reading.', NULL)
          FROM seq
          WHERE (SELECT COUNT(*) FROM fuel_transactions WHERE transaction_number LIKE 'FT-B5-%') < 50",

        // ---------- IDLING EVENTS (30 records) --------------------------------
        @"INSERT INTO idling_events
            (company_id, event_number, vehicle_id, driver_id, job_id, route_id,
             location_description, started_at, ended_at, duration_minutes,
             estimated_fuel_burn, estimated_cost, currency, threshold_status, risk_score, recommended_action)
          WITH RECURSIVE seq(n) AS (SELECT 1 UNION ALL SELECT n+1 FROM seq WHERE n<30)
          SELECT 1,
            CONCAT('IDLE-',1000+n),
            ((n-1)%20)+1,
            ((n-1)%20)+1,
            ((n-1)%50)+1,
            ((n-1)%12)+1,
            ELT((n%7)+1,'Manassas Yard','Woodbridge I-95','Alexandria Medical Zone','Dulles Toll Road','Fairfax Delivery Zone','Arlington Urban Core','Washington DC Service Zone'),
            DATE_SUB(NOW(), INTERVAL n*2 HOUR),
            DATE_SUB(NOW(), INTERVAL n*2-1 HOUR),
            ROUND(8 + (n%45), 1),
            ROUND((8+(n%45)) * 0.006, 3),
            ROUND((8+(n%45)) * 0.006 * 3.75, 2),
            'USD',
            IF(n%3=0,'Excessive', IF(n%5=0,'Warning','Normal')),
            IF(n%3=0, 72+(n%20), 18+(n%30)),
            IF(n%3=0,'Idle cost leakage detected — coach driver on idling policy',IF(n%5=0,'Review idle duration — approaching threshold','Normal idle within policy'))
          FROM seq
          WHERE (SELECT COUNT(*) FROM idling_events) < 30",

        // ---------- FUEL ANOMALIES (12 records) --------------------------------
        @"INSERT INTO fuel_anomalies
            (company_id, fuel_transaction_id, vehicle_id, driver_id, anomaly_type, severity, description, estimated_loss, status)
          WITH RECURSIVE seq(n) AS (SELECT 1 UNION ALL SELECT n+1 FROM seq WHERE n<12)
          SELECT 1,
            ((n-1)%50)+1,
            ((n-1)%20)+1,
            ((n-1)%20)+1,
            ELT((n%6)+1,'Quantity vs Odometer Mismatch','Unusual Station','High Unit Price','Repeated Fill-up','High Cost per Mile','Off-route Purchase'),
            ELT((n%4)+1,'Low','Medium','High','Critical'),
            CONCAT('AI fuel advisor detected anomaly: ', ELT((n%6)+1,'fuel quantity inconsistent with odometer delta','transaction at unusual station outside normal route corridor','unit price 18% above regional average — possible mis-key','duplicate fill-up within 4 hours of prior transaction','cost per mile significantly above fleet average','fuel purchase recorded outside active job route')),
            ROUND(28 + (n%120), 2),
            ELT((n%4)+1,'Open','Under Review','Resolved','Closed')
          FROM seq
          WHERE (SELECT COUNT(*) FROM fuel_anomalies) < 12",

        // ---------- EXPENSE CATEGORIES (10) ------------------------------------
        @"INSERT INTO expense_categories (company_id, category_name, category_type, status)
          SELECT 1, x.n, x.t, 'Active' FROM (
            SELECT 'Fuel' n, 'Operating' t UNION ALL SELECT 'Maintenance','Operating' UNION ALL SELECT 'Toll','Operating'
            UNION ALL SELECT 'Parking','Operating' UNION ALL SELECT 'Driver Reimbursement','HR'
            UNION ALL SELECT 'Carrier Charge','Operations' UNION ALL SELECT 'Insurance','Finance'
            UNION ALL SELECT 'Permit','Compliance' UNION ALL SELECT 'Inspection','Compliance'
            UNION ALL SELECT 'Miscellaneous','Other'
          ) x WHERE NOT EXISTS (SELECT 1 FROM expense_categories WHERE category_name=x.n AND company_id=1)",

        // ---------- EXPENSES: backfill existing + NEW (40) --------------------
        @"UPDATE expenses
          SET expense_number    = COALESCE(expense_number, CONCAT('EXP-', LPAD(id,5,'0'))),
              category_name     = COALESCE(category_name, category),
              approval_status   = COALESCE(approval_status, IF(status='Approved','Approved', IF(id%4=0,'Rejected','Pending'))),
              receipt_status    = COALESCE(receipt_status, IF(id%3=0,'Uploaded','Missing')),
              risk_score        = IF(risk_score=20 AND id%5=0, 65+(id%25), risk_score),
              recommended_action= COALESCE(recommended_action, IF(receipt_status='Missing','Upload receipt before approval','Review and approve expense'))
          WHERE expense_number IS NULL",

        @"INSERT INTO expenses
            (company_id, expense_number, category, title, category_id, category_name, amount, currency,
             expense_date, vehicle_id, driver_id, job_id, route_id, customer_id,
             vendor_name, status, approval_status, receipt_status, risk_score, recommended_action, notes)
          WITH RECURSIVE seq(n) AS (SELECT 1 UNION ALL SELECT n+1 FROM seq WHERE n<40)
          SELECT 1,
            CONCAT('EXP-B5-',2000+n),
            ELT((n%10)+1,'Fuel','Maintenance','Toll','Parking','Driver Reimbursement','Carrier Charge','Insurance','Permit','Inspection','Miscellaneous'),
            CONCAT(ELT((n%10)+1,'Fuel','Maintenance','Toll','Parking','Driver Reimbursement','Carrier Charge','Insurance','Permit','Inspection','Miscellaneous'), ' expense review ', n),
            ((n-1)%10)+1,
            ELT((n%10)+1,'Fuel','Maintenance','Toll','Parking','Driver Reimbursement','Carrier Charge','Insurance','Permit','Inspection','Miscellaneous'),
            ROUND(45 + (n%480), 2),
            'USD',
            DATE_SUB(CURDATE(), INTERVAL n DAY),
            ((n-1)%20)+1,
            ((n-1)%20)+1,
            ((n-1)%50)+1,
            ((n-1)%12)+1,
            ((n-1)%10)+1,
            ELT((n%7)+1,'Shell Fuel Network','NOVA Fleet Care','E-ZPass VA','Diamond Parking','Driver Pool','FastFreight Logistics','AIG Fleet Insurance'),
            IF(n%6=0,'Rejected', IF(n%4=0,'Approved','Pending')),
            IF(n%6=0,'Rejected', IF(n%4=0,'Approved','Pending')),
            IF(n%3=0,'Uploaded', IF(n%5=0,'Pending','Missing')),
            IF(n%7=0, 70+(n%20), 15+(n%35)),
            IF(n%7=0,'Anomaly detected — review amount and receipt','Review and approve expense'),
            IF(n%7=0,'AI flagged as unusual amount for category.', NULL)
          FROM seq
          WHERE (SELECT COUNT(*) FROM expenses WHERE expense_number LIKE 'EXP-B5-%') < 40",

        // ---------- CONTRACTS: backfill existing + NEW (12) -------------------
        @"UPDATE contracts
          SET contract_number         = COALESCE(contract_number, CONCAT('CON-', LPAD(id,5,'0'))),
              base_rate               = IF(base_rate=0, ROUND(1.85 + (id%40)*0.08, 4), base_rate),
              currency                = COALESCE(currency, 'USD'),
              fuel_surcharge_enabled  = IF(id%3=0, TRUE, fuel_surcharge_enabled),
              fuel_surcharge_percent  = IF(id%3=0, ROUND(3+(id%5),2), fuel_surcharge_percent),
              margin_risk             = COALESCE(IF(base_rate < 2.2, 'High', IF(base_rate < 2.8, 'Medium', 'Low')), 'Low'),
              contract_type           = COALESCE(contract_type, ELT((id%3)+1,'Customer','Carrier','Internal'))
          WHERE contract_number IS NULL",

        @"INSERT INTO contracts
            (company_id, contract_code, title, contract_number, customer_id, carrier_id, contract_type, effective_date, expiry_date,
             status, currency, base_rate, rate_type, fuel_surcharge_enabled, fuel_surcharge_percent,
             sla_terms, margin_risk, notes)
          WITH RECURSIVE seq(n) AS (SELECT 1 UNION ALL SELECT n+1 FROM seq WHERE n<12)
          SELECT 1,
            CONCAT('CON-B5-',3000+n),
            CONCAT('CON-B5-',3000+n),
            CONCAT(ELT((n%3)+1,'Customer','Carrier','Internal'), ' rate agreement ', n),
            ((n-1)%10)+1,
            IF(n%3=0, ((n-1)%5)+1, NULL),
            ELT((n%3)+1,'Customer','Carrier','Internal'),
            DATE_SUB(CURDATE(), INTERVAL n*30 DAY),
            IF(n%4=0, DATE_SUB(CURDATE(), INTERVAL 10 DAY), DATE_ADD(CURDATE(), INTERVAL (13-n)*30 DAY)),
            IF(n%4=0,'Expired', IF(n%8=0,'Expiring Soon','Active')),
            'USD',
            ROUND(1.75 + (n%40)*0.09, 4),
            ELT((n%6)+1,'Per Mile','Per Kilometer','Flat Rate','Hourly','Per Stop','Weight Based'),
            n%3=0,
            IF(n%3=0, ROUND(3+(n%5),2), NULL),
            IF(n%2=0,'On-time delivery 96%, 4h ETA window, damage liability per cargo value.', NULL),
            IF(1.75+(n%40)*0.09 < 2.20, 'High', IF(1.75+(n%40)*0.09 < 2.80, 'Medium', 'Low')),
            IF(n%4=0,'Contract nearing expiry — schedule renewal review.', NULL)
          FROM seq
          WHERE (SELECT COUNT(*) FROM contracts WHERE contract_number LIKE 'CON-B5-%') < 12",

        // ---------- CONTRACT RATES (30 records) --------------------------------
        @"INSERT INTO contract_rates
            (company_id, contract_id, rate_code, rate_type, origin_zone, destination_zone,
             vehicle_type, base_rate, minimum_charge, fuel_surcharge_percent, accessorial_type,
             effective_date, expiry_date, status)
          WITH RECURSIVE seq(n) AS (SELECT 1 UNION ALL SELECT n+1 FROM seq WHERE n<30)
          SELECT 1,
            ((n-1)%12)+1,
            CONCAT('RATE-',LPAD(n,4,'0')),
            ELT((n%6)+1,'Per Mile','Per Kilometer','Flat Rate','Hourly','Per Stop','Weight Based'),
            ELT((n%5)+1,'Northern VA','DC Metro','Southern VA','Maryland','West VA'),
            ELT((n%5)+1,'DC Metro','Northern VA','Maryland','West VA','Southern VA'),
            ELT((n%4)+1,'Truck','Van','Reefer','Flatbed'),
            ROUND(1.45 + (n%60)*0.045, 4),
            ROUND(85 + (n%90), 2),
            IF(n%3=0, ROUND(3+(n%5),2), NULL),
            IF(n%4=0, ELT((n%3)+1,'Liftgate','Fuel Surcharge','Detention'), NULL),
            DATE_SUB(CURDATE(), INTERVAL n*15 DAY),
            DATE_ADD(CURDATE(), INTERVAL (31-n)*20 DAY),
            IF(n%8=0,'Inactive','Active')
          FROM seq
          WHERE (SELECT COUNT(*) FROM contract_rates) < 30",

        // ---------- CARRIERS: backfill existing + NEW (10) --------------------
        @"UPDATE carriers
          SET carrier_number      = COALESCE(carrier_number, CONCAT('CAR-', LPAD(id,5,'0'))),
              region              = COALESCE(region, ELT((id%5)+1,'Northern VA','DC Metro','Southern VA','Maryland','National')),
              compliance_status   = COALESCE(compliance_status, IF(id%5=0,'Non-Compliant', IF(id%4=0,'At Risk','Compliant'))),
              insurance_expiry    = COALESCE(insurance_expiry, DATE_ADD(CURDATE(), INTERVAL (id%18) MONTH)),
              contract_status     = COALESCE(contract_status, IF(id%6=0,'Expired','Active')),
              on_time_percent     = IF(on_time_percent=90, GREATEST(72,97-(id%3)*8), on_time_percent),
              safety_score        = IF(safety_score=88, GREATEST(68,96-(id%4)*7), safety_score),
              performance_score   = IF(performance_score=86, GREATEST(70,95-(id%4)*6), performance_score),
              risk_score          = IF(risk_score=20, 12+(id%6)*10, risk_score),
              recommended_action  = COALESCE(recommended_action, IF(compliance_status='Non-Compliant','Suspend carrier — compliance risk',IF(insurance_expiry<DATE_ADD(CURDATE(),INTERVAL 60 DAY),'Renew insurance immediately','Monitor performance')))
          WHERE carrier_number IS NULL",

        @"INSERT INTO carriers
            (company_id, carrier_number, name, mc_number, contact_name, phone, email, region, status,
             compliance_status, insurance_expiry, contract_status, on_time_percent, safety_score,
             cost_score, performance_score, risk_score, recommended_action, notes)
          WITH RECURSIVE seq(n) AS (SELECT 1 UNION ALL SELECT n+1 FROM seq WHERE n<10)
          SELECT 1,
            CONCAT('CAR-B5-',4000+n),
            CONCAT(ELT((n%5)+1,'FastLane','PrimeHaul','NovaTrans','BlueStar','EagleFreight'),' Logistics'),
            CONCAT('MC-',100000+n*7),
            ELT((n%5)+1,'Mike Harrison','Susan Park','David Wright','Amy Torres','James Clark'),
            CONCAT('571-',LPAD(n*37,3,'0'),'-',LPAD(n*91,4,'0')),
            LOWER(CONCAT(ELT((n%5)+1,'fastlane','primehavl','novatrans','bluestar','eaglefreight'),'@carrier.example.com')),
            ELT((n%5)+1,'Northern VA','DC Metro','Southern VA','Maryland','National'),
            IF(n%5=0,'Suspended', IF(n%4=0,'Pending','Active')),
            IF(n%5=0,'Non-Compliant', IF(n%4=0,'At Risk','Compliant')),
            DATE_ADD(CURDATE(), INTERVAL (n%18) MONTH),
            IF(n%6=0,'Expired','Active'),
            GREATEST(72, 97-(n%4)*7),
            GREATEST(68, 95-(n%4)*6),
            GREATEST(70, 94-(n%3)*6),
            GREATEST(70, 93-(n%4)*6),
            12+(n%6)*10,
            IF(n%5=0,'Suspend carrier — compliance risk',IF(n%4=0,'Review carrier compliance before next tender','Monitor carrier performance quarterly')),
            IF(n%5=0,'Carrier suspended pending compliance review.', NULL)
          FROM seq
          WHERE (SELECT COUNT(*) FROM carriers WHERE carrier_number LIKE 'CAR-B5-%') < 10",

        // ---------- CARRIER DOCUMENTS (15 records) ----------------------------
        @"INSERT INTO carrier_documents
            (company_id, carrier_id, document_type, document_number, expiry_date, status)
          WITH RECURSIVE seq(n) AS (SELECT 1 UNION ALL SELECT n+1 FROM seq WHERE n<15)
          SELECT 1,
            ((n-1)%10)+1,
            ELT((n%5)+1,'Operating Authority','Insurance Certificate','Safety Rating','MC Number','DOT Registration'),
            CONCAT('DOC-CAR-',LPAD(n,4,'0')),
            DATE_ADD(CURDATE(), INTERVAL (n%18-3) MONTH),
            IF(n%5=0,'Expired', IF(n%6=0,'Expiring','Active'))
          FROM seq
          WHERE (SELECT COUNT(*) FROM carrier_documents) < 15",

        // ---------- CARRIER PERFORMANCE (10 records) -------------------------
        @"INSERT INTO carrier_performance
            (company_id, carrier_id, period_start, period_end, jobs_handled, on_time_percent, incident_count, expense_total, performance_score)
          WITH RECURSIVE seq(n) AS (SELECT 1 UNION ALL SELECT n+1 FROM seq WHERE n<10)
          SELECT 1,
            ((n-1)%10)+1,
            DATE_SUB(CURDATE(), INTERVAL n MONTH),
            DATE_SUB(CURDATE(), INTERVAL n-1 MONTH),
            12+(n%28),
            GREATEST(70, 97-(n%4)*6),
            n%4,
            ROUND(2800+(n%3200), 2),
            GREATEST(68, 95-(n%5)*5)
          FROM seq
          WHERE (SELECT COUNT(*) FROM carrier_performance) < 10",

        // ---------- COST MARGIN RECORDS (40 records) --------------------------
        @"INSERT INTO cost_margin_records
            (company_id, entity_type, entity_id, customer_id, job_id, route_id, vehicle_id, driver_id,
             revenue_estimate, fuel_cost, driver_cost, maintenance_cost, carrier_cost, expense_total,
             delay_cost, idle_cost, total_cost, margin_estimate, margin_percent, margin_risk)
          WITH RECURSIVE seq(n) AS (SELECT 1 UNION ALL SELECT n+1 FROM seq WHERE n<40)
          SELECT 1,
            ELT((n%4)+1,'job','route','vehicle','customer'),
            ((n-1)%50)+1,
            ((n-1)%10)+1,
            IF(n%4=1, ((n-1)%50)+1, NULL),
            IF(n%4=2, ((n-1)%12)+1, NULL),
            IF(n%4=3, ((n-1)%20)+1, NULL),
            ((n-1)%20)+1,
            ROUND(480 + (n%520), 2),
            ROUND(88  + (n%120), 2),
            ROUND(120 + (n%80),  2),
            ROUND(22  + (n%55),  2),
            ROUND(IF(n%3=0, 85+(n%120), 0), 2),
            ROUND(38  + (n%75),  2),
            ROUND(IF(n%5=0, 45+(n%80), 0), 2),
            ROUND(IF(n%4=0, 18+(n%35), 0), 2),
            ROUND((88+(n%120)) + (120+(n%80)) + (22+(n%55)) + (38+(n%75)), 2),
            ROUND((480+(n%520)) - ((88+(n%120))+(120+(n%80))+(22+(n%55))+(38+(n%75))), 2),
            ROUND(((480+(n%520)) - ((88+(n%120))+(120+(n%80))+(22+(n%55))+(38+(n%75)))) / NULLIF(480+(n%520),0) * 100, 2),
            IF(((480+(n%520)) - ((88+(n%120))+(120+(n%80))+(22+(n%55))+(38+(n%75)))) / NULLIF(480+(n%520),0) < 0.15, 'High', IF(((480+(n%520)) - ((88+(n%120))+(120+(n%80))+(22+(n%55))+(38+(n%75)))) / NULLIF(480+(n%520),0) < 0.28, 'Medium', 'Low'))
          FROM seq
          WHERE (SELECT COUNT(*) FROM cost_margin_records) < 40",

        // ---------- COST MARGIN PREDICTIONS (20 records) ----------------------
        @"INSERT INTO cost_margin_predictions
            (company_id, entity_type, entity_id, prediction_type, predicted_revenue, predicted_cost,
             predicted_margin, predicted_margin_percent, confidence_level, risk_level, recommendation)
          WITH RECURSIVE seq(n) AS (SELECT 1 UNION ALL SELECT n+1 FROM seq WHERE n<20)
          SELECT 1,
            ELT((n%4)+1,'job','route','vehicle','customer'),
            ((n-1)%50)+1,
            ELT((n%3)+1,'Margin Forecast','Cost Trend','Revenue Risk'),
            ROUND(500+(n%480), 2),
            ROUND(320+(n%280), 2),
            ROUND((500+(n%480)) - (320+(n%280)), 2),
            ROUND(((500+(n%480)) - (320+(n%280))) / NULLIF(500+(n%480),0) * 100, 2),
            ELT((n%3)+1,'High','Medium','Low'),
            IF(((500+(n%480)) - (320+(n%280))) / NULLIF(500+(n%480),0) < 0.12,'High', IF(((500+(n%480)) - (320+(n%280))) / NULLIF(500+(n%480),0) < 0.25,'Medium','Low')),
            ELT((n%5)+1,'Review pricing with customer to recover margin','Reduce idle time to lower cost burden','Optimize route sequence to cut fuel spend','Renegotiate carrier rate for this lane','Review accessorial charges for unbilled items')
          FROM seq
          WHERE (SELECT COUNT(*) FROM cost_margin_predictions) < 20",

        // ---------- COST LEAKAGE ITEMS (25 records) ---------------------------
        @"INSERT INTO cost_leakage_items
            (company_id, leakage_number, category, entity_type, entity_id, title, description,
             estimated_loss, projected_monthly_loss, severity, status, owner_role, recommended_action)
          WITH RECURSIVE seq(n) AS (SELECT 1 UNION ALL SELECT n+1 FROM seq WHERE n<25)
          SELECT 1,
            CONCAT('LEAK-',1000+n),
            ELT((n%12)+1,'Idle Time','Fuel Anomaly','Delay / SLA Penalty','Repeated Maintenance','Underpriced Contract','Unbilled Accessorial','Carrier Overcharge','High-Cost Driver','High-Cost Vehicle','Route Inefficiency','Missing Customer Update','Document/Compliance Risk'),
            ELT((n%4)+1,'vehicle','driver','job','route'),
            ((n-1)%20)+1,
            CONCAT(ELT((n%12)+1,'Excessive idle on','Fuel anomaly on','Delay penalty on','Repeat brake job on','Underpriced contract — ','Missing liftgate fee on','Carrier overcharge on','High fuel cost driver — ','High maintenance cost — ','Inefficient stop sequence on','ETA update missing for','Expired document risk —'),' ',ELT((n%4)+1,'vehicle','driver','job','route'),' #',((n-1)%20)+1),
            ELT((n%5)+1,'AI cost advisor identified leakage pattern from operational data — action recommended to recover margin.','Idle cost accumulation detected above threshold — coaching and scheduling adjustment advised.','Recurring charge pattern requires finance review and corrective pricing.','Carrier invoice variance exceeds contracted rate — dispute or renegotiation required.','Compliance document approaching expiry — potential fine risk and operational exposure.'),
            ROUND(85+(n%820), 2),
            ROUND((85+(n%820))*0.95, 2),
            ELT((n%4)+1,'Low','Medium','High','Critical'),
            ELT((n%4)+1,'Open','Acknowledged','In Progress','Resolved'),
            ELT((n%5)+1,'Fleet Manager','Finance Manager','Compliance Manager','Dispatch Manager','Company Admin'),
            ELT((n%6)+1,'Coach driver on idle policy and set threshold alerts','Investigate fuel transaction and review driver log','Dispute carrier invoice and update rate schedule','Review contract pricing against actual cost per mile','Add accessorial fee to next customer invoice','Schedule compliance renewal immediately')
          FROM seq
          WHERE (SELECT COUNT(*) FROM cost_leakage_items) < 25",

        // ---------- COST LEAKAGE ACTIONS (25 records) -------------------------
        @"INSERT INTO cost_leakage_actions
            (company_id, cost_leakage_item_id, action_title, action_description, estimated_savings,
             status, assigned_to_user_id, due_at)
          WITH RECURSIVE seq(n) AS (SELECT 1 UNION ALL SELECT n+1 FROM seq WHERE n<25)
          SELECT 1,
            ((n-1)%25)+1,
            CONCAT('Recovery action ',n,' — ',ELT((n%6)+1,'Reduce idle time','Dispute overcharge','Renegotiate rate','Add accessorial fee','Schedule compliance renewal','Coach driver')),
            CONCAT('Implement corrective action to recover estimated $',ROUND(120+(n%680),0),' monthly leakage. Review evidence and assign owner.'),
            ROUND(120+(n%680), 2),
            ELT((n%4)+1,'Open','In Progress','Completed','Cancelled'),
            1,
            DATE_ADD(NOW(), INTERVAL n*3 DAY)
          FROM seq
          WHERE (SELECT COUNT(*) FROM cost_leakage_actions) < 25",

        // ---------- AI RECOMMENDATIONS for Batch 5 modules --------------------
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

        // ---------- NOTIFICATIONS for Batch 5 --------------------------------
        @"INSERT INTO notifications (company_id, title, body, severity, module_key, status)
          SELECT 1, x.title, x.body, x.severity, x.module_key, 'Unread' FROM (
            SELECT 'Fuel anomaly detected' title,'AI detected possible fuel card misuse — review FT-B5-1009.' body,'High' severity,'fuel-idling' module_key
            UNION ALL SELECT 'Expense approval needed','9 expenses pending manager approval.','Warning','expenses'
            UNION ALL SELECT 'Contract expiring','CON-B5-3004 expires in 8 days — initiate renewal.','High','contracts-rates'
            UNION ALL SELECT 'Carrier compliance risk','CAR-B5-4005 is Non-Compliant — review before next tender.','Critical','carrier-management'
            UNION ALL SELECT 'Margin risk alert','6 jobs below 15% margin target.','High','predictive-margin'
            UNION ALL SELECT 'Cost leakage detected','$12,400 recoverable leakage identified this month.','Warning','cost-leakage'
          ) x WHERE NOT EXISTS (SELECT 1 FROM notifications n WHERE n.title=x.title AND n.module_key=x.module_key)",

        // ---------- AUDIT LOGS for Batch 5 seed actions ----------------------
        @"INSERT INTO audit_logs (company_id, action_name, entity_type, entity_id, actor_name, created_at)
          SELECT 1, x.action_name, x.entity_type, x.entity_id, 'OpsTrax System Seed', x.ts FROM (
            SELECT 'fuel.transaction.created' action_name,'FuelTransaction' entity_type,1 entity_id, DATE_SUB(NOW(), INTERVAL 2 HOUR) ts
            UNION ALL SELECT 'expense.created','Expense',1, DATE_SUB(NOW(), INTERVAL 3 HOUR)
            UNION ALL SELECT 'contract.created','Contract',1, DATE_SUB(NOW(), INTERVAL 4 HOUR)
            UNION ALL SELECT 'carrier.created','Carrier',1, DATE_SUB(NOW(), INTERVAL 5 HOUR)
            UNION ALL SELECT 'cost.leakage.detected','CostLeakage',1, DATE_SUB(NOW(), INTERVAL 6 HOUR)
          ) x WHERE (SELECT COUNT(*) FROM audit_logs WHERE action_name LIKE 'fuel.%' OR action_name LIKE 'expense.%') < 5",
    ];
}
