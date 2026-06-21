-- ═══════════════════════════════════════════════════════════════════════════
-- PRODUCTION TiDB VERIFICATION SCRIPT — READ-ONLY
-- Run this in Render shell:
--   mysql -h <TIDB_HOST> -P <PORT> -u <USER> -p<PASSWORD> <DATABASE> < verify-prod-tidb.sql
-- OR pipe directly:
--   mysql -h ... < verify-prod-tidb.sql
-- ALL queries are SELECT/SHOW only. No writes, no DDL.
-- ═══════════════════════════════════════════════════════════════════════════

-- ── 1. Connection & DB ──────────────────────────────────────────────────────
SELECT '=== 1. CONNECTION' AS section;
SELECT VERSION() AS tidb_version;
SELECT DATABASE() AS current_db;

-- ── 2. Migration state ──────────────────────────────────────────────────────
SELECT '=== 2. MIGRATIONS' AS section;
SELECT COUNT(*) AS total_applied_migrations FROM __EFMigrationsHistory;
SELECT MigrationId AS last_migration
FROM   __EFMigrationsHistory
ORDER  BY MigrationId DESC
LIMIT  1;

-- Check all expected migrations are present
SELECT MigrationId
FROM   __EFMigrationsHistory
WHERE  MigrationId IN (
    '20260523095025_EmployeeManagementLivePersistence',
    '20260614000000_AddPlatformUsers',
    '20260614010000_AddMarketingAndLeads',
    '20260617231354_AddFinanceGlEntries',
    '20260618014207_AddWpsFileBatchMetadata',
    '20260618225223_AddPayrollCompanyScope',
    '20260621000001_AddMfaSupport',
    '20260621170335_AddPlatformComplianceAndConfig',
    '20260623000000_AddCountryPackFramework',
    '20260624000000_AddEmployeeBonusIntId'
)
ORDER  BY MigrationId;

-- ── 3. Core HR row counts ───────────────────────────────────────────────────
SELECT '=== 3. CORE HR' AS section;
SELECT table_name, table_rows AS approx_rows
FROM   information_schema.TABLES
WHERE  table_schema = DATABASE()
  AND  table_name IN (
         'employees', 'companies', 'departments', 'designations',
         'branches', 'users', 'tenants', 'employee_payroll_profiles'
       )
ORDER  BY table_name;

-- Tenant count
SELECT COUNT(*)  AS tenant_count  FROM tenants WHERE is_deleted = 0 OR is_deleted IS NULL;
SELECT COUNT(*)  AS employee_count FROM employees WHERE is_deleted = 0 OR is_deleted IS NULL;
SELECT COUNT(DISTINCT tenant_id) AS tenants_with_employees FROM employees;

-- ── 4. Payroll ──────────────────────────────────────────────────────────────
SELECT '=== 4. PAYROLL' AS section;
SELECT table_name, table_rows AS approx_rows
FROM   information_schema.TABLES
WHERE  table_schema = DATABASE()
  AND  table_name IN (
         'payroll_runs', 'payroll_slips', 'payroll_cycles',
         'payroll_payment_batches', 'payslips', 'salary_components'
       )
ORDER  BY table_name;

-- Payroll run status summary
SELECT status, COUNT(*) AS count
FROM   payroll_runs
GROUP  BY status
ORDER  BY status;

-- Payroll orphan check
SELECT CASE WHEN COUNT(*) = 0 THEN 'PASS' ELSE CONCAT('WARN: ', COUNT(*), ' orphaned slips') END
       AS payslip_integrity
FROM   payroll_slips ps
WHERE  NOT EXISTS (SELECT 1 FROM payroll_runs pr WHERE pr.id = ps.run_id);

-- ── 5. Leave & Attendance ───────────────────────────────────────────────────
SELECT '=== 5. LEAVE & ATTENDANCE' AS section;
SELECT table_name, table_rows AS approx_rows
FROM   information_schema.TABLES
WHERE  table_schema = DATABASE()
  AND  table_name IN (
         'leave_requests', 'leave_types', 'leave_balances',
         'attendance_records', 'attendance_daily_records', 'overtime_requests'
       )
ORDER  BY table_name;

-- ── 6. Performance & Talent ─────────────────────────────────────────────────
SELECT '=== 6. PERFORMANCE & TALENT' AS section;
SELECT table_name, table_rows AS approx_rows
FROM   information_schema.TABLES
WHERE  table_schema = DATABASE()
  AND  table_name IN (
         'performance_cycles', 'appraisal_reviews', 'appraisal_calibrations',
         'employee_goals', 'performance_improvement_plans',
         'candidates', 'job_openings', 'onboarding_tasks'
       )
ORDER  BY table_name;

-- ── 7. Country Pack / Compliance ────────────────────────────────────────────
SELECT '=== 7. COUNTRY PACK' AS section;
SELECT table_name, table_rows AS approx_rows
FROM   information_schema.TABLES
WHERE  table_schema = DATABASE()
  AND  table_name IN (
         'country_payroll_rules', 'gcc_compliance_settings',
         'statutory_rules', 'wps_file_batches', 'sif_file_records',
         'QiwaTenantConnections', 'QiwaSyncLogs', 'qiwa_api_credentials'
       )
ORDER  BY table_name;

SELECT COUNT(*) AS country_rule_count FROM country_payroll_rules;

-- ── 8. Finance ──────────────────────────────────────────────────────────────
SELECT '=== 8. FINANCE' AS section;
SELECT table_name, table_rows AS approx_rows
FROM   information_schema.TABLES
WHERE  table_schema = DATABASE()
  AND  table_name IN (
         'finance_gl_entries', 'tenant_invoices',
         'employee_loans', 'salary_advances', 'employee_bonuses'
       )
ORDER  BY table_name;

-- ── 9. Platform (SaaS) ──────────────────────────────────────────────────────
SELECT '=== 9. PLATFORM SAAS' AS section;
SELECT table_name, table_rows AS approx_rows
FROM   information_schema.TABLES
WHERE  table_schema = DATABASE()
  AND  table_name IN (
         'platform_users', 'platform_announcements', 'platform_leads',
         'platform_support_sessions', 'platform_compliance_controls',
         'platform_config_entries', 'platform_security_incidents'
       )
ORDER  BY table_name;

-- Platform user count
SELECT role, COUNT(*) AS count FROM platform_users GROUP BY role ORDER BY role;

-- ── 10. Tenant isolation spot-check ─────────────────────────────────────────
SELECT '=== 10. TENANT ISOLATION' AS section;
SELECT COUNT(DISTINCT tenant_id) AS emp_tenants   FROM employees;
SELECT COUNT(DISTINCT tenant_id) AS leave_tenants  FROM leave_requests;
SELECT COUNT(DISTINCT tenant_id) AS payroll_tenants FROM payroll_runs;
SELECT COUNT(DISTINCT tenant_id) AS att_tenants    FROM attendance_records;

-- Cross-tenant orphan check (employees without a matching tenant record)
SELECT CASE WHEN COUNT(*) = 0 THEN 'PASS' ELSE CONCAT('WARN: ', COUNT(*), ' employees with no tenant row') END
       AS emp_tenant_integrity
FROM   employees e
WHERE  NOT EXISTS (SELECT 1 FROM tenants t WHERE t.id = e.tenant_id);

-- ── 11. Audit log ───────────────────────────────────────────────────────────
SELECT '=== 11. AUDIT LOG' AS section;
SELECT COUNT(*) AS total_audit_entries FROM admin_audit_logs;
SELECT action, COUNT(*) AS count
FROM   admin_audit_logs
GROUP  BY action
ORDER  BY count DESC
LIMIT  10;

-- ── 12. TiDB collation / charset warnings ───────────────────────────────────
SELECT '=== 12. COLLATION CHECK' AS section;

-- Tables NOT in an accepted collation
SELECT table_name, table_collation
FROM   information_schema.TABLES
WHERE  table_schema = DATABASE()
  AND  table_collation NOT IN (
         'utf8mb4_general_ci', 'utf8mb4_unicode_ci',
         'utf8mb4_0900_ai_ci', 'utf8mb4_bin'
       )
ORDER  BY table_name
LIMIT  20;

-- GUID columns: should be ascii_general_ci or latin1_bin
SELECT table_name, column_name, character_set_name, collation_name
FROM   information_schema.COLUMNS
WHERE  table_schema = DATABASE()
  AND  column_type LIKE 'char(36)%'
  AND  collation_name NOT IN ('ascii_general_ci', 'latin1_bin', 'utf8mb4_general_ci')
ORDER  BY table_name, column_name
LIMIT  20;

-- ── 13. Missing tables guard ────────────────────────────────────────────────
SELECT '=== 13. TABLE EXISTENCE GUARD' AS section;
SELECT 'finance_gl_entries' AS expected_table,
       CASE WHEN COUNT(*) > 0 THEN 'PRESENT' ELSE 'MISSING' END AS status
FROM   information_schema.TABLES
WHERE  table_schema = DATABASE() AND table_name = 'finance_gl_entries'
UNION ALL
SELECT 'platform_compliance_controls',
       CASE WHEN COUNT(*) > 0 THEN 'PRESENT' ELSE 'MISSING' END
FROM   information_schema.TABLES
WHERE  table_schema = DATABASE() AND table_name = 'platform_compliance_controls'
UNION ALL
SELECT 'platform_config_entries',
       CASE WHEN COUNT(*) > 0 THEN 'PRESENT' ELSE 'MISSING' END
FROM   information_schema.TABLES
WHERE  table_schema = DATABASE() AND table_name = 'platform_config_entries'
UNION ALL
SELECT 'platform_security_incidents',
       CASE WHEN COUNT(*) > 0 THEN 'PRESENT' ELSE 'MISSING' END
FROM   information_schema.TABLES
WHERE  table_schema = DATABASE() AND table_name = 'platform_security_incidents'
UNION ALL
SELECT 'wps_file_batches',
       CASE WHEN COUNT(*) > 0 THEN 'PRESENT' ELSE 'MISSING' END
FROM   information_schema.TABLES
WHERE  table_schema = DATABASE() AND table_name = 'wps_file_batches';

SELECT '=== VERIFICATION COMPLETE' AS section;
