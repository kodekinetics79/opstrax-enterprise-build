-- Phase 1 hotfix: extend ai_hr_query_logs for the new AI audit fields.
-- Safe to run multiple times. No columns are dropped or renamed.

DROP PROCEDURE IF EXISTS add_ai_hr_query_log_phase1_columns;
DELIMITER //

CREATE PROCEDURE add_ai_hr_query_log_phase1_columns()
BEGIN
  IF NOT EXISTS (
    SELECT 1
    FROM information_schema.COLUMNS
    WHERE TABLE_SCHEMA = DATABASE()
      AND TABLE_NAME = 'ai_hr_query_logs'
      AND COLUMN_NAME = 'logged_prompt'
  ) THEN
    ALTER TABLE ai_hr_query_logs
      ADD COLUMN logged_prompt LONGTEXT NULL COMMENT 'Raw prompt text when AI_LOG_PROMPTS=true; otherwise left empty.';
  END IF;

  IF NOT EXISTS (
    SELECT 1
    FROM information_schema.COLUMNS
    WHERE TABLE_SCHEMA = DATABASE()
      AND TABLE_NAME = 'ai_hr_query_logs'
      AND COLUMN_NAME = 'prompt_hash'
  ) THEN
    ALTER TABLE ai_hr_query_logs
      ADD COLUMN prompt_hash VARCHAR(128) NULL COMMENT 'SHA-256 hash of the effective prompt for audit correlation.';
  END IF;

  IF NOT EXISTS (
    SELECT 1
    FROM information_schema.COLUMNS
    WHERE TABLE_SCHEMA = DATABASE()
      AND TABLE_NAME = 'ai_hr_query_logs'
      AND COLUMN_NAME = 'prompt_summary'
  ) THEN
    ALTER TABLE ai_hr_query_logs
      ADD COLUMN prompt_summary LONGTEXT NULL COMMENT 'Redacted prompt summary when raw prompt logging is disabled.';
  END IF;

  IF NOT EXISTS (
    SELECT 1
    FROM information_schema.COLUMNS
    WHERE TABLE_SCHEMA = DATABASE()
      AND TABLE_NAME = 'ai_hr_query_logs'
      AND COLUMN_NAME = 'module'
  ) THEN
    ALTER TABLE ai_hr_query_logs
      ADD COLUMN module VARCHAR(50) NULL COMMENT 'AI module classification such as Payroll, Leave, HR, Attendance, or Organization.';
  END IF;

  IF NOT EXISTS (
    SELECT 1
    FROM information_schema.COLUMNS
    WHERE TABLE_SCHEMA = DATABASE()
      AND TABLE_NAME = 'ai_hr_query_logs'
      AND COLUMN_NAME = 'provider'
  ) THEN
    ALTER TABLE ai_hr_query_logs
      ADD COLUMN provider VARCHAR(50) NULL COMMENT 'AI provider used for the request, for example fallback, anthropic, openai, or policy.';
  END IF;

  IF NOT EXISTS (
    SELECT 1
    FROM information_schema.COLUMNS
    WHERE TABLE_SCHEMA = DATABASE()
      AND TABLE_NAME = 'ai_hr_query_logs'
      AND COLUMN_NAME = 'model'
  ) THEN
    ALTER TABLE ai_hr_query_logs
      ADD COLUMN model VARCHAR(100) NULL COMMENT 'Model name used for the AI request.';
  END IF;

  IF NOT EXISTS (
    SELECT 1
    FROM information_schema.COLUMNS
    WHERE TABLE_SCHEMA = DATABASE()
      AND TABLE_NAME = 'ai_hr_query_logs'
      AND COLUMN_NAME = 'response_status'
  ) THEN
    ALTER TABLE ai_hr_query_logs
      ADD COLUMN response_status VARCHAR(50) NULL COMMENT 'Outcome of the response path, such as provider_success, fallback, or blocked.';
  END IF;

  IF NOT EXISTS (
    SELECT 1
    FROM information_schema.COLUMNS
    WHERE TABLE_SCHEMA = DATABASE()
      AND TABLE_NAME = 'ai_hr_query_logs'
      AND COLUMN_NAME = 'human_review_required'
  ) THEN
    ALTER TABLE ai_hr_query_logs
      ADD COLUMN human_review_required TINYINT(1) NOT NULL DEFAULT 0 COMMENT 'Marks responses that require human review.';
  END IF;

  IF NOT EXISTS (
    SELECT 1
    FROM information_schema.COLUMNS
    WHERE TABLE_SCHEMA = DATABASE()
      AND TABLE_NAME = 'ai_hr_query_logs'
      AND COLUMN_NAME = 'prompt_tokens'
  ) THEN
    ALTER TABLE ai_hr_query_logs
      ADD COLUMN prompt_tokens INT NOT NULL DEFAULT 0 COMMENT 'Prompt token count when available or estimated.';
  END IF;

  IF NOT EXISTS (
    SELECT 1
    FROM information_schema.COLUMNS
    WHERE TABLE_SCHEMA = DATABASE()
      AND COLUMN_NAME = 'completion_tokens'
      AND TABLE_NAME = 'ai_hr_query_logs'
      AND TABLE_SCHEMA = DATABASE()
  ) THEN
    ALTER TABLE ai_hr_query_logs
      ADD COLUMN completion_tokens INT NOT NULL DEFAULT 0 COMMENT 'Completion token count when available or estimated.';
  END IF;
END//

DELIMITER ;

CALL add_ai_hr_query_log_phase1_columns();
DROP PROCEDURE add_ai_hr_query_log_phase1_columns;
