-- AI chatbot cache hotfix: widen role/permission signature columns so cache keys
-- can safely store longer role/permission sets without truncation.
-- Safe to run multiple times. No columns are dropped or renamed.

DROP PROCEDURE IF EXISTS expand_ai_hr_query_cache_signature_columns;
DELIMITER //

CREATE PROCEDURE expand_ai_hr_query_cache_signature_columns()
BEGIN
  IF EXISTS (
    SELECT 1
    FROM information_schema.COLUMNS
    WHERE TABLE_SCHEMA = DATABASE()
      AND TABLE_NAME = 'ai_hr_query_cache'
      AND COLUMN_NAME = 'user_role_signature'
      AND (DATA_TYPE <> 'longtext' OR CHARACTER_MAXIMUM_LENGTH IS NULL)
  ) THEN
    ALTER TABLE ai_hr_query_cache
      MODIFY COLUMN user_role_signature LONGTEXT NOT NULL COMMENT 'Sorted, normalized caller role signature used to prevent cross-role cache reuse.';
  END IF;

  IF EXISTS (
    SELECT 1
    FROM information_schema.COLUMNS
    WHERE TABLE_SCHEMA = DATABASE()
      AND TABLE_NAME = 'ai_hr_query_cache'
      AND COLUMN_NAME = 'permission_signature'
      AND (DATA_TYPE <> 'longtext' OR CHARACTER_MAXIMUM_LENGTH IS NULL)
  ) THEN
    ALTER TABLE ai_hr_query_cache
      MODIFY COLUMN permission_signature LONGTEXT NOT NULL COMMENT 'Sorted, normalized caller permission signature used to prevent cross-permission cache reuse.';
  END IF;
END//

DELIMITER ;

CALL expand_ai_hr_query_cache_signature_columns();
DROP PROCEDURE expand_ai_hr_query_cache_signature_columns;
