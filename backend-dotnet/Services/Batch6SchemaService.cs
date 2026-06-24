using Opstrax.Api.Data;

namespace Opstrax.Api.Services;

public sealed class Batch6SchemaService(Database db)
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
        new("documents",    "country_code",          "VARCHAR(10) NULL"),
        new("documents",    "issuing_authority",     "VARCHAR(200) NULL"),
        new("documents",    "issued_at",             "DATE NULL"),
        new("dvir_reports", "country_code",          "VARCHAR(10) NULL"),
        new("dvir_reports", "compliance_profile_id", "BIGINT NULL"),
        new("hos_logs",     "vehicle_id",            "BIGINT NULL"),
        new("hos_logs",     "country_code",          "VARCHAR(10) NOT NULL DEFAULT 'US'"),
        new("hos_logs",     "profile_id",            "BIGINT NULL"),
        new("hos_logs",     "start_time",            "TIMESTAMPTZ NULL"),
        new("hos_logs",     "end_time",              "TIMESTAMPTZ NULL"),
        new("hos_logs",     "duration_minutes",      "INT NOT NULL DEFAULT 0"),
        new("hos_logs",     "location",              "VARCHAR(200) NULL"),
        new("hos_logs",     "is_certified",          "BOOLEAN NOT NULL DEFAULT false"),
        new("ai_recommendations", "description",     "TEXT NULL"),
        new("ai_recommendations", "priority",        "VARCHAR(40) NULL"),
        new("ai_recommendations", "action_label",      "VARCHAR(160) NULL"),
        new("ai_recommendations", "action_type",      "VARCHAR(120) NULL"),
        new("countries",           "hos_ruleset",      "VARCHAR(80) NULL"),
        new("countries",           "rtl",              "BOOLEAN NOT NULL DEFAULT false"),
        new("compliance_profiles", "max_driving_hours","INT NULL"),
        new("compliance_profiles", "max_duty_hours",   "INT NULL"),
        new("compliance_profiles", "rest_requirement_hours", "INT NULL"),
        new("compliance_rules",    "severity",         "VARCHAR(50) NOT NULL DEFAULT 'High'"),
        new("compliance_rules",    "threshold_value",  "DECIMAL(12,2) NULL"),
        new("compliance_rules",    "threshold_unit",   "VARCHAR(40) NULL"),
        new("hos_clocks",          "hos_warning",      "TEXT NULL")
    ];

    private static readonly string[] Tables =
    [
        @"CREATE TABLE IF NOT EXISTS countries (
            id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
            code VARCHAR(10) NOT NULL UNIQUE,
            name VARCHAR(200) NOT NULL,
            currency VARCHAR(10) NOT NULL DEFAULT 'USD',
            distance_unit VARCHAR(20) NOT NULL DEFAULT 'Miles',
            volume_unit VARCHAR(20) NOT NULL DEFAULT 'Gallons',
            hos_ruleset VARCHAR(80) NULL,
            rtl BOOLEAN NOT NULL DEFAULT false,
            created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
        )",

        @"CREATE TABLE IF NOT EXISTS languages (
            id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
            code VARCHAR(10) NOT NULL UNIQUE,
            name VARCHAR(100) NOT NULL,
            native_name VARCHAR(100) NOT NULL,
            country_code VARCHAR(10) NULL,
            rtl BOOLEAN NOT NULL DEFAULT false,
            created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
        )",

        @"CREATE TABLE IF NOT EXISTS tenant_locale_settings (
            id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
            tenant_id BIGINT NULL,
            default_language VARCHAR(10) NOT NULL DEFAULT 'en-US',
            default_country VARCHAR(10) NOT NULL DEFAULT 'US',
            timezone VARCHAR(80) NOT NULL DEFAULT 'America/New_York',
            date_format VARCHAR(40) NOT NULL DEFAULT 'MM/DD/YYYY',
            currency VARCHAR(10) NOT NULL DEFAULT 'USD',
            distance_unit VARCHAR(20) NOT NULL DEFAULT 'Miles',
            volume_unit VARCHAR(20) NOT NULL DEFAULT 'Gallons',
            created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
            updated_at TIMESTAMPTZ NULL
        )",

        @"CREATE TABLE IF NOT EXISTS user_locale_preferences (
            id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
            user_id BIGINT NULL,
            language VARCHAR(10) NOT NULL DEFAULT 'en-US',
            country_code VARCHAR(10) NULL,
            timezone VARCHAR(80) NULL,
            date_format VARCHAR(40) NULL,
            created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
            updated_at TIMESTAMPTZ NULL
        )",

        @"CREATE TABLE IF NOT EXISTS compliance_profiles (
            id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
            country_code VARCHAR(10) NOT NULL,
            profile_name VARCHAR(200) NOT NULL,
            authority VARCHAR(200) NULL,
            hos_ruleset VARCHAR(80) NULL,
            eld_required BOOLEAN NOT NULL DEFAULT false,
            max_driving_hours DECIMAL(5,2) NULL,
            max_duty_hours DECIMAL(5,2) NULL,
            rest_requirement_hours DECIMAL(5,2) NULL,
            notes TEXT NULL,
            is_active BOOLEAN NOT NULL DEFAULT true,
            created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
            updated_at TIMESTAMPTZ NULL
        )",

        @"CREATE TABLE IF NOT EXISTS compliance_rules (
            id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
            profile_id BIGINT NOT NULL,
            rule_code VARCHAR(80) NOT NULL,
            rule_name VARCHAR(200) NOT NULL,
            category VARCHAR(80) NOT NULL DEFAULT 'HOS',
            description TEXT NULL,
            severity VARCHAR(40) NOT NULL DEFAULT 'Medium',
            threshold_value DECIMAL(10,2) NULL,
            threshold_unit VARCHAR(40) NULL,
            is_active BOOLEAN NOT NULL DEFAULT true,
            created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
        )",

        @"CREATE TABLE IF NOT EXISTS driver_compliance_status (
            id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
            driver_id BIGINT NOT NULL,
            country_code VARCHAR(10) NOT NULL DEFAULT 'US',
            profile_id BIGINT NULL,
            overall_status VARCHAR(80) NOT NULL DEFAULT 'Compliant',
            license_valid BOOLEAN NOT NULL DEFAULT true,
            license_expiry DATE NULL,
            medical_cert_valid BOOLEAN NOT NULL DEFAULT true,
            medical_cert_expiry DATE NULL,
            drug_test_valid BOOLEAN NOT NULL DEFAULT true,
            drug_test_expiry DATE NULL,
            hos_status VARCHAR(80) NOT NULL DEFAULT 'OK',
            violation_count INT NOT NULL DEFAULT 0,
            last_audit_date DATE NULL,
            notes TEXT NULL,
            updated_at TIMESTAMPTZ NULL
        )",

        @"CREATE TABLE IF NOT EXISTS vehicle_compliance_status (
            id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
            vehicle_id BIGINT NOT NULL,
            country_code VARCHAR(10) NOT NULL DEFAULT 'US',
            profile_id BIGINT NULL,
            overall_status VARCHAR(80) NOT NULL DEFAULT 'Compliant',
            registration_valid BOOLEAN NOT NULL DEFAULT true,
            registration_expiry DATE NULL,
            insurance_valid BOOLEAN NOT NULL DEFAULT true,
            insurance_expiry DATE NULL,
            inspection_valid BOOLEAN NOT NULL DEFAULT true,
            inspection_expiry DATE NULL,
            eld_installed BOOLEAN NOT NULL DEFAULT false,
            eld_device_id BIGINT NULL,
            violation_count INT NOT NULL DEFAULT 0,
            last_audit_date DATE NULL,
            notes TEXT NULL,
            updated_at TIMESTAMPTZ NULL
        )",

        @"CREATE TABLE IF NOT EXISTS hos_logs (
            id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
            driver_id BIGINT NOT NULL,
            vehicle_id BIGINT NULL,
            log_date DATE NOT NULL,
            country_code VARCHAR(10) NOT NULL DEFAULT 'US',
            profile_id BIGINT NULL,
            status VARCHAR(80) NOT NULL DEFAULT 'Off Duty',
            start_time TIMESTAMPTZ NOT NULL DEFAULT NOW(),
            end_time TIMESTAMPTZ NULL,
            duration_minutes INT NOT NULL DEFAULT 0,
            odometer_start DECIMAL(12,2) NULL,
            odometer_end DECIMAL(12,2) NULL,
            location VARCHAR(200) NULL,
            notes TEXT NULL,
            is_certified BOOLEAN NOT NULL DEFAULT false,
            certified_at TIMESTAMPTZ NULL,
            created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
            updated_at TIMESTAMPTZ NULL,
            deleted_at TIMESTAMPTZ NULL
        )",

        @"CREATE TABLE IF NOT EXISTS hos_clocks (
            id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
            driver_id BIGINT NOT NULL,
            country_code VARCHAR(10) NOT NULL DEFAULT 'US',
            profile_id BIGINT NULL,
            cycle_type VARCHAR(80) NOT NULL DEFAULT '70hr/8day',
            drive_time_remaining_minutes INT NOT NULL DEFAULT 660,
            shift_time_remaining_minutes INT NOT NULL DEFAULT 840,
            cycle_time_remaining_minutes INT NOT NULL DEFAULT 4200,
            break_needed_at TIMESTAMPTZ NULL,
            reset_at TIMESTAMPTZ NULL,
            status VARCHAR(80) NOT NULL DEFAULT 'OK',
            hos_warning VARCHAR(200) NULL,
            updated_at TIMESTAMPTZ NULL
        )",

        @"CREATE TABLE IF NOT EXISTS eld_devices (
            id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
            device_serial VARCHAR(120) NOT NULL UNIQUE,
            device_model VARCHAR(120) NULL,
            provider VARCHAR(120) NULL,
            vehicle_id BIGINT NULL,
            driver_id BIGINT NULL,
            status VARCHAR(80) NOT NULL DEFAULT 'Active',
            malfunction_code VARCHAR(80) NULL,
            malfunction_description TEXT NULL,
            last_sync_at TIMESTAMPTZ NULL,
            firmware_version VARCHAR(80) NULL,
            notes TEXT NULL,
            created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
            updated_at TIMESTAMPTZ NULL,
            deleted_at TIMESTAMPTZ NULL
        )",

        @"CREATE TABLE IF NOT EXISTS compliance_violations (
            id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
            violation_code VARCHAR(80) NOT NULL,
            rule_id BIGINT NULL,
            profile_id BIGINT NULL,
            country_code VARCHAR(10) NOT NULL DEFAULT 'US',
            driver_id BIGINT NULL,
            vehicle_id BIGINT NULL,
            category VARCHAR(80) NOT NULL DEFAULT 'HOS',
            description TEXT NULL,
            severity VARCHAR(40) NOT NULL DEFAULT 'Medium',
            status VARCHAR(80) NOT NULL DEFAULT 'Open',
            detected_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
            resolved_at TIMESTAMPTZ NULL,
            resolution_notes TEXT NULL,
            created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
            updated_at TIMESTAMPTZ NULL
        )",

        @"CREATE TABLE IF NOT EXISTS compliance_audit_packages (
            id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
            package_code VARCHAR(80) NOT NULL UNIQUE,
            country_code VARCHAR(10) NOT NULL DEFAULT 'US',
            profile_id BIGINT NULL,
            created_by VARCHAR(120) NULL,
            status VARCHAR(80) NOT NULL DEFAULT 'Draft',
            included_drivers INT NOT NULL DEFAULT 0,
            included_vehicles INT NOT NULL DEFAULT 0,
            included_documents INT NOT NULL DEFAULT 0,
            included_violations INT NOT NULL DEFAULT 0,
            hos_logs_count INT NOT NULL DEFAULT 0,
            date_range_start DATE NULL,
            date_range_end DATE NULL,
            notes TEXT NULL,
            created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
            updated_at TIMESTAMPTZ NULL
        )",
    ];

    private static readonly string[] Indexes =
    [
        "CREATE INDEX IF NOT EXISTS idx_compliance_violations_driver ON compliance_violations(driver_id)",
        "CREATE INDEX IF NOT EXISTS idx_compliance_violations_vehicle ON compliance_violations(vehicle_id)",
        "CREATE INDEX IF NOT EXISTS idx_compliance_violations_country ON compliance_violations(country_code)",
        "CREATE INDEX IF NOT EXISTS idx_hos_logs_driver ON hos_logs(driver_id)",
        "CREATE INDEX IF NOT EXISTS idx_hos_logs_date ON hos_logs(log_date)",
        "CREATE INDEX IF NOT EXISTS idx_hos_clocks_driver ON hos_clocks(driver_id)",
        "CREATE INDEX IF NOT EXISTS idx_eld_devices_vehicle ON eld_devices(vehicle_id)",
        "CREATE INDEX IF NOT EXISTS idx_driver_compliance_driver ON driver_compliance_status(driver_id)",
        "CREATE INDEX IF NOT EXISTS idx_vehicle_compliance_vehicle ON vehicle_compliance_status(vehicle_id)",
    ];

    private static readonly string[] Seeds =
    [
        @"INSERT INTO countries (code,name,currency,distance_unit,volume_unit,hos_ruleset,rtl) VALUES
          ('US','United States','USD','Miles','Gallons','FMCSA 395.3',false),
          ('CA','Canada','CAD','Kilometers','Liters','TC NSC',false),
          ('SA','Saudi Arabia','SAR','Kilometers','Liters','SASO HOS',true),
          ('AE','United Arab Emirates','AED','Kilometers','Liters','UAE RTA',true),
          ('PK','Pakistan','PKR','Kilometers','Liters','NHA Regulations',false)
          ON CONFLICT DO NOTHING",

        @"INSERT INTO languages (code,name,native_name,country_code,rtl) VALUES
          ('en-US','English (US)','English (US)','US',false),
          ('en-CA','English (Canada)','English (Canada)','CA',false),
          ('fr-CA','French (Canada)','Français (Canada)','CA',false),
          ('ar-SA','Arabic (Saudi Arabia)','العربية (السعودية)','SA',true),
          ('ar-AE','Arabic (UAE)','العربية (الإمارات)','AE',true),
          ('ur-PK','Urdu (Pakistan)','اردو (پاکستان)','PK',true)
          ON CONFLICT DO NOTHING",

        @"INSERT INTO tenant_locale_settings (id,tenant_id,default_language,default_country,timezone,date_format,currency,distance_unit,volume_unit)
          OVERRIDING SYSTEM VALUE
          SELECT 1,1,'en-US','US','America/New_York','MM/DD/YYYY','USD','Miles','Gallons'
          WHERE NOT EXISTS (SELECT 1 FROM tenant_locale_settings WHERE id=1)
          ON CONFLICT DO NOTHING",

        @"INSERT INTO compliance_profiles (id,country_code,profile_name,authority,hos_ruleset,eld_required,max_driving_hours,max_duty_hours,rest_requirement_hours) OVERRIDING SYSTEM VALUE VALUES
          (1,'US','FMCSA Property Carrier','FMCSA','395.3 Property',true,11,14,10),
          (2,'US','FMCSA Passenger Carrier','FMCSA','395.3 Passenger',true,10,15,8),
          (3,'CA','Transport Canada NSC','Transport Canada','NSC',true,13,14,8),
          (4,'SA','Saudi Arabia HOS','SASO','SASO HOS',false,10,14,8),
          (5,'AE','UAE RTA Compliance','UAE RTA','UAE HOS',false,10,12,8),
          (6,'PK','NHA Pakistan Compliance','NHA Pakistan','NHA',false,10,12,8)
          ON CONFLICT DO NOTHING",

        @"INSERT INTO compliance_rules (id,profile_id,rule_code,rule_name,category,description,severity,threshold_value,threshold_unit) OVERRIDING SYSTEM VALUE VALUES
          (1,1,'FMCSA-HOS-11H','11-Hour Driving Limit','HOS','Driver cannot drive more than 11 hours after 10 consecutive hours off duty','Critical',11,'Hours'),
          (2,1,'FMCSA-HOS-14H','14-Hour Window','HOS','Driver cannot drive after the 14th hour following 10 hours off duty','Critical',14,'Hours'),
          (3,1,'FMCSA-HOS-70H','70-Hour / 8-Day Limit','HOS','Driver may not drive after 70 hours on duty in 8 consecutive days','High',70,'Hours'),
          (4,1,'FMCSA-ELD-CERT','ELD Certification','ELD','Vehicle must have an FMCSA registered ELD device installed','High',NULL,NULL),
          (5,1,'FMCSA-DOC-CDL','CDL Requirement','Documents','Commercial Driver License must be valid and current','Critical',NULL,NULL),
          (6,3,'TC-HOS-13H','13-Hour Driving Limit','HOS','Canadian NSC: Maximum 13 hours driving in a day','Critical',13,'Hours'),
          (7,3,'TC-NSC-CARRIER','NSC Carrier Certification','Documents','Transport Canada NSC carrier registration required','High',NULL,NULL),
          (8,4,'SA-HOS-10H','Saudi Arabia 10-Hour Limit','HOS','Maximum 10 hours driving per day under SASO regulations','High',10,'Hours'),
          (9,5,'AE-HOS-10H','UAE 10-Hour Driving Limit','HOS','UAE RTA: Maximum 10 hours driving per day','High',10,'Hours'),
          (10,6,'PK-NHA-10H','Pakistan 10-Hour Limit','HOS','NHA: Maximum 10 hours driving per day','Medium',10,'Hours')
          ON CONFLICT DO NOTHING",

        @"INSERT INTO hos_clocks (id,driver_id,country_code,profile_id,cycle_type,drive_time_remaining_minutes,shift_time_remaining_minutes,cycle_time_remaining_minutes,status,hos_warning) OVERRIDING SYSTEM VALUE VALUES
          (1,1,'US',1,'70hr/8day',480,620,3900,'OK',NULL),
          (2,2,'US',1,'70hr/8day',55,90,240,'Warning','Approaching drive limit'),
          (3,3,'US',1,'70hr/8day',660,840,4200,'OK',NULL),
          (4,4,'US',1,'70hr/8day',0,120,900,'Violation','11-hour driving limit exceeded'),
          (5,5,'US',1,'70hr/8day',300,480,2400,'OK',NULL),
          (6,6,'CA',3,'13hr/day',540,660,3600,'OK',NULL),
          (7,7,'CA',3,'13hr/day',90,150,600,'Warning','Approaching duty limit'),
          (8,8,'SA',4,'10hr/day',420,540,2800,'OK',NULL),
          (9,9,'AE',5,'10hr/day',360,480,2600,'OK',NULL),
          (10,10,'PK',6,'10hr/day',300,420,2200,'OK',NULL)
          ON CONFLICT DO NOTHING",

        @"INSERT INTO hos_logs (id,driver_id,vehicle_id,log_date,country_code,profile_id,status,start_time,end_time,duration_minutes,location,is_certified) VALUES
          (1,1,1,'2026-05-20','US',1,'Driving','2026-05-20 06:00:00','2026-05-20 12:00:00',360,'Manassas, VA',true),
          (2,1,1,'2026-05-20','US',1,'On Duty (Not Driving)','2026-05-20 12:00:00','2026-05-20 12:30:00',30,'Woodbridge, VA',true),
          (3,1,1,'2026-05-20','US',1,'Driving','2026-05-20 12:30:00','2026-05-20 17:30:00',300,'Alexandria, VA',true),
          (4,2,2,'2026-05-20','US',1,'Driving','2026-05-20 05:00:00','2026-05-20 16:00:00',660,'Dulles, VA',true),
          (5,2,2,'2026-05-20','US',1,'Driving','2026-05-20 16:15:00','2026-05-20 17:25:00',70,'Fairfax, VA',false),
          (6,3,3,'2026-05-21','US',1,'Off Duty','2026-05-21 00:00:00','2026-05-21 08:00:00',480,'Arlington, VA',true),
          (7,3,3,'2026-05-21','US',1,'Driving','2026-05-21 08:00:00','2026-05-21 15:00:00',420,'Washington DC',true),
          (8,4,4,'2026-05-21','US',1,'Driving','2026-05-21 04:00:00','2026-05-21 15:00:00',660,'Manassas, VA',true),
          (9,4,4,'2026-05-21','US',1,'Driving','2026-05-21 15:30:00','2026-05-21 17:30:00',120,'Woodbridge, VA',false),
          (10,5,5,'2026-05-21','US',1,'Driving','2026-05-21 07:00:00','2026-05-21 12:00:00',300,'Alexandria, VA',true),
          (11,6,6,'2026-05-21','CA',3,'Driving','2026-05-21 06:00:00','2026-05-21 15:00:00',540,'Ottawa, ON',true),
          (12,7,7,'2026-05-21','CA',3,'Driving','2026-05-21 05:30:00','2026-05-21 17:00:00',690,'Toronto, ON',false),
          (13,8,8,'2026-05-21','SA',4,'Driving','2026-05-21 07:00:00','2026-05-21 14:00:00',420,'Riyadh, SA',true),
          (14,9,9,'2026-05-21','AE',5,'Driving','2026-05-21 08:00:00','2026-05-21 14:00:00',360,'Dubai, AE',true),
          (15,10,10,'2026-05-21','PK',6,'Driving','2026-05-21 07:00:00','2026-05-21 12:00:00',300,'Karachi, PK',true),
          (16,1,1,'2026-05-22','US',1,'Driving','2026-05-22 06:00:00','2026-05-22 11:00:00',300,'Manassas, VA',true),
          (17,2,2,'2026-05-22','US',1,'Off Duty','2026-05-22 00:00:00','2026-05-22 10:00:00',600,'Rest Stop VA',true),
          (18,3,3,'2026-05-22','US',1,'Driving','2026-05-22 09:00:00','2026-05-22 14:30:00',330,'Dulles, VA',true),
          (19,4,4,'2026-05-22','US',1,'Sleeper Berth','2026-05-22 00:00:00','2026-05-22 10:00:00',600,'Truck Stop VA',true),
          (20,5,5,'2026-05-22','US',1,'Driving','2026-05-22 08:00:00','2026-05-22 13:00:00',300,'Fairfax, VA',true),
          (21,6,6,'2026-05-22','CA',3,'Driving','2026-05-22 07:00:00','2026-05-22 13:00:00',360,'Montreal, QC',true),
          (22,7,7,'2026-05-22','CA',3,'Off Duty','2026-05-22 00:00:00','2026-05-22 08:00:00',480,'Rest Area ON',true),
          (23,8,8,'2026-05-22','SA',4,'Driving','2026-05-22 06:00:00','2026-05-22 13:00:00',420,'Jeddah, SA',true),
          (24,9,9,'2026-05-22','AE',5,'On Duty (Not Driving)','2026-05-22 08:00:00','2026-05-22 09:00:00',60,'Abu Dhabi, AE',true),
          (25,10,10,'2026-05-22','PK',6,'Driving','2026-05-22 07:00:00','2026-05-22 12:30:00',330,'Lahore, PK',true),
          (26,1,1,'2026-05-23','US',1,'Driving','2026-05-23 06:00:00','2026-05-23 12:00:00',360,'Arlington, VA',true),
          (27,2,2,'2026-05-23','US',1,'Driving','2026-05-23 07:00:00','2026-05-23 15:00:00',480,'Washington DC',false),
          (28,3,3,'2026-05-23','US',1,'Driving','2026-05-23 08:00:00','2026-05-23 13:00:00',300,'Woodbridge, VA',false),
          (29,4,4,'2026-05-23','US',1,'Driving','2026-05-23 07:00:00','2026-05-23 12:00:00',300,'Alexandria, VA',false),
          (30,5,5,'2026-05-23','US',1,'Driving','2026-05-23 07:00:00','2026-05-23 12:00:00',300,'Manassas, VA',false)
          ON CONFLICT DO NOTHING",

        @"INSERT INTO eld_devices (id,device_serial,device_model,provider,vehicle_id,driver_id,status,last_sync_at,firmware_version) VALUES
          (1,'ELD-001-TRK101','KeepTruckin M300','Motive',1,1,'Active','2026-05-24 08:00:00','3.4.1'),
          (2,'ELD-002-TRK102','KeepTruckin M300','Motive',2,2,'Active','2026-05-24 07:30:00','3.4.1'),
          (3,'ELD-003-VAN103','Samsara VG34','Samsara',3,3,'Active','2026-05-24 08:15:00','2.9.0'),
          (4,'ELD-004-TRK104','Omnitracs IVG','Omnitracs',4,4,'Malfunction','2026-05-23 11:00:00','4.1.2'),
          (5,'ELD-005-TRK105','KeepTruckin M300','Motive',5,5,'Active','2026-05-24 07:45:00','3.4.1'),
          (6,'ELD-006-VAN106','Samsara VG34','Samsara',6,6,'Active','2026-05-24 08:00:00','2.9.0'),
          (7,'ELD-007-BOX107','Omnitracs IVG','Omnitracs',7,7,'Active','2026-05-24 08:20:00','4.1.2'),
          (8,'ELD-008-TRK108','KeepTruckin M300','Motive',8,8,'Diagnostic','2026-05-24 06:00:00','3.4.0'),
          (9,'ELD-009-TRK109','Samsara VG34','Samsara',9,9,'Active','2026-05-24 08:10:00','2.9.0'),
          (10,'ELD-010-VAN110','Omnitracs IVG','Omnitracs',10,10,'Active','2026-05-24 08:05:00','4.1.2')
          ON CONFLICT DO NOTHING",

        @"INSERT INTO driver_compliance_status (id,driver_id,country_code,profile_id,overall_status,license_valid,license_expiry,medical_cert_valid,medical_cert_expiry,drug_test_valid,drug_test_expiry,hos_status,violation_count) VALUES
          (1,1,'US',1,'Compliant',true,'2028-03-15',true,'2026-09-01',true,'2026-11-15','OK',0),
          (2,2,'US',1,'Warning',true,'2026-07-20',true,'2026-06-01',true,'2026-08-01','Warning',2),
          (3,3,'US',1,'Compliant',true,'2029-01-10',true,'2027-02-15',true,'2027-01-20','OK',0),
          (4,4,'US',1,'Violation',true,'2027-05-30',true,'2026-10-01',true,'2026-09-15','Violation',3),
          (5,5,'US',1,'Compliant',true,'2028-08-22',true,'2026-12-01',true,'2026-10-01','OK',1),
          (6,6,'CA',3,'Compliant',true,'2027-11-30',true,'2027-03-01',true,'2027-02-01','OK',0),
          (7,7,'CA',3,'Warning',true,'2026-06-15',false,'2026-05-30',true,'2026-07-01','Warning',1),
          (8,8,'SA',4,'Compliant',true,'2027-09-01',true,'2027-01-15',true,'2026-12-01','OK',0),
          (9,9,'AE',5,'Compliant',true,'2028-01-20',true,'2027-06-01',true,'2027-05-01','OK',0),
          (10,10,'PK',6,'Compliant',true,'2027-04-10',true,'2026-11-01',true,'2026-10-15','OK',0)
          ON CONFLICT DO NOTHING",

        @"INSERT INTO vehicle_compliance_status (id,vehicle_id,country_code,profile_id,overall_status,registration_valid,registration_expiry,insurance_valid,insurance_expiry,inspection_valid,inspection_expiry,eld_installed,eld_device_id,violation_count) VALUES
          (1,1,'US',1,'Compliant',true,'2027-01-31',true,'2026-09-30',true,'2026-11-15',true,1,0),
          (2,2,'US',1,'Warning',true,'2026-08-31',true,'2026-06-30',true,'2026-07-15',true,2,1),
          (3,3,'US',1,'Compliant',true,'2027-03-31',true,'2026-10-30',true,'2026-12-15',true,3,0),
          (4,4,'US',1,'Violation',true,'2027-05-31',true,'2027-01-30',false,'2026-03-15',true,4,2),
          (5,5,'US',1,'Compliant',true,'2027-07-31',true,'2026-11-30',true,'2027-01-15',true,5,0),
          (6,6,'CA',3,'Compliant',true,'2027-02-28',true,'2026-12-31',true,'2027-02-28',true,6,0),
          (7,7,'CA',3,'Warning',true,'2026-07-31',true,'2026-08-31',true,'2026-06-30',true,7,1),
          (8,8,'SA',4,'Compliant',true,'2027-01-31',true,'2026-10-31',true,'2026-09-30',false,NULL,0),
          (9,9,'AE',5,'Compliant',true,'2027-06-30',true,'2026-12-31',true,'2027-03-31',false,NULL,0),
          (10,10,'PK',6,'Compliant',true,'2027-01-31',true,'2026-11-30',true,'2026-10-31',false,NULL,0)
          ON CONFLICT DO NOTHING",

        @"INSERT INTO compliance_violations (id,violation_code,rule_id,profile_id,country_code,driver_id,vehicle_id,category,description,severity,status,detected_at) VALUES
          (1,'VIO-001',1,1,'US',2,2,'HOS','Driver 2 exceeded 11-hour driving limit by 70 minutes on 2026-05-20','Critical','Open','2026-05-20 17:25:00'),
          (2,'VIO-002',2,1,'US',4,4,'HOS','Driver 4 exceeded 14-hour duty window on 2026-05-21','Critical','Open','2026-05-21 18:00:00'),
          (3,'VIO-003',4,1,'US',NULL,4,'ELD','Vehicle TRK-104 ELD device reporting malfunction (ELD-004)','High','Open','2026-05-23 11:00:00'),
          (4,'VIO-004',5,1,'US',7,NULL,'Documents','Driver 7 CDL expired — medical certificate invalid as of 2026-05-30','High','Open','2026-05-24 00:00:00'),
          (5,'VIO-005',6,3,'CA',7,7,'HOS','Driver 7 exceeded 13-hour Canadian driving limit on 2026-05-21','Critical','Open','2026-05-21 17:00:00'),
          (6,'VIO-006',7,3,'CA',NULL,7,'Documents','Vehicle 7 insurance expiring in 24 days','Medium','Acknowledged','2026-05-24 00:00:00'),
          (7,'VIO-007',3,1,'US',2,NULL,'HOS','Driver 2 approaching 70-hour/8-day cycle limit — 240 minutes remaining','Medium','Open','2026-05-23 08:00:00'),
          (8,'VIO-008',1,1,'US',4,4,'HOS','Repeated 11-hour violation — pattern identified','Critical','Under Review','2026-05-22 19:00:00'),
          (9,'VIO-009',5,1,'US',2,2,'Documents','Driver 2 medical certificate expiring in 8 days','High','Open','2026-05-24 00:00:00'),
          (10,'VIO-010',2,1,'US',4,4,'HOS','Driver 4 14-hour window repeat violation','Critical','Escalated','2026-05-22 18:30:00'),
          (11,'VIO-011',8,4,'SA',8,8,'HOS','Saudi Arabia HOS: monitoring flag — 10-hour limit tracking','Low','Resolved','2026-05-20 14:00:00'),
          (12,'VIO-012',9,5,'AE',9,9,'HOS','UAE RTA: monitoring alert — tracking 10-hour threshold','Low','Resolved','2026-05-21 14:30:00'),
          (13,'VIO-013',4,1,'US',NULL,2,'ELD','Vehicle TRK-102 ELD connectivity warning — intermittent signal','Medium','Open','2026-05-23 06:00:00'),
          (14,'VIO-014',5,1,'US',1,NULL,'Documents','Driver 1 drug test record — annual renewal due in 45 days','Low','Acknowledged','2026-05-24 00:00:00'),
          (15,'VIO-015',3,1,'US',5,NULL,'HOS','Driver 5 cycle time caution — review 70hr/8day cycle usage','Medium','Open','2026-05-23 09:00:00')
          ON CONFLICT DO NOTHING",

        @"INSERT INTO compliance_audit_packages (id,package_code,country_code,profile_id,created_by,status,included_drivers,included_vehicles,included_documents,included_violations,hos_logs_count,date_range_start,date_range_end,notes) VALUES
          (1,'AUD-2026-US-001','US',1,'admin','Ready',5,5,18,8,20,'2026-05-01','2026-05-24','FMCSA property carrier compliance audit package — May 2026'),
          (2,'AUD-2026-CA-001','CA',3,'admin','Draft',2,2,6,2,6,'2026-05-01','2026-05-24','Transport Canada NSC audit package — May 2026'),
          (3,'AUD-2026-AE-001','AE',5,'admin','Draft',1,1,3,0,3,'2026-05-01','2026-05-24','UAE RTA compliance audit package'),
          (4,'AUD-2026-SA-001','SA',4,'admin','Draft',1,1,4,0,3,'2026-05-01','2026-05-24','SASO Saudi Arabia compliance review'),
          (5,'AUD-2026-ALL-001','US',NULL,'admin','In Progress',10,10,30,15,30,'2026-05-01','2026-05-24','Fleet-wide cross-border compliance audit package')
          ON CONFLICT DO NOTHING",

        @"INSERT INTO ai_recommendations (company_id,module_key,title,body,description,priority,score,status,action_label,action_type) VALUES
          (1,'compliance','3 Critical HOS Violations Require Immediate Review','Drivers 2, 4, and 7 have active HOS violations. Driver 4 has a repeat pattern. Schedule compliance review within 24 hours.','Drivers 2, 4, and 7 have active HOS violations. Driver 4 has a repeat pattern. Schedule compliance review within 24 hours.','Critical',98,'Recommended','Review Violations','compliance_review'),
          (1,'compliance','ELD Device Malfunction - TRK-104','ELD-004 on vehicle TRK-104 is reporting a malfunction. Per FMCSA regulations, the driver must use paper logs until device is repaired or replaced.','ELD-004 on vehicle TRK-104 is reporting a malfunction. Per FMCSA regulations, the driver must use paper logs until device is repaired or replaced.','High',92,'Recommended','View ELD Status','eld_action'),
          (1,'compliance','Medical Certificate Expiring - 2 Drivers','Drivers 2 and 7 have medical certificates expiring within 30 days. Schedule medical exams immediately to avoid compliance violations.','Drivers 2 and 7 have medical certificates expiring within 30 days. Schedule medical exams immediately to avoid compliance violations.','High',89,'Recommended','Schedule Exams','document_action'),
          (1,'compliance','Audit Package Ready for Review','AUD-2026-US-001 is ready and contains 5 drivers, 5 vehicles, 20 HOS logs. Recommend final review before submission.','AUD-2026-US-001 is ready and contains 5 drivers, 5 vehicles, 20 HOS logs. Recommend final review before submission.','Medium',75,'Recommended','View Package','audit_action'),
          (1,'hos-eld','Driver 4 - Repeat 11-Hour Violation Pattern','Driver 4 has exceeded the 11-hour driving limit twice in 3 days. This pattern indicates dispatch scheduling issues. Review route assignments.','Driver 4 has exceeded the 11-hour driving limit twice in 3 days. This pattern indicates dispatch scheduling issues. Review route assignments.','Critical',97,'Recommended','Review Schedule','hos_action'),
          (1,'hos-eld','Driver 2 - 70-Hour Cycle Limit Warning','Driver 2 has only 240 minutes remaining in the 70-hour/8-day cycle. Plan mandatory reset before next dispatch.','Driver 2 has only 240 minutes remaining in the 70-hour/8-day cycle. Plan mandatory reset before next dispatch.','High',88,'Recommended','Plan Reset','hos_action'),
          (1,'hos-eld','ELD-008 Firmware Update Available','Device ELD-008 is running firmware 3.4.0. Update to 3.4.1 available - addresses connectivity issues.','Device ELD-008 is running firmware 3.4.0. Update to 3.4.1 available - addresses connectivity issues.','Low',55,'Recommended','Schedule Update','eld_action')
          ON CONFLICT DO NOTHING",
    ];
}
