CREATE DATABASE IF NOT EXISTS zayra;
USE zayra;
CREATE TABLE employees (
 id INT AUTO_INCREMENT PRIMARY KEY,
 employee_code VARCHAR(50) NOT NULL UNIQUE,
 full_name VARCHAR(150) NOT NULL,
 department VARCHAR(100),
 designation VARCHAR(100),
 work_location VARCHAR(100),
 status VARCHAR(30) DEFAULT 'Active',
 joining_date DATE,
 created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);
CREATE TABLE attendance_records (
 id INT AUTO_INCREMENT PRIMARY KEY,
 employee_id INT NOT NULL,
 work_date DATE NOT NULL,
 time_in TIME NULL,
 time_out TIME NULL,
 overtime_hours DECIMAL(5,2) DEFAULT 0,
 status VARCHAR(30) DEFAULT 'Present',
 FOREIGN KEY (employee_id) REFERENCES employees(id)
);
CREATE TABLE requisitions (
 id INT AUTO_INCREMENT PRIMARY KEY,
 department VARCHAR(100),
 role_title VARCHAR(150),
 requisition_type ENUM('New Headcount','Replacement') DEFAULT 'New Headcount',
 budget_status VARCHAR(50),
 approval_status VARCHAR(50) DEFAULT 'Pending',
 requested_by VARCHAR(150),
 created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);
CREATE TABLE appraisal_cycles (
 id INT AUTO_INCREMENT PRIMARY KEY,
 cycle_name VARCHAR(150),
 period_start DATE,
 period_end DATE,
 status VARCHAR(50) DEFAULT 'Draft'
);

CREATE TABLE tenants (
 id CHAR(36) PRIMARY KEY,
 name VARCHAR(160) NOT NULL,
 slug VARCHAR(80) NOT NULL UNIQUE,
 is_active BOOLEAN NOT NULL DEFAULT TRUE,
 created_at_utc DATETIME(6) NOT NULL
);

CREATE TABLE users (
 id CHAR(36) PRIMARY KEY,
 tenant_id CHAR(36) NOT NULL,
 email VARCHAR(256) NOT NULL,
 normalized_email VARCHAR(256) NOT NULL,
 full_name VARCHAR(180) NOT NULL,
 password_hash VARCHAR(512) NOT NULL,
 is_active BOOLEAN NOT NULL DEFAULT TRUE,
 is_email_confirmed BOOLEAN NOT NULL DEFAULT TRUE,
 last_login_at_utc DATETIME(6) NULL,
 created_at_utc DATETIME(6) NOT NULL,
 updated_at_utc DATETIME(6) NULL,
 UNIQUE KEY ux_users_tenant_email (tenant_id, normalized_email),
 CONSTRAINT fk_users_tenants FOREIGN KEY (tenant_id) REFERENCES tenants(id)
);

CREATE TABLE roles (
 id CHAR(36) PRIMARY KEY,
 tenant_id CHAR(36) NULL,
 name VARCHAR(80) NOT NULL,
 normalized_name VARCHAR(80) NOT NULL,
 description VARCHAR(240) NOT NULL,
 is_system BOOLEAN NOT NULL DEFAULT FALSE,
 created_at_utc DATETIME(6) NOT NULL,
 UNIQUE KEY ux_roles_tenant_name (tenant_id, normalized_name),
 CONSTRAINT fk_roles_tenants FOREIGN KEY (tenant_id) REFERENCES tenants(id)
);

CREATE TABLE permissions (
 id CHAR(36) PRIMARY KEY,
 permission_key VARCHAR(120) NOT NULL UNIQUE,
 module VARCHAR(80) NOT NULL,
 description VARCHAR(240) NOT NULL,
 created_at_utc DATETIME(6) NOT NULL
);

CREATE TABLE user_roles (
 user_id CHAR(36) NOT NULL,
 role_id CHAR(36) NOT NULL,
 PRIMARY KEY (user_id, role_id),
 CONSTRAINT fk_user_roles_users FOREIGN KEY (user_id) REFERENCES users(id) ON DELETE CASCADE,
 CONSTRAINT fk_user_roles_roles FOREIGN KEY (role_id) REFERENCES roles(id) ON DELETE CASCADE
);

CREATE TABLE role_permissions (
 role_id CHAR(36) NOT NULL,
 permission_id CHAR(36) NOT NULL,
 PRIMARY KEY (role_id, permission_id),
 CONSTRAINT fk_role_permissions_roles FOREIGN KEY (role_id) REFERENCES roles(id) ON DELETE CASCADE,
 CONSTRAINT fk_role_permissions_permissions FOREIGN KEY (permission_id) REFERENCES permissions(id) ON DELETE CASCADE
);

CREATE TABLE refresh_tokens (
 id CHAR(36) PRIMARY KEY,
 user_id CHAR(36) NOT NULL,
 token_hash VARCHAR(128) NOT NULL UNIQUE,
 expires_at_utc DATETIME(6) NOT NULL,
 created_at_utc DATETIME(6) NOT NULL,
 revoked_at_utc DATETIME(6) NULL,
 replaced_by_token_hash VARCHAR(128) NULL,
 created_by_ip VARCHAR(64) NULL,
 revoked_by_ip VARCHAR(64) NULL,
 CONSTRAINT fk_refresh_tokens_users FOREIGN KEY (user_id) REFERENCES users(id) ON DELETE CASCADE
);

CREATE TABLE password_reset_tokens (
 id CHAR(36) PRIMARY KEY,
 user_id CHAR(36) NOT NULL,
 token_hash VARCHAR(128) NOT NULL UNIQUE,
 expires_at_utc DATETIME(6) NOT NULL,
 created_at_utc DATETIME(6) NOT NULL,
 used_at_utc DATETIME(6) NULL,
 created_by_ip VARCHAR(64) NULL,
 CONSTRAINT fk_password_reset_tokens_users FOREIGN KEY (user_id) REFERENCES users(id) ON DELETE CASCADE
);

CREATE TABLE audit_logs (
 id CHAR(36) PRIMARY KEY,
 tenant_id CHAR(36) NULL,
 user_id CHAR(36) NULL,
 action VARCHAR(120) NOT NULL,
 entity_name VARCHAR(120) NOT NULL,
 entity_id VARCHAR(80) NULL,
 ip_address VARCHAR(64) NULL,
 user_agent VARCHAR(512) NULL,
 metadata JSON NULL,
 created_at_utc DATETIME(6) NOT NULL,
 INDEX ix_audit_logs_tenant_created (tenant_id, created_at_utc),
 INDEX ix_audit_logs_user_created (user_id, created_at_utc)
);

CREATE TABLE IF NOT EXISTS notifications (
 id CHAR(36) PRIMARY KEY,
 tenant_id CHAR(36) NOT NULL,
 user_id CHAR(36) NULL,
 channel VARCHAR(40) NOT NULL,
 title VARCHAR(180) NOT NULL,
 message VARCHAR(1000) NOT NULL,
 entity_name VARCHAR(120) NOT NULL,
 entity_id VARCHAR(80) NULL,
 status VARCHAR(40) NOT NULL,
 created_at_utc DATETIME(6) NOT NULL,
 read_at_utc DATETIME(6) NULL,
 INDEX ix_notifications_tenant_user_status_created (tenant_id, user_id, status, created_at_utc)
);

ALTER TABLE employees ADD COLUMN IF NOT EXISTS tenant_id CHAR(36) NULL;
ALTER TABLE employees ADD COLUMN IF NOT EXISTS english_name VARCHAR(180) NOT NULL DEFAULT '';
ALTER TABLE employees ADD COLUMN IF NOT EXISTS arabic_name VARCHAR(180) NOT NULL DEFAULT '';
ALTER TABLE employees ADD COLUMN IF NOT EXISTS date_of_birth DATE NULL;
ALTER TABLE employees ADD COLUMN IF NOT EXISTS marital_status VARCHAR(60) NOT NULL DEFAULT '';
ALTER TABLE employees ADD COLUMN IF NOT EXISTS emergency_contact_name VARCHAR(180) NOT NULL DEFAULT '';
ALTER TABLE employees ADD COLUMN IF NOT EXISTS emergency_contact_phone VARCHAR(60) NOT NULL DEFAULT '';
ALTER TABLE employees ADD COLUMN IF NOT EXISTS contract_type VARCHAR(80) NOT NULL DEFAULT '';
ALTER TABLE employees ADD COLUMN IF NOT EXISTS grade VARCHAR(80) NOT NULL DEFAULT '';
ALTER TABLE employees ADD COLUMN IF NOT EXISTS cost_center VARCHAR(80) NOT NULL DEFAULT '';
ALTER TABLE employees ADD COLUMN IF NOT EXISTS passport_issue_date DATE NULL;
ALTER TABLE employees ADD COLUMN IF NOT EXISTS visa_issue_date DATE NULL;
ALTER TABLE employees ADD COLUMN IF NOT EXISTS residency_issue_date DATE NULL;
ALTER TABLE employees ADD COLUMN IF NOT EXISTS work_permit_issue_date DATE NULL;

CREATE TABLE IF NOT EXISTS employee_drafts (
 id CHAR(36) PRIMARY KEY,
 tenant_id CHAR(36) NOT NULL,
 created_by_user_id CHAR(36) NULL,
 status VARCHAR(40) NOT NULL,
 current_step VARCHAR(80) NOT NULL,
 english_name VARCHAR(180) NOT NULL DEFAULT '',
 arabic_name VARCHAR(180) NOT NULL DEFAULT '',
 personal_email VARCHAR(256) NOT NULL DEFAULT '',
 work_email VARCHAR(256) NOT NULL DEFAULT '',
 phone VARCHAR(60) NOT NULL DEFAULT '',
 gender VARCHAR(40) NOT NULL DEFAULT '',
 date_of_birth DATE NULL,
 marital_status VARCHAR(60) NOT NULL DEFAULT '',
 emergency_contact_name VARCHAR(180) NOT NULL DEFAULT '',
 emergency_contact_phone VARCHAR(60) NOT NULL DEFAULT '',
 nationality VARCHAR(80) NOT NULL DEFAULT '',
 country_code VARCHAR(10) NOT NULL DEFAULT '',
 department VARCHAR(120) NOT NULL DEFAULT '',
 designation VARCHAR(120) NOT NULL DEFAULT '',
 branch VARCHAR(120) NOT NULL DEFAULT '',
 work_location VARCHAR(120) NOT NULL DEFAULT '',
 manager_employee_id INT NULL,
 joining_date DATETIME(6) NULL,
 contract_type VARCHAR(80) NOT NULL DEFAULT '',
 grade VARCHAR(80) NOT NULL DEFAULT '',
 cost_center VARCHAR(80) NOT NULL DEFAULT '',
 contract_start_date DATE NULL,
 contract_end_date DATE NULL,
 probation_end_date DATE NULL,
 payroll_profile_code VARCHAR(80) NOT NULL DEFAULT '',
 salary DECIMAL(12,2) NULL,
 bank_name VARCHAR(160) NOT NULL DEFAULT '',
 bank_iban VARCHAR(80) NOT NULL DEFAULT '',
 wps_bank_details VARCHAR(240) NOT NULL DEFAULT '',
 shift_policy_code VARCHAR(80) NOT NULL DEFAULT '',
 leave_policy_code VARCHAR(80) NOT NULL DEFAULT '',
 sponsor_name VARCHAR(180) NOT NULL DEFAULT '',
 passport_issue_date DATE NULL,
 passport_number VARCHAR(80) NOT NULL DEFAULT '',
 passport_expiry_date DATE NULL,
 visa_issue_date DATE NULL,
 visa_number VARCHAR(80) NOT NULL DEFAULT '',
 visa_expiry_date DATE NULL,
 residency_issue_date DATE NULL,
 work_permit_issue_date DATE NULL,
 iqama_number VARCHAR(80) NOT NULL DEFAULT '',
 muqeem_number VARCHAR(80) NOT NULL DEFAULT '',
 gosi_reference VARCHAR(80) NOT NULL DEFAULT '',
 qiwa_contract_number VARCHAR(80) NOT NULL DEFAULT '',
 emirates_id VARCHAR(80) NOT NULL DEFAULT '',
 labor_card_number VARCHAR(80) NOT NULL DEFAULT '',
 visa_file_number VARCHAR(80) NOT NULL DEFAULT '',
 qid VARCHAR(80) NOT NULL DEFAULT '',
 work_permit_number VARCHAR(80) NOT NULL DEFAULT '',
 civil_id VARCHAR(80) NOT NULL DEFAULT '',
 residency_number VARCHAR(80) NOT NULL DEFAULT '',
 profile_completeness_score DECIMAL(5,2) NOT NULL DEFAULT 0,
 created_at_utc DATETIME(6) NOT NULL,
 submitted_at_utc DATETIME(6) NULL,
 approved_at_utc DATETIME(6) NULL,
 activated_at_utc DATETIME(6) NULL,
 INDEX ix_employee_drafts_tenant_status (tenant_id, status)
);

CREATE TABLE IF NOT EXISTS companies (
 id CHAR(36) PRIMARY KEY,
 tenant_id CHAR(36) NOT NULL,
 legal_name_en VARCHAR(180) NOT NULL,
 legal_name_ar VARCHAR(180) NOT NULL,
 trade_name VARCHAR(180) NOT NULL,
 country_code VARCHAR(10) NOT NULL,
 registration_number VARCHAR(100) NOT NULL,
 tax_number VARCHAR(100) NOT NULL,
 wps_employer_id VARCHAR(100) NOT NULL,
 gosi_employer_id VARCHAR(100) NOT NULL,
 qiwa_establishment_id VARCHAR(100) NOT NULL,
 default_currency VARCHAR(10) NOT NULL,
 is_active BOOLEAN NOT NULL,
 created_at_utc DATETIME(6) NOT NULL,
 updated_at_utc DATETIME(6) NULL,
 INDEX ix_companies_tenant_legal_name (tenant_id, legal_name_en)
);

CREATE TABLE IF NOT EXISTS branches (
 id CHAR(36) PRIMARY KEY,
 tenant_id CHAR(36) NOT NULL,
 company_id CHAR(36) NOT NULL,
 code VARCHAR(80) NOT NULL,
 name_en VARCHAR(180) NOT NULL,
 name_ar VARCHAR(180) NOT NULL,
 country_code VARCHAR(10) NOT NULL,
 city VARCHAR(120) NOT NULL,
 address_line1 VARCHAR(240) NOT NULL,
 address_line2 VARCHAR(240) NOT NULL,
 time_zone_id VARCHAR(80) NOT NULL,
 labor_office_code VARCHAR(100) NOT NULL,
 is_head_office BOOLEAN NOT NULL,
 is_active BOOLEAN NOT NULL,
 created_at_utc DATETIME(6) NOT NULL,
 updated_at_utc DATETIME(6) NULL,
 UNIQUE KEY ux_branches_tenant_code (tenant_id, code),
 INDEX ix_branches_tenant_company (tenant_id, company_id)
);

CREATE TABLE IF NOT EXISTS departments (
 id CHAR(36) PRIMARY KEY,
 tenant_id CHAR(36) NOT NULL,
 branch_id CHAR(36) NULL,
 parent_department_id CHAR(36) NULL,
 code VARCHAR(80) NOT NULL,
 name_en VARCHAR(180) NOT NULL,
 name_ar VARCHAR(180) NOT NULL,
 manager_employee_id INT NULL,
 is_active BOOLEAN NOT NULL,
 created_at_utc DATETIME(6) NOT NULL,
 updated_at_utc DATETIME(6) NULL,
 UNIQUE KEY ux_departments_tenant_code (tenant_id, code),
 INDEX ix_departments_tenant_branch (tenant_id, branch_id)
);

CREATE TABLE IF NOT EXISTS designations (
 id CHAR(36) PRIMARY KEY,
 tenant_id CHAR(36) NOT NULL,
 department_id CHAR(36) NULL,
 code VARCHAR(80) NOT NULL,
 title_en VARCHAR(180) NOT NULL,
 title_ar VARCHAR(180) NOT NULL,
 job_grade VARCHAR(80) NOT NULL,
 is_manager_role BOOLEAN NOT NULL,
 is_active BOOLEAN NOT NULL,
 created_at_utc DATETIME(6) NOT NULL,
 updated_at_utc DATETIME(6) NULL,
 UNIQUE KEY ux_designations_tenant_code (tenant_id, code),
 INDEX ix_designations_tenant_department (tenant_id, department_id)
);

CREATE TABLE IF NOT EXISTS approval_workflows (
 id CHAR(36) PRIMARY KEY,
 tenant_id CHAR(36) NOT NULL,
 code VARCHAR(80) NOT NULL,
 name VARCHAR(180) NOT NULL,
 entity_name VARCHAR(120) NOT NULL,
 is_active BOOLEAN NOT NULL,
 created_at_utc DATETIME(6) NOT NULL,
 UNIQUE KEY ux_approval_workflows_tenant_code (tenant_id, code)
);

CREATE TABLE IF NOT EXISTS approval_workflow_steps (
 id CHAR(36) PRIMARY KEY,
 tenant_id CHAR(36) NOT NULL,
 workflow_id CHAR(36) NOT NULL,
 step_order INT NOT NULL,
 step_name VARCHAR(180) NOT NULL,
 approver_role VARCHAR(80) NOT NULL,
 is_final_step BOOLEAN NOT NULL,
 UNIQUE KEY ux_approval_steps_workflow_order (tenant_id, workflow_id, step_order)
);

CREATE TABLE IF NOT EXISTS approval_requests (
 id CHAR(36) PRIMARY KEY,
 tenant_id CHAR(36) NOT NULL,
 workflow_id CHAR(36) NOT NULL,
 entity_name VARCHAR(120) NOT NULL,
 entity_id VARCHAR(80) NOT NULL,
 title VARCHAR(240) NOT NULL,
 status VARCHAR(40) NOT NULL,
 current_step_order INT NOT NULL,
 requested_by_user_id CHAR(36) NULL,
 created_at_utc DATETIME(6) NOT NULL,
 completed_at_utc DATETIME(6) NULL,
 INDEX ix_approval_requests_entity_status (tenant_id, entity_name, entity_id, status)
);

CREATE TABLE IF NOT EXISTS approval_decisions (
 id CHAR(36) PRIMARY KEY,
 tenant_id CHAR(36) NOT NULL,
 approval_request_id CHAR(36) NOT NULL,
 step_order INT NOT NULL,
 decision VARCHAR(40) NOT NULL,
 comments VARCHAR(1000) NOT NULL,
 decided_by_user_id CHAR(36) NULL,
 decided_at_utc DATETIME(6) NOT NULL,
 INDEX ix_approval_decisions_request_step (tenant_id, approval_request_id, step_order)
);

-- ── Recruitment ────────────────────────────────────────────────────────────────

CREATE TABLE IF NOT EXISTS manpower_requisitions (
 id CHAR(36) PRIMARY KEY,
 tenant_id CHAR(36) NOT NULL,
 requisition_number VARCHAR(40) NOT NULL,
 department_id CHAR(36) NULL,
 department_name VARCHAR(180) NOT NULL,
 designation_id CHAR(36) NULL,
 designation_title VARCHAR(180) NOT NULL,
 head_count INT NOT NULL DEFAULT 1,
 employment_type VARCHAR(40) NOT NULL DEFAULT 'Full-Time',
 priority VARCHAR(20) NOT NULL DEFAULT 'Medium',
 justification TEXT NOT NULL,
 required_skills TEXT NOT NULL,
 min_experience_years INT NULL,
 max_experience_years INT NULL,
 budget_from DECIMAL(18,2) NULL,
 budget_to DECIMAL(18,2) NULL,
 target_joining_date DATE NULL,
 status VARCHAR(40) NOT NULL DEFAULT 'Draft',
 requested_by_user_id CHAR(36) NULL,
 requested_by_name VARCHAR(180) NOT NULL DEFAULT '',
 requested_by_employee_id INT NULL,
 rejection_reason VARCHAR(500) NOT NULL DEFAULT '',
 approval_request_id CHAR(36) NULL,
 created_at_utc DATETIME(6) NOT NULL,
 submitted_at_utc DATETIME(6) NULL,
 approved_at_utc DATETIME(6) NULL,
 rejected_at_utc DATETIME(6) NULL,
 UNIQUE KEY ux_manpower_req_tenant_number (tenant_id, requisition_number),
 INDEX ix_manpower_req_tenant_status (tenant_id, status)
);

CREATE TABLE IF NOT EXISTS job_openings (
 id CHAR(36) PRIMARY KEY,
 tenant_id CHAR(36) NOT NULL,
 job_code VARCHAR(40) NOT NULL,
 requisition_id CHAR(36) NULL,
 title VARCHAR(180) NOT NULL,
 department_id CHAR(36) NULL,
 department_name VARCHAR(180) NOT NULL,
 designation_id CHAR(36) NULL,
 designation_title VARCHAR(180) NOT NULL,
 employment_type VARCHAR(40) NOT NULL DEFAULT 'Full-Time',
 head_count INT NOT NULL DEFAULT 1,
 filled_count INT NOT NULL DEFAULT 0,
 description TEXT NOT NULL,
 requirements TEXT NOT NULL,
 responsibilities TEXT NOT NULL,
 salary_from DECIMAL(18,2) NULL,
 salary_to DECIMAL(18,2) NULL,
 location VARCHAR(180) NOT NULL DEFAULT '',
 status VARCHAR(40) NOT NULL DEFAULT 'Open',
 assigned_hr_user_id CHAR(36) NULL,
 assigned_hr_name VARCHAR(180) NOT NULL DEFAULT '',
 created_at_utc DATETIME(6) NOT NULL,
 published_at_utc DATETIME(6) NULL,
 closed_at_utc DATETIME(6) NULL,
 UNIQUE KEY ux_job_openings_tenant_code (tenant_id, job_code),
 INDEX ix_job_openings_tenant_status (tenant_id, status)
);

CREATE TABLE IF NOT EXISTS candidates (
 id CHAR(36) PRIMARY KEY,
 tenant_id CHAR(36) NOT NULL,
 first_name VARCHAR(120) NOT NULL,
 last_name VARCHAR(120) NOT NULL,
 email VARCHAR(255) NOT NULL,
 phone VARCHAR(40) NOT NULL DEFAULT '',
 current_job_title VARCHAR(180) NOT NULL DEFAULT '',
 current_company VARCHAR(180) NOT NULL DEFAULT '',
 total_experience_years DECIMAL(4,1) NOT NULL DEFAULT 0,
 education_level VARCHAR(80) NOT NULL DEFAULT '',
 nationality VARCHAR(80) NOT NULL DEFAULT '',
 linked_in_url VARCHAR(500) NOT NULL DEFAULT '',
 resume_url VARCHAR(500) NOT NULL DEFAULT '',
 source VARCHAR(80) NOT NULL DEFAULT 'Direct',
 status VARCHAR(40) NOT NULL DEFAULT 'Active',
 tags VARCHAR(500) NOT NULL DEFAULT '',
 created_at_utc DATETIME(6) NOT NULL,
 updated_at_utc DATETIME(6) NULL,
 UNIQUE KEY ux_candidates_tenant_email (tenant_id, email),
 INDEX ix_candidates_tenant_status (tenant_id, status)
);

CREATE TABLE IF NOT EXISTS job_applications (
 id CHAR(36) PRIMARY KEY,
 tenant_id CHAR(36) NOT NULL,
 job_opening_id CHAR(36) NOT NULL,
 job_title VARCHAR(180) NOT NULL DEFAULT '',
 candidate_id CHAR(36) NOT NULL,
 candidate_name VARCHAR(240) NOT NULL DEFAULT '',
 candidate_email VARCHAR(255) NOT NULL DEFAULT '',
 stage VARCHAR(40) NOT NULL DEFAULT 'Applied',
 stage_order INT NOT NULL DEFAULT 1,
 status VARCHAR(40) NOT NULL DEFAULT 'Active',
 rejection_reason VARCHAR(500) NOT NULL DEFAULT '',
 offered_salary DECIMAL(18,2) NULL,
 applied_at_utc DATETIME(6) NOT NULL,
 stage_changed_at_utc DATETIME(6) NULL,
 hired_at_utc DATETIME(6) NULL,
 onboarding_draft_id CHAR(36) NULL,
 UNIQUE KEY ux_job_applications_opening_candidate (tenant_id, job_opening_id, candidate_id),
 INDEX ix_job_applications_tenant_opening (tenant_id, job_opening_id),
 INDEX ix_job_applications_tenant_candidate (tenant_id, candidate_id),
 INDEX ix_job_applications_tenant_status (tenant_id, status)
);

CREATE TABLE IF NOT EXISTS application_events (
 id CHAR(36) PRIMARY KEY,
 tenant_id CHAR(36) NOT NULL,
 application_id CHAR(36) NOT NULL,
 event_type VARCHAR(80) NOT NULL,
 stage VARCHAR(40) NOT NULL DEFAULT '',
 notes TEXT NOT NULL,
 performed_by_user_id CHAR(36) NULL,
 performed_by_name VARCHAR(180) NOT NULL DEFAULT '',
 created_at_utc DATETIME(6) NOT NULL,
 INDEX ix_application_events_tenant_app (tenant_id, application_id)
);

CREATE TABLE IF NOT EXISTS interview_schedules (
 id CHAR(36) PRIMARY KEY,
 tenant_id CHAR(36) NOT NULL,
 application_id CHAR(36) NOT NULL,
 interview_type VARCHAR(80) NOT NULL DEFAULT 'HR Screening',
 interviewer_names VARCHAR(500) NOT NULL DEFAULT '',
 scheduled_at DATETIME(6) NOT NULL,
 duration_minutes INT NOT NULL DEFAULT 60,
 mode VARCHAR(40) NOT NULL DEFAULT 'Video',
 meeting_link VARCHAR(500) NOT NULL DEFAULT '',
 location VARCHAR(300) NOT NULL DEFAULT '',
 status VARCHAR(40) NOT NULL DEFAULT 'Scheduled',
 overall_rating INT NULL,
 recommendation VARCHAR(40) NOT NULL DEFAULT '',
 feedback_notes TEXT NOT NULL DEFAULT '',
 completed_at DATETIME(6) NULL,
 created_at_utc DATETIME(6) NOT NULL,
 INDEX ix_interview_schedules_tenant_app (tenant_id, application_id)
);

CREATE TABLE IF NOT EXISTS offer_letters (
 id CHAR(36) PRIMARY KEY,
 tenant_id CHAR(36) NOT NULL,
 application_id CHAR(36) NOT NULL,
 candidate_name VARCHAR(240) NOT NULL DEFAULT '',
 offered_job_title VARCHAR(180) NOT NULL DEFAULT '',
 offered_department VARCHAR(180) NOT NULL DEFAULT '',
 start_date DATE NOT NULL,
 basic_salary DECIMAL(18,2) NOT NULL DEFAULT 0,
 housing_allowance DECIMAL(18,2) NOT NULL DEFAULT 0,
 transport_allowance DECIMAL(18,2) NOT NULL DEFAULT 0,
 other_allowances DECIMAL(18,2) NOT NULL DEFAULT 0,
 gross_salary DECIMAL(18,2) NOT NULL DEFAULT 0,
 probation_months INT NOT NULL DEFAULT 3,
 content_html LONGTEXT NOT NULL DEFAULT '',
 status VARCHAR(40) NOT NULL DEFAULT 'Draft',
 generated_at_utc DATETIME(6) NOT NULL,
 sent_at_utc DATETIME(6) NULL,
 response_deadline DATETIME(6) NULL,
 accepted_at_utc DATETIME(6) NULL,
 declined_at_utc DATETIME(6) NULL,
 decline_reason VARCHAR(500) NOT NULL DEFAULT '',
 INDEX ix_offer_letters_tenant_app (tenant_id, application_id),
 INDEX ix_offer_letters_tenant_status (tenant_id, status)
);

-- ── Performance & Appraisals ───────────────────────────────────────────────────

CREATE TABLE IF NOT EXISTS performance_cycles (
 id CHAR(36) PRIMARY KEY,
 tenant_id CHAR(36) NOT NULL,
 name VARCHAR(240) NOT NULL,
 cycle_type VARCHAR(40) NOT NULL DEFAULT 'Annual',
 review_period_start DATE NOT NULL,
 review_period_end DATE NOT NULL,
 status VARCHAR(40) NOT NULL DEFAULT 'Draft',
 enable_calibration BOOLEAN NOT NULL DEFAULT TRUE,
 enable_360_feedback BOOLEAN NOT NULL DEFAULT FALSE,
 enable_self_assessment BOOLEAN NOT NULL DEFAULT TRUE,
 enable_forced_distribution BOOLEAN NOT NULL DEFAULT FALSE,
 self_assessment_deadline DATE NULL,
 manager_review_deadline DATE NULL,
 calibration_deadline DATE NULL,
 default_scorecard_template_id CHAR(36) NULL,
 notes TEXT NOT NULL DEFAULT '',
 created_by_user_id CHAR(36) NULL,
 created_at_utc DATETIME(6) NOT NULL,
 launched_at_utc DATETIME(6) NULL,
 published_at_utc DATETIME(6) NULL,
 closed_at_utc DATETIME(6) NULL,
 INDEX ix_perf_cycles_tenant_status (tenant_id, status)
);

CREATE TABLE IF NOT EXISTS performance_scorecard_templates (
 id CHAR(36) PRIMARY KEY,
 tenant_id CHAR(36) NOT NULL,
 name VARCHAR(240) NOT NULL,
 department_name VARCHAR(180) NOT NULL DEFAULT '',
 designation_title VARCHAR(180) NOT NULL DEFAULT '',
 grade VARCHAR(80) NOT NULL DEFAULT '',
 kpi_weight DECIMAL(5,2) NOT NULL DEFAULT 40,
 competency_weight DECIMAL(5,2) NOT NULL DEFAULT 20,
 attendance_weight DECIMAL(5,2) NOT NULL DEFAULT 10,
 productivity_weight DECIMAL(5,2) NOT NULL DEFAULT 15,
 feedback_weight DECIMAL(5,2) NOT NULL DEFAULT 10,
 discipline_weight DECIMAL(5,2) NOT NULL DEFAULT 5,
 min_passing_score DECIMAL(5,2) NOT NULL DEFAULT 60,
 requires_calibration BOOLEAN NOT NULL DEFAULT TRUE,
 requires_360_feedback BOOLEAN NOT NULL DEFAULT FALSE,
 is_default BOOLEAN NOT NULL DEFAULT FALSE,
 is_active BOOLEAN NOT NULL DEFAULT TRUE,
 rating_labels TEXT NOT NULL DEFAULT '',
 created_at_utc DATETIME(6) NOT NULL,
 updated_at_utc DATETIME(6) NULL,
 INDEX ix_perf_templates_tenant (tenant_id, is_active)
);

CREATE TABLE IF NOT EXISTS performance_rating_scales (
 id CHAR(36) PRIMARY KEY,
 tenant_id CHAR(36) NOT NULL,
 name VARCHAR(180) NOT NULL,
 scale_points INT NOT NULL DEFAULT 5,
 is_default BOOLEAN NOT NULL DEFAULT FALSE,
 created_at_utc DATETIME(6) NOT NULL,
 INDEX ix_perf_rating_scales_tenant (tenant_id)
);

CREATE TABLE IF NOT EXISTS performance_rating_options (
 id CHAR(36) PRIMARY KEY,
 tenant_id CHAR(36) NOT NULL,
 scale_id CHAR(36) NOT NULL,
 label VARCHAR(120) NOT NULL,
 min_score DECIMAL(5,2) NOT NULL,
 max_score DECIMAL(5,2) NOT NULL,
 color VARCHAR(20) NOT NULL DEFAULT '#64748b',
 sort_order INT NOT NULL DEFAULT 0,
 INDEX ix_perf_rating_options_scale (tenant_id, scale_id)
);

CREATE TABLE IF NOT EXISTS performance_cycle_employees (
 id CHAR(36) PRIMARY KEY,
 tenant_id CHAR(36) NOT NULL,
 cycle_id CHAR(36) NOT NULL,
 employee_id INT NOT NULL,
 employee_name VARCHAR(240) NOT NULL DEFAULT '',
 department_name VARCHAR(180) NOT NULL DEFAULT '',
 designation_title VARCHAR(180) NOT NULL DEFAULT '',
 scorecard_template_id CHAR(36) NULL,
 status VARCHAR(40) NOT NULL DEFAULT 'Enrolled',
 enrolled_at_utc DATETIME(6) NOT NULL,
 UNIQUE KEY ux_perf_cycle_emp (tenant_id, cycle_id, employee_id),
 INDEX ix_perf_cycle_emp_cycle (tenant_id, cycle_id, status)
);

CREATE TABLE IF NOT EXISTS competencies (
 id CHAR(36) PRIMARY KEY,
 tenant_id CHAR(36) NOT NULL,
 name VARCHAR(180) NOT NULL,
 category VARCHAR(40) NOT NULL DEFAULT 'Core',
 description TEXT NOT NULL DEFAULT '',
 behavioral_indicators TEXT NOT NULL DEFAULT '',
 is_active BOOLEAN NOT NULL DEFAULT TRUE,
 created_at_utc DATETIME(6) NOT NULL,
 INDEX ix_competencies_tenant_category (tenant_id, category, is_active)
);

CREATE TABLE IF NOT EXISTS role_competencies (
 id CHAR(36) PRIMARY KEY,
 tenant_id CHAR(36) NOT NULL,
 competency_id CHAR(36) NOT NULL,
 department_name VARCHAR(180) NOT NULL DEFAULT '',
 designation_title VARCHAR(180) NOT NULL DEFAULT '',
 expected_level VARCHAR(80) NOT NULL DEFAULT '',
 weight DECIMAL(5,2) NOT NULL DEFAULT 100,
 INDEX ix_role_competencies_tenant (tenant_id, department_name)
);

CREATE TABLE IF NOT EXISTS employee_goals (
 id CHAR(36) PRIMARY KEY,
 tenant_id CHAR(36) NOT NULL,
 cycle_id CHAR(36) NULL,
 employee_id INT NOT NULL,
 employee_name VARCHAR(240) NOT NULL DEFAULT '',
 title VARCHAR(300) NOT NULL,
 description TEXT NOT NULL DEFAULT '',
 category VARCHAR(40) NOT NULL DEFAULT 'Individual',
 kpi_type VARCHAR(40) NOT NULL DEFAULT 'Quantitative',
 measurement_unit VARCHAR(80) NOT NULL DEFAULT '',
 target_value DECIMAL(14,4) NOT NULL DEFAULT 0,
 actual_value DECIMAL(14,4) NOT NULL DEFAULT 0,
 weight DECIMAL(5,2) NOT NULL DEFAULT 100,
 achievement_pct DECIMAL(5,2) NOT NULL DEFAULT 0,
 due_date DATE NULL,
 status VARCHAR(40) NOT NULL DEFAULT 'Active',
 manager_approved BOOLEAN NOT NULL DEFAULT FALSE,
 approved_by_user_id CHAR(36) NULL,
 created_by_user_id CHAR(36) NULL,
 created_at_utc DATETIME(6) NOT NULL,
 updated_at_utc DATETIME(6) NULL,
 INDEX ix_employee_goals_tenant_employee (tenant_id, employee_id, status),
 INDEX ix_employee_goals_tenant_cycle (tenant_id, cycle_id)
);

CREATE TABLE IF NOT EXISTS goal_progress_updates (
 id CHAR(36) PRIMARY KEY,
 tenant_id CHAR(36) NOT NULL,
 goal_id CHAR(36) NOT NULL,
 updated_value DECIMAL(14,4) NOT NULL DEFAULT 0,
 notes TEXT NOT NULL DEFAULT '',
 updated_by_user_id CHAR(36) NULL,
 updated_by_name VARCHAR(180) NOT NULL DEFAULT '',
 updated_at_utc DATETIME(6) NOT NULL,
 INDEX ix_goal_progress_tenant_goal (tenant_id, goal_id)
);

CREATE TABLE IF NOT EXISTS appraisal_reviews (
 id CHAR(36) PRIMARY KEY,
 tenant_id CHAR(36) NOT NULL,
 cycle_id CHAR(36) NOT NULL,
 cycle_name VARCHAR(240) NOT NULL DEFAULT '',
 employee_id INT NOT NULL,
 employee_name VARCHAR(240) NOT NULL DEFAULT '',
 department_name VARCHAR(180) NOT NULL DEFAULT '',
 designation_title VARCHAR(180) NOT NULL DEFAULT '',
 scorecard_template_id CHAR(36) NOT NULL,
 kpi_score DECIMAL(5,2) NOT NULL DEFAULT 0,
 competency_score DECIMAL(5,2) NOT NULL DEFAULT 0,
 attendance_score DECIMAL(5,2) NOT NULL DEFAULT 0,
 productivity_score DECIMAL(5,2) NOT NULL DEFAULT 0,
 feedback_score DECIMAL(5,2) NOT NULL DEFAULT 0,
 discipline_score DECIMAL(5,2) NOT NULL DEFAULT 0,
 final_score DECIMAL(5,2) NOT NULL DEFAULT 0,
 final_rating VARCHAR(80) NOT NULL DEFAULT '',
 calibration_adjustment DECIMAL(5,2) NOT NULL DEFAULT 0,
 calibration_notes TEXT NOT NULL DEFAULT '',
 self_assessment_notes TEXT NOT NULL DEFAULT '',
 manager_notes TEXT NOT NULL DEFAULT '',
 hr_notes TEXT NOT NULL DEFAULT '',
 status VARCHAR(60) NOT NULL DEFAULT 'Pending',
 self_assessment_submitted_at DATETIME(6) NULL,
 manager_reviewed_at DATETIME(6) NULL,
 published_at DATETIME(6) NULL,
 acknowledged_at DATETIME(6) NULL,
 is_appealed BOOLEAN NOT NULL DEFAULT FALSE,
 reviewer_manager_id INT NULL,
 reviewer_manager_name VARCHAR(240) NOT NULL DEFAULT '',
 created_at_utc DATETIME(6) NOT NULL,
 updated_at_utc DATETIME(6) NULL,
 UNIQUE KEY ux_appraisal_review_cycle_emp (tenant_id, cycle_id, employee_id),
 INDEX ix_appraisal_reviews_tenant_status (tenant_id, status),
 INDEX ix_appraisal_reviews_tenant_dept (tenant_id, department_name)
);

CREATE TABLE IF NOT EXISTS appraisal_score_breakdowns (
 id CHAR(36) PRIMARY KEY,
 tenant_id CHAR(36) NOT NULL,
 review_id CHAR(36) NOT NULL,
 component VARCHAR(40) NOT NULL,
 raw_score DECIMAL(5,2) NOT NULL DEFAULT 0,
 weight DECIMAL(5,2) NOT NULL DEFAULT 0,
 weighted_score DECIMAL(5,2) NOT NULL DEFAULT 0,
 notes VARCHAR(500) NOT NULL DEFAULT '',
 INDEX ix_score_breakdowns_review (tenant_id, review_id)
);

CREATE TABLE IF NOT EXISTS appraisal_competency_ratings (
 id CHAR(36) PRIMARY KEY,
 tenant_id CHAR(36) NOT NULL,
 review_id CHAR(36) NOT NULL,
 competency_id CHAR(36) NOT NULL,
 competency_name VARCHAR(180) NOT NULL DEFAULT '',
 competency_category VARCHAR(40) NOT NULL DEFAULT '',
 self_rating DECIMAL(4,2) NOT NULL DEFAULT 0,
 manager_rating DECIMAL(4,2) NOT NULL DEFAULT 0,
 self_comments TEXT NOT NULL DEFAULT '',
 manager_comments TEXT NOT NULL DEFAULT '',
 weight DECIMAL(5,2) NOT NULL DEFAULT 100,
 UNIQUE KEY ux_comp_rating_review_comp (tenant_id, review_id, competency_id),
 INDEX ix_comp_ratings_review (tenant_id, review_id)
);

CREATE TABLE IF NOT EXISTS feedback_360 (
 id CHAR(36) PRIMARY KEY,
 tenant_id CHAR(36) NOT NULL,
 review_id CHAR(36) NOT NULL,
 reviewer_employee_id INT NOT NULL,
 reviewer_name VARCHAR(240) NOT NULL DEFAULT '',
 reviewer_role VARCHAR(40) NOT NULL DEFAULT '',
 is_anonymous BOOLEAN NOT NULL DEFAULT FALSE,
 score DECIMAL(4,2) NOT NULL DEFAULT 0,
 strengths TEXT NOT NULL DEFAULT '',
 improvements TEXT NOT NULL DEFAULT '',
 comments TEXT NOT NULL DEFAULT '',
 submitted_at DATETIME(6) NULL,
 created_at_utc DATETIME(6) NOT NULL,
 INDEX ix_feedback_360_review (tenant_id, review_id)
);

CREATE TABLE IF NOT EXISTS appraisal_calibrations (
 id CHAR(36) PRIMARY KEY,
 tenant_id CHAR(36) NOT NULL,
 review_id CHAR(36) NOT NULL,
 cycle_id CHAR(36) NOT NULL,
 employee_name VARCHAR(240) NOT NULL DEFAULT '',
 department_name VARCHAR(180) NOT NULL DEFAULT '',
 original_score DECIMAL(5,2) NOT NULL DEFAULT 0,
 adjusted_score DECIMAL(5,2) NOT NULL DEFAULT 0,
 adjustment_reason TEXT NOT NULL DEFAULT '',
 original_rating VARCHAR(80) NOT NULL DEFAULT '',
 adjusted_rating VARCHAR(80) NOT NULL DEFAULT '',
 calibrated_by_user_id CHAR(36) NULL,
 calibrated_by_name VARCHAR(180) NOT NULL DEFAULT '',
 calibrated_at_utc DATETIME(6) NOT NULL,
 INDEX ix_calibrations_tenant_cycle (tenant_id, cycle_id),
 INDEX ix_calibrations_review (tenant_id, review_id)
);

CREATE TABLE IF NOT EXISTS appraisal_appeals (
 id CHAR(36) PRIMARY KEY,
 tenant_id CHAR(36) NOT NULL,
 review_id CHAR(36) NOT NULL,
 employee_id INT NOT NULL,
 employee_name VARCHAR(240) NOT NULL DEFAULT '',
 appeal_reason TEXT NOT NULL DEFAULT '',
 employee_justification TEXT NOT NULL DEFAULT '',
 status VARCHAR(40) NOT NULL DEFAULT 'Submitted',
 hr_response TEXT NOT NULL DEFAULT '',
 reviewed_by_user_id CHAR(36) NULL,
 reviewed_by_name VARCHAR(180) NOT NULL DEFAULT '',
 submitted_at DATETIME(6) NOT NULL,
 reviewed_at DATETIME(6) NULL,
 INDEX ix_appeals_tenant_review (tenant_id, review_id),
 INDEX ix_appeals_tenant_status (tenant_id, status)
);

CREATE TABLE IF NOT EXISTS increment_recommendations (
 id CHAR(36) PRIMARY KEY,
 tenant_id CHAR(36) NOT NULL,
 review_id CHAR(36) NOT NULL,
 employee_id INT NOT NULL,
 employee_name VARCHAR(240) NOT NULL DEFAULT '',
 department_name VARCHAR(180) NOT NULL DEFAULT '',
 designation_title VARCHAR(180) NOT NULL DEFAULT '',
 current_salary DECIMAL(14,2) NOT NULL DEFAULT 0,
 recommended_increment_pct DECIMAL(5,2) NOT NULL DEFAULT 0,
 recommended_increment_amount DECIMAL(14,2) NOT NULL DEFAULT 0,
 new_salary DECIMAL(14,2) NOT NULL DEFAULT 0,
 effective_date DATE NOT NULL,
 reason TEXT NOT NULL DEFAULT '',
 status VARCHAR(40) NOT NULL DEFAULT 'Pending',
 recommended_by_user_id CHAR(36) NULL,
 recommended_by_name VARCHAR(180) NOT NULL DEFAULT '',
 approved_by_user_id CHAR(36) NULL,
 created_at_utc DATETIME(6) NOT NULL,
 approved_at_utc DATETIME(6) NULL,
 INDEX ix_increment_recs_tenant_status (tenant_id, status)
);

CREATE TABLE IF NOT EXISTS promotion_recommendations (
 id CHAR(36) PRIMARY KEY,
 tenant_id CHAR(36) NOT NULL,
 review_id CHAR(36) NOT NULL,
 employee_id INT NOT NULL,
 employee_name VARCHAR(240) NOT NULL DEFAULT '',
 department_name VARCHAR(180) NOT NULL DEFAULT '',
 current_designation VARCHAR(180) NOT NULL DEFAULT '',
 proposed_designation VARCHAR(180) NOT NULL DEFAULT '',
 effective_date DATE NOT NULL,
 reason TEXT NOT NULL DEFAULT '',
 status VARCHAR(40) NOT NULL DEFAULT 'Pending',
 recommended_by_user_id CHAR(36) NULL,
 recommended_by_name VARCHAR(180) NOT NULL DEFAULT '',
 approved_by_user_id CHAR(36) NULL,
 created_at_utc DATETIME(6) NOT NULL,
 approved_at_utc DATETIME(6) NULL,
 INDEX ix_promotion_recs_tenant_status (tenant_id, status)
);

CREATE TABLE IF NOT EXISTS bonus_recommendations (
 id CHAR(36) PRIMARY KEY,
 tenant_id CHAR(36) NOT NULL,
 review_id CHAR(36) NOT NULL,
 employee_id INT NOT NULL,
 employee_name VARCHAR(240) NOT NULL DEFAULT '',
 department_name VARCHAR(180) NOT NULL DEFAULT '',
 bonus_amount DECIMAL(14,2) NOT NULL DEFAULT 0,
 bonus_type VARCHAR(40) NOT NULL DEFAULT 'Performance',
 reason TEXT NOT NULL DEFAULT '',
 status VARCHAR(40) NOT NULL DEFAULT 'Pending',
 recommended_by_user_id CHAR(36) NULL,
 recommended_by_name VARCHAR(180) NOT NULL DEFAULT '',
 approved_by_user_id CHAR(36) NULL,
 created_at_utc DATETIME(6) NOT NULL,
 approved_at_utc DATETIME(6) NULL,
 INDEX ix_bonus_recs_tenant_status (tenant_id, status)
);

CREATE TABLE IF NOT EXISTS performance_improvement_plans (
 id CHAR(36) PRIMARY KEY,
 tenant_id CHAR(36) NOT NULL,
 employee_id INT NOT NULL,
 employee_name VARCHAR(240) NOT NULL DEFAULT '',
 department_name VARCHAR(180) NOT NULL DEFAULT '',
 trigger_review_id CHAR(36) NULL,
 performance_gaps TEXT NOT NULL DEFAULT '',
 improvement_goals TEXT NOT NULL DEFAULT '',
 support_plan TEXT NOT NULL DEFAULT '',
 start_date DATE NOT NULL,
 end_date DATE NOT NULL,
 status VARCHAR(60) NOT NULL DEFAULT 'Active',
 hr_notes TEXT NOT NULL DEFAULT '',
 manager_notes TEXT NOT NULL DEFAULT '',
 employee_comments TEXT NOT NULL DEFAULT '',
 initiated_by_user_id CHAR(36) NULL,
 initiated_by_name VARCHAR(180) NOT NULL DEFAULT '',
 created_at_utc DATETIME(6) NOT NULL,
 closed_at_utc DATETIME(6) NULL,
 INDEX ix_pips_tenant_employee (tenant_id, employee_id, status)
);

CREATE TABLE IF NOT EXISTS pip_check_ins (
 id CHAR(36) PRIMARY KEY,
 tenant_id CHAR(36) NOT NULL,
 pip_id CHAR(36) NOT NULL,
 check_in_date DATE NOT NULL,
 notes TEXT NOT NULL DEFAULT '',
 outcome VARCHAR(40) NOT NULL DEFAULT '',
 checked_by_user_id CHAR(36) NULL,
 checked_by_name VARCHAR(180) NOT NULL DEFAULT '',
 created_at_utc DATETIME(6) NOT NULL,
 INDEX ix_pip_checkins_tenant_pip (tenant_id, pip_id)
);

CREATE TABLE IF NOT EXISTS probation_reviews (
 id CHAR(36) PRIMARY KEY,
 tenant_id CHAR(36) NOT NULL,
 employee_id INT NOT NULL,
 employee_name VARCHAR(240) NOT NULL DEFAULT '',
 department_name VARCHAR(180) NOT NULL DEFAULT '',
 designation_title VARCHAR(180) NOT NULL DEFAULT '',
 probation_start_date DATE NOT NULL,
 probation_end_date DATE NOT NULL,
 review_due_date DATE NULL,
 performance_summary TEXT NOT NULL DEFAULT '',
 overall_rating DECIMAL(4,2) NOT NULL DEFAULT 0,
 manager_recommendation VARCHAR(40) NOT NULL DEFAULT '',
 manager_notes TEXT NOT NULL DEFAULT '',
 hr_decision VARCHAR(40) NOT NULL DEFAULT '',
 hr_notes TEXT NOT NULL DEFAULT '',
 status VARCHAR(40) NOT NULL DEFAULT 'Pending',
 reviewed_by_manager_user_id CHAR(36) NULL,
 reviewed_by_manager_name VARCHAR(180) NOT NULL DEFAULT '',
 approved_by_hr_user_id CHAR(36) NULL,
 manager_reviewed_at DATETIME(6) NULL,
 hr_approved_at DATETIME(6) NULL,
 created_at_utc DATETIME(6) NOT NULL,
 INDEX ix_probation_reviews_tenant_employee (tenant_id, employee_id, status)
);

CREATE TABLE IF NOT EXISTS continuous_feedback (
 id CHAR(36) PRIMARY KEY,
 tenant_id CHAR(36) NOT NULL,
 employee_id INT NOT NULL,
 employee_name VARCHAR(240) NOT NULL DEFAULT '',
 given_by_user_id CHAR(36) NULL,
 given_by_name VARCHAR(180) NOT NULL DEFAULT '',
 feedback_type VARCHAR(40) NOT NULL DEFAULT 'Note',
 content TEXT NOT NULL DEFAULT '',
 is_private BOOLEAN NOT NULL DEFAULT FALSE,
 linked_review_id CHAR(36) NULL,
 created_at_utc DATETIME(6) NOT NULL,
 INDEX ix_continuous_feedback_tenant_employee (tenant_id, employee_id),
 INDEX ix_continuous_feedback_tenant_type (tenant_id, feedback_type)
);

CREATE TABLE IF NOT EXISTS performance_audit_logs (
 id CHAR(36) PRIMARY KEY,
 tenant_id CHAR(36) NOT NULL,
 entity_type VARCHAR(80) NOT NULL,
 entity_id VARCHAR(80) NOT NULL,
 action VARCHAR(120) NOT NULL,
 old_value TEXT NOT NULL DEFAULT '',
 new_value TEXT NOT NULL DEFAULT '',
 reason TEXT NOT NULL DEFAULT '',
 performed_by_user_id CHAR(36) NULL,
 performed_by_name VARCHAR(180) NOT NULL DEFAULT '',
 created_at_utc DATETIME(6) NOT NULL,
 INDEX ix_perf_audit_logs_entity (tenant_id, entity_type, entity_id),
 INDEX ix_perf_audit_logs_time (tenant_id, created_at_utc)
);

-- ═══════════════════════════════════════════════════════════════════
-- LEAVE MANAGEMENT MODULE
-- ═══════════════════════════════════════════════════════════════════

CREATE TABLE IF NOT EXISTS leave_types (
  id CHAR(36) PRIMARY KEY,
  tenant_id CHAR(36) NOT NULL,
  code VARCHAR(80) NOT NULL,
  name_en VARCHAR(180) NOT NULL,
  name_ar VARCHAR(180) NOT NULL DEFAULT '',
  category VARCHAR(80) NOT NULL DEFAULT '',
  is_paid BOOLEAN NOT NULL DEFAULT TRUE,
  is_half_day_allowed BOOLEAN NOT NULL DEFAULT TRUE,
  is_hourly_allowed BOOLEAN NOT NULL DEFAULT FALSE,
  requires_attachment BOOLEAN NOT NULL DEFAULT FALSE,
  requires_reason BOOLEAN NOT NULL DEFAULT FALSE,
  max_consecutive_days INT NOT NULL DEFAULT 0,
  color_code VARCHAR(20) NOT NULL DEFAULT '#2F6BFF',
  is_active BOOLEAN NOT NULL DEFAULT TRUE,
  sort_order INT NOT NULL DEFAULT 0,
  created_at_utc DATETIME(6) NOT NULL,
  UNIQUE KEY ux_leave_types_tenant_code (tenant_id, code),
  INDEX ix_leave_types_tenant_active (tenant_id, is_active)
);

CREATE TABLE IF NOT EXISTS leave_policies (
  id CHAR(36) PRIMARY KEY,
  tenant_id CHAR(36) NOT NULL,
  name VARCHAR(240) NOT NULL,
  leave_type_id CHAR(36) NOT NULL,
  country_code VARCHAR(10) NOT NULL DEFAULT '',
  company_id CHAR(36) NULL,
  branch_id CHAR(36) NULL,
  department_name VARCHAR(180) NOT NULL DEFAULT '',
  grade VARCHAR(80) NOT NULL DEFAULT '',
  employment_type VARCHAR(80) NOT NULL DEFAULT '',
  contract_type VARCHAR(80) NOT NULL DEFAULT '',
  gender VARCHAR(20) NOT NULL DEFAULT '',
  applies_on_probation BOOLEAN NOT NULL DEFAULT FALSE,
  annual_entitlement_days DECIMAL(6,2) NOT NULL DEFAULT 21,
  accrual_method VARCHAR(40) NOT NULL DEFAULT 'Monthly',
  carry_forward_max DECIMAL(6,2) NOT NULL DEFAULT 0,
  carry_forward_expiry INT NOT NULL DEFAULT 0,
  encashment_allowed BOOLEAN NOT NULL DEFAULT FALSE,
  encashment_max_days DECIMAL(6,2) NOT NULL DEFAULT 0,
  minimum_days_per_request DECIMAL(5,2) NOT NULL DEFAULT 1,
  maximum_days_per_request DECIMAL(5,2) NOT NULL DEFAULT 0,
  notice_required_days INT NOT NULL DEFAULT 0,
  weekends_included BOOLEAN NOT NULL DEFAULT FALSE,
  public_holidays_included BOOLEAN NOT NULL DEFAULT FALSE,
  payroll_impact VARCHAR(40) NOT NULL DEFAULT 'Full',
  approval_workflow_id CHAR(36) NULL,
  status VARCHAR(40) NOT NULL DEFAULT 'Draft',
  created_at_utc DATETIME(6) NOT NULL,
  updated_at_utc DATETIME(6) NULL,
  INDEX ix_leave_policies_tenant_type (tenant_id, leave_type_id, status)
);

CREATE TABLE IF NOT EXISTS employee_leave_balances (
  id CHAR(36) PRIMARY KEY,
  tenant_id CHAR(36) NOT NULL,
  employee_id INT NOT NULL,
  employee_name VARCHAR(180) NOT NULL DEFAULT '',
  leave_type_id CHAR(36) NOT NULL,
  leave_type_name VARCHAR(180) NOT NULL DEFAULT '',
  year INT NOT NULL,
  entitled DECIMAL(7,2) NOT NULL DEFAULT 0,
  accrued DECIMAL(7,2) NOT NULL DEFAULT 0,
  used DECIMAL(7,2) NOT NULL DEFAULT 0,
  pending DECIMAL(7,2) NOT NULL DEFAULT 0,
  carried_forward DECIMAL(7,2) NOT NULL DEFAULT 0,
  encashed DECIMAL(7,2) NOT NULL DEFAULT 0,
  expired DECIMAL(7,2) NOT NULL DEFAULT 0,
  manual_adjustment DECIMAL(7,2) NOT NULL DEFAULT 0,
  negative_allowed BOOLEAN NOT NULL DEFAULT FALSE,
  created_at_utc DATETIME(6) NOT NULL,
  updated_at_utc DATETIME(6) NULL,
  UNIQUE KEY ux_employee_leave_balances_emp_type_year (tenant_id, employee_id, leave_type_id, year),
  INDEX ix_employee_leave_balances_tenant_emp (tenant_id, employee_id)
);

CREATE TABLE IF NOT EXISTS leave_balance_transactions (
  id CHAR(36) PRIMARY KEY,
  tenant_id CHAR(36) NOT NULL,
  employee_id INT NOT NULL,
  leave_type_id CHAR(36) NOT NULL,
  year INT NOT NULL,
  transaction_type VARCHAR(60) NOT NULL,
  amount DECIMAL(7,2) NOT NULL,
  balance_before DECIMAL(7,2) NOT NULL,
  balance_after DECIMAL(7,2) NOT NULL,
  reference VARCHAR(240) NOT NULL DEFAULT '',
  reason VARCHAR(500) NOT NULL DEFAULT '',
  performed_by_name VARCHAR(180) NOT NULL DEFAULT '',
  created_at_utc DATETIME(6) NOT NULL,
  INDEX ix_leave_balance_txn_tenant_emp (tenant_id, employee_id, leave_type_id)
);

CREATE TABLE IF NOT EXISTS leave_requests (
  id CHAR(36) PRIMARY KEY,
  tenant_id CHAR(36) NOT NULL,
  employee_id INT NOT NULL,
  employee_name VARCHAR(180) NOT NULL DEFAULT '',
  department_name VARCHAR(180) NOT NULL DEFAULT '',
  designation_title VARCHAR(180) NOT NULL DEFAULT '',
  leave_type_id CHAR(36) NOT NULL,
  leave_type_name VARCHAR(180) NOT NULL DEFAULT '',
  policy_id CHAR(36) NULL,
  start_date DATE NOT NULL,
  end_date DATE NOT NULL,
  total_days DECIMAL(6,2) NOT NULL DEFAULT 1,
  day_type VARCHAR(20) NOT NULL DEFAULT 'Full',
  hours_requested DECIMAL(5,2) NOT NULL DEFAULT 0,
  reason VARCHAR(1000) NOT NULL DEFAULT '',
  is_emergency BOOLEAN NOT NULL DEFAULT FALSE,
  attachment_path VARCHAR(500) NOT NULL DEFAULT '',
  payroll_impact VARCHAR(40) NOT NULL DEFAULT 'Full',
  status VARCHAR(60) NOT NULL DEFAULT 'Draft',
  manager_approval_notes VARCHAR(500) NOT NULL DEFAULT '',
  hr_approval_notes VARCHAR(500) NOT NULL DEFAULT '',
  rejection_reason VARCHAR(500) NOT NULL DEFAULT '',
  cancellation_reason VARCHAR(500) NOT NULL DEFAULT '',
  return_date DATE NULL,
  delegate_employee_id INT NULL,
  delegate_employee_name VARCHAR(180) NOT NULL DEFAULT '',
  created_at_utc DATETIME(6) NOT NULL,
  submitted_at_utc DATETIME(6) NULL,
  decided_at_utc DATETIME(6) NULL,
  cancelled_at_utc DATETIME(6) NULL,
  INDEX ix_leave_requests_tenant_status (tenant_id, status),
  INDEX ix_leave_requests_tenant_emp_date (tenant_id, employee_id, start_date)
);

CREATE TABLE IF NOT EXISTS leave_approvals (
  id CHAR(36) PRIMARY KEY,
  tenant_id CHAR(36) NOT NULL,
  leave_request_id CHAR(36) NOT NULL,
  step_number INT NOT NULL DEFAULT 1,
  approver_role VARCHAR(80) NOT NULL DEFAULT '',
  approver_id CHAR(36) NULL,
  approver_name VARCHAR(180) NOT NULL DEFAULT '',
  decision VARCHAR(40) NOT NULL DEFAULT 'Pending',
  notes VARCHAR(500) NOT NULL DEFAULT '',
  acted_at_utc DATETIME(6) NULL,
  created_at_utc DATETIME(6) NOT NULL,
  INDEX ix_leave_approvals_request (tenant_id, leave_request_id)
);

CREATE TABLE IF NOT EXISTS leave_cancellation_requests (
  id CHAR(36) PRIMARY KEY,
  tenant_id CHAR(36) NOT NULL,
  leave_request_id CHAR(36) NOT NULL,
  employee_id INT NOT NULL,
  reason VARCHAR(500) NOT NULL DEFAULT '',
  status VARCHAR(40) NOT NULL DEFAULT 'Pending',
  reviewed_by_name VARCHAR(180) NOT NULL DEFAULT '',
  review_notes VARCHAR(500) NOT NULL DEFAULT '',
  created_at_utc DATETIME(6) NOT NULL,
  reviewed_at_utc DATETIME(6) NULL
);

CREATE TABLE IF NOT EXISTS leave_modification_requests (
  id CHAR(36) PRIMARY KEY,
  tenant_id CHAR(36) NOT NULL,
  leave_request_id CHAR(36) NOT NULL,
  employee_id INT NOT NULL,
  new_start_date DATE NOT NULL,
  new_end_date DATE NOT NULL,
  new_total_days DECIMAL(6,2) NOT NULL DEFAULT 1,
  reason VARCHAR(500) NOT NULL DEFAULT '',
  status VARCHAR(40) NOT NULL DEFAULT 'Pending',
  reviewed_by_name VARCHAR(180) NOT NULL DEFAULT '',
  review_notes VARCHAR(500) NOT NULL DEFAULT '',
  created_at_utc DATETIME(6) NOT NULL,
  reviewed_at_utc DATETIME(6) NULL
);

CREATE TABLE IF NOT EXISTS public_holiday_calendars (
  id CHAR(36) PRIMARY KEY,
  tenant_id CHAR(36) NOT NULL,
  name VARCHAR(240) NOT NULL,
  country_code VARCHAR(10) NOT NULL DEFAULT '',
  company_id CHAR(36) NULL,
  branch_id CHAR(36) NULL,
  calendar_year INT NOT NULL,
  is_active BOOLEAN NOT NULL DEFAULT TRUE,
  created_at_utc DATETIME(6) NOT NULL,
  INDEX ix_phcal_tenant_country_year (tenant_id, country_code, calendar_year)
);

CREATE TABLE IF NOT EXISTS public_holidays (
  id CHAR(36) PRIMARY KEY,
  tenant_id CHAR(36) NOT NULL,
  calendar_id CHAR(36) NOT NULL,
  name_en VARCHAR(240) NOT NULL,
  name_ar VARCHAR(240) NOT NULL DEFAULT '',
  date DATE NOT NULL,
  hijri_date VARCHAR(40) NOT NULL DEFAULT '',
  is_recurring BOOLEAN NOT NULL DEFAULT FALSE,
  is_optional BOOLEAN NOT NULL DEFAULT FALSE,
  holiday_type VARCHAR(40) NOT NULL DEFAULT 'National',
  notes VARCHAR(500) NOT NULL DEFAULT '',
  created_at_utc DATETIME(6) NOT NULL,
  INDEX ix_public_holidays_tenant_cal_date (tenant_id, calendar_id, date)
);

CREATE TABLE IF NOT EXISTS leave_blackout_dates (
  id CHAR(36) PRIMARY KEY,
  tenant_id CHAR(36) NOT NULL,
  name_en VARCHAR(240) NOT NULL,
  start_date DATE NOT NULL,
  end_date DATE NOT NULL,
  department_name VARCHAR(180) NOT NULL DEFAULT '',
  reason VARCHAR(500) NOT NULL DEFAULT '',
  is_company_wide BOOLEAN NOT NULL DEFAULT FALSE,
  created_at_utc DATETIME(6) NOT NULL,
  INDEX ix_leave_blackout_tenant_date (tenant_id, start_date)
);

CREATE TABLE IF NOT EXISTS leave_encashment_requests (
  id CHAR(36) PRIMARY KEY,
  tenant_id CHAR(36) NOT NULL,
  employee_id INT NOT NULL,
  employee_name VARCHAR(180) NOT NULL DEFAULT '',
  leave_type_id CHAR(36) NOT NULL,
  leave_type_name VARCHAR(180) NOT NULL DEFAULT '',
  year INT NOT NULL,
  days_to_encash DECIMAL(6,2) NOT NULL,
  amount_per_day DECIMAL(10,2) NOT NULL DEFAULT 0,
  total_amount DECIMAL(12,2) NOT NULL DEFAULT 0,
  reason VARCHAR(500) NOT NULL DEFAULT '',
  status VARCHAR(40) NOT NULL DEFAULT 'Pending',
  hr_notes VARCHAR(500) NOT NULL DEFAULT '',
  payroll_notes VARCHAR(500) NOT NULL DEFAULT '',
  created_at_utc DATETIME(6) NOT NULL,
  processed_at_utc DATETIME(6) NULL,
  INDEX ix_leave_encashment_tenant_status (tenant_id, status)
);

CREATE TABLE IF NOT EXISTS comp_off_credits (
  id CHAR(36) PRIMARY KEY,
  tenant_id CHAR(36) NOT NULL,
  employee_id INT NOT NULL,
  employee_name VARCHAR(180) NOT NULL DEFAULT '',
  worked_date DATE NOT NULL,
  work_type VARCHAR(40) NOT NULL DEFAULT 'Overtime',
  hours_worked DECIMAL(5,2) NOT NULL DEFAULT 0,
  days_earned DECIMAL(5,2) NOT NULL DEFAULT 0,
  expiry_date DATE NULL,
  status VARCHAR(40) NOT NULL DEFAULT 'Pending',
  manager_approval_notes VARCHAR(500) NOT NULL DEFAULT '',
  approved_by_name VARCHAR(180) NOT NULL DEFAULT '',
  created_at_utc DATETIME(6) NOT NULL,
  approved_at_utc DATETIME(6) NULL,
  INDEX ix_comp_off_tenant_emp_status (tenant_id, employee_id, status)
);

CREATE TABLE IF NOT EXISTS comp_off_usages (
  id CHAR(36) PRIMARY KEY,
  tenant_id CHAR(36) NOT NULL,
  employee_id INT NOT NULL,
  comp_off_credit_id CHAR(36) NOT NULL,
  leave_request_id CHAR(36) NULL,
  days_used DECIMAL(5,2) NOT NULL DEFAULT 0,
  created_at_utc DATETIME(6) NOT NULL
);

CREATE TABLE IF NOT EXISTS absence_records (
  id CHAR(36) PRIMARY KEY,
  tenant_id CHAR(36) NOT NULL,
  employee_id INT NOT NULL,
  employee_name VARCHAR(180) NOT NULL DEFAULT '',
  department_name VARCHAR(180) NOT NULL DEFAULT '',
  absence_date DATE NOT NULL,
  absence_type VARCHAR(60) NOT NULL DEFAULT 'Unauthorized',
  is_regularized BOOLEAN NOT NULL DEFAULT FALSE,
  payroll_impact VARCHAR(40) NOT NULL DEFAULT 'Deduction',
  regularization_request_id CHAR(36) NULL,
  created_at_utc DATETIME(6) NOT NULL,
  INDEX ix_absence_records_tenant_emp_date (tenant_id, employee_id, absence_date)
);

CREATE TABLE IF NOT EXISTS absence_regularization_requests (
  id CHAR(36) PRIMARY KEY,
  tenant_id CHAR(36) NOT NULL,
  employee_id INT NOT NULL,
  employee_name VARCHAR(180) NOT NULL DEFAULT '',
  absence_record_id CHAR(36) NOT NULL,
  reason VARCHAR(1000) NOT NULL DEFAULT '',
  leave_type_id CHAR(36) NULL,
  status VARCHAR(40) NOT NULL DEFAULT 'Pending',
  manager_notes VARCHAR(500) NOT NULL DEFAULT '',
  hr_notes VARCHAR(500) NOT NULL DEFAULT '',
  created_at_utc DATETIME(6) NOT NULL,
  reviewed_at_utc DATETIME(6) NULL,
  INDEX ix_absence_reg_tenant_status (tenant_id, status)
);

CREATE TABLE IF NOT EXISTS leave_delegations (
  id CHAR(36) PRIMARY KEY,
  tenant_id CHAR(36) NOT NULL,
  employee_id INT NOT NULL,
  employee_name VARCHAR(180) NOT NULL DEFAULT '',
  delegate_employee_id INT NOT NULL,
  delegate_employee_name VARCHAR(180) NOT NULL DEFAULT '',
  leave_request_id CHAR(36) NULL,
  start_date DATE NOT NULL,
  end_date DATE NOT NULL,
  delegation_type VARCHAR(60) NOT NULL DEFAULT 'ApprovalOnly',
  notes VARCHAR(500) NOT NULL DEFAULT '',
  status VARCHAR(40) NOT NULL DEFAULT 'Active',
  created_at_utc DATETIME(6) NOT NULL,
  INDEX ix_leave_delegations_tenant_emp (tenant_id, employee_id, status)
);

CREATE TABLE IF NOT EXISTS leave_payroll_impacts (
  id CHAR(36) PRIMARY KEY,
  tenant_id CHAR(36) NOT NULL,
  leave_request_id CHAR(36) NOT NULL,
  employee_id INT NOT NULL,
  pay_period VARCHAR(20) NOT NULL DEFAULT '',
  impact_type VARCHAR(40) NOT NULL DEFAULT 'Deduction',
  days DECIMAL(6,2) NOT NULL DEFAULT 0,
  amount DECIMAL(12,2) NOT NULL DEFAULT 0,
  status VARCHAR(40) NOT NULL DEFAULT 'Pending',
  processed_at_utc DATETIME(6) NULL,
  created_at_utc DATETIME(6) NOT NULL,
  INDEX ix_leave_payroll_tenant_status (tenant_id, status)
);

CREATE TABLE IF NOT EXISTS leave_audit_logs (
  id CHAR(36) PRIMARY KEY,
  tenant_id CHAR(36) NOT NULL,
  entity_type VARCHAR(80) NOT NULL,
  entity_id VARCHAR(80) NOT NULL,
  action VARCHAR(80) NOT NULL,
  old_value TEXT NOT NULL DEFAULT '',
  new_value TEXT NOT NULL DEFAULT '',
  performed_by_name VARCHAR(180) NOT NULL DEFAULT '',
  reason VARCHAR(500) NOT NULL DEFAULT '',
  created_at_utc DATETIME(6) NOT NULL,
  INDEX ix_leave_audit_tenant_entity (tenant_id, entity_type, entity_id)
);

CREATE TABLE IF NOT EXISTS leave_ai_insights (
  id CHAR(36) PRIMARY KEY,
  tenant_id CHAR(36) NOT NULL,
  insight_type VARCHAR(80) NOT NULL,
  severity VARCHAR(20) NOT NULL DEFAULT 'Info',
  title VARCHAR(240) NOT NULL,
  summary TEXT NOT NULL,
  affected_employee_id INT NULL,
  affected_department VARCHAR(180) NOT NULL DEFAULT '',
  data JSON NOT NULL,
  is_acknowledged BOOLEAN NOT NULL DEFAULT FALSE,
  acknowledged_by_name VARCHAR(180) NOT NULL DEFAULT '',
  created_at_utc DATETIME(6) NOT NULL,
  INDEX ix_leave_ai_insights_tenant_type (tenant_id, insight_type, is_acknowledged)
);

-- Employee Management live-persistence tables added by EF migration
-- 20260523095025_EmployeeManagementLivePersistence.
CREATE TABLE IF NOT EXISTS grades (
  id CHAR(36) PRIMARY KEY,
  tenant_id CHAR(36) NOT NULL,
  code VARCHAR(80) NOT NULL,
  name VARCHAR(180) NOT NULL DEFAULT '',
  band VARCHAR(80) NOT NULL DEFAULT '',
  level INT NOT NULL DEFAULT 0,
  is_active BOOLEAN NOT NULL DEFAULT TRUE,
  created_at_utc DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
  created_by CHAR(36) NULL,
  updated_at_utc DATETIME(6) NULL,
  updated_by CHAR(36) NULL,
  is_deleted BOOLEAN NOT NULL DEFAULT FALSE,
  deleted_at_utc DATETIME(6) NULL,
  deleted_by CHAR(36) NULL,
  UNIQUE KEY ux_grades_tenant_code (tenant_id, code)
);

CREATE TABLE IF NOT EXISTS cost_centers (
  id CHAR(36) PRIMARY KEY,
  tenant_id CHAR(36) NOT NULL,
  company_id CHAR(36) NULL,
  code VARCHAR(80) NOT NULL,
  name VARCHAR(180) NOT NULL DEFAULT '',
  is_active BOOLEAN NOT NULL DEFAULT TRUE,
  created_at_utc DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
  created_by CHAR(36) NULL,
  updated_at_utc DATETIME(6) NULL,
  updated_by CHAR(36) NULL,
  is_deleted BOOLEAN NOT NULL DEFAULT FALSE,
  deleted_at_utc DATETIME(6) NULL,
  deleted_by CHAR(36) NULL,
  UNIQUE KEY ux_cost_centers_tenant_code (tenant_id, code),
  INDEX ix_cost_centers_company (tenant_id, company_id)
);

CREATE TABLE IF NOT EXISTS employee_id_rules (
  id CHAR(36) PRIMARY KEY,
  tenant_id CHAR(36) NOT NULL,
  company_id CHAR(36) NULL,
  name VARCHAR(180) NOT NULL DEFAULT 'Default employee ID rule',
  company_prefix VARCHAR(20) NOT NULL DEFAULT 'ZAY',
  use_country_prefix BOOLEAN NOT NULL DEFAULT TRUE,
  use_branch_prefix BOOLEAN NOT NULL DEFAULT FALSE,
  use_department_prefix BOOLEAN NOT NULL DEFAULT TRUE,
  use_year BOOLEAN NOT NULL DEFAULT TRUE,
  padding_length INT NOT NULL DEFAULT 4,
  next_sequence INT NOT NULL DEFAULT 1,
  allow_manual_override BOOLEAN NOT NULL DEFAULT FALSE,
  is_active BOOLEAN NOT NULL DEFAULT TRUE,
  created_at_utc DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
  created_by CHAR(36) NULL,
  updated_at_utc DATETIME(6) NULL,
  updated_by CHAR(36) NULL,
  is_deleted BOOLEAN NOT NULL DEFAULT FALSE,
  deleted_at_utc DATETIME(6) NULL,
  deleted_by CHAR(36) NULL,
  INDEX ix_employee_id_rules_tenant_company (tenant_id, company_id, is_active)
);

CREATE TABLE IF NOT EXISTS employee_payroll_profiles (
  id CHAR(36) PRIMARY KEY,
  tenant_id CHAR(36) NOT NULL,
  employee_id INT NOT NULL,
  bank_name VARCHAR(160) NOT NULL DEFAULT '',
  iban VARCHAR(80) NOT NULL DEFAULT '',
  account_number VARCHAR(80) NOT NULL DEFAULT '',
  payment_method VARCHAR(80) NOT NULL DEFAULT 'BankTransfer',
  salary_currency VARCHAR(10) NOT NULL DEFAULT 'AED',
  payroll_group VARCHAR(120) NOT NULL DEFAULT '',
  salary_structure_reference VARCHAR(120) NOT NULL DEFAULT '',
  wps_eligible BOOLEAN NOT NULL DEFAULT TRUE,
  eosb_eligible BOOLEAN NOT NULL DEFAULT TRUE,
  social_insurance_reference VARCHAR(120) NOT NULL DEFAULT '',
  created_at_utc DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
  created_by CHAR(36) NULL,
  updated_at_utc DATETIME(6) NULL,
  updated_by CHAR(36) NULL,
  is_deleted BOOLEAN NOT NULL DEFAULT FALSE,
  deleted_at_utc DATETIME(6) NULL,
  deleted_by CHAR(36) NULL,
  UNIQUE KEY ux_employee_payroll_profiles_employee (tenant_id, employee_id)
);

CREATE TABLE IF NOT EXISTS employee_compliance_records (
  id CHAR(36) PRIMARY KEY,
  tenant_id CHAR(36) NOT NULL,
  employee_id INT NOT NULL,
  country_code VARCHAR(10) NOT NULL DEFAULT '',
  field_key VARCHAR(120) NOT NULL,
  field_label VARCHAR(180) NOT NULL DEFAULT '',
  field_value TEXT NOT NULL,
  issue_date DATE NULL,
  expiry_date DATE NULL,
  is_sensitive BOOLEAN NOT NULL DEFAULT TRUE,
  is_required BOOLEAN NOT NULL DEFAULT FALSE,
  created_at_utc DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
  created_by CHAR(36) NULL,
  updated_at_utc DATETIME(6) NULL,
  updated_by CHAR(36) NULL,
  is_deleted BOOLEAN NOT NULL DEFAULT FALSE,
  deleted_at_utc DATETIME(6) NULL,
  deleted_by CHAR(36) NULL,
  UNIQUE KEY ux_employee_compliance_field (tenant_id, employee_id, country_code, field_key),
  INDEX ix_employee_compliance_expiry (tenant_id, expiry_date)
);

CREATE TABLE IF NOT EXISTS employee_status_histories (
  id CHAR(36) PRIMARY KEY,
  tenant_id CHAR(36) NOT NULL,
  employee_id INT NOT NULL,
  old_status VARCHAR(80) NOT NULL DEFAULT '',
  new_status VARCHAR(80) NOT NULL DEFAULT '',
  effective_date DATE NOT NULL,
  reason VARCHAR(1000) NOT NULL DEFAULT '',
  changed_by_user_id CHAR(36) NULL,
  created_at_utc DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
  INDEX ix_employee_status_history_employee (tenant_id, employee_id, created_at_utc)
);

CREATE TABLE IF NOT EXISTS employee_document_versions (
  id CHAR(36) PRIMARY KEY,
  tenant_id CHAR(36) NOT NULL,
  employee_document_id CHAR(36) NOT NULL,
  version_number INT NOT NULL DEFAULT 1,
  file_name VARCHAR(240) NOT NULL DEFAULT '',
  content_type VARCHAR(120) NOT NULL DEFAULT '',
  storage_url VARCHAR(500) NOT NULL DEFAULT '',
  created_by CHAR(36) NULL,
  created_at_utc DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
  UNIQUE KEY ux_employee_document_versions (tenant_id, employee_document_id, version_number)
);
