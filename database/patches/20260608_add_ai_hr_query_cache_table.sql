-- AI chatbot cache: stores advisory-only responses for repeated tenant-safe queries.
-- Safe to run multiple times. No existing tables are dropped or renamed.

CREATE TABLE IF NOT EXISTS ai_hr_query_cache (
  id CHAR(36) NOT NULL COMMENT 'Primary key for the cached AI response row.',
  tenant_id CHAR(36) NOT NULL COMMENT 'Tenant owning the cached answer; never shared across tenants.',
  cache_key VARCHAR(191) NOT NULL COMMENT 'Deterministic lookup key derived from tenant, intent, role signature, permission signature, and normalized query.',
  query_hash VARCHAR(128) NOT NULL COMMENT 'SHA-256 hash of the normalized query used for audit correlation.',
  normalized_query LONGTEXT NOT NULL COMMENT 'Whitespace-normalized query text used to build the cache key.',
  intent_classified VARCHAR(80) NOT NULL COMMENT 'Intent classification resolved by governance before the provider call.',
  module VARCHAR(50) NOT NULL COMMENT 'Module classification resolved by governance before the provider call.',
  employee_id INT NULL COMMENT 'Employee context attached to the query when applicable.',
  user_role_signature VARCHAR(255) NOT NULL COMMENT 'Sorted, normalized caller role signature used to prevent cross-role cache reuse.',
  permission_signature VARCHAR(255) NOT NULL COMMENT 'Sorted, normalized caller permission signature used to prevent cross-permission cache reuse.',
  answer LONGTEXT NOT NULL COMMENT 'Cached advisory response text returned to the caller.',
  provider VARCHAR(50) NOT NULL COMMENT 'Provider that produced the original cached response, such as ollama, openai, anthropic, fallback, or policy.',
  model VARCHAR(100) NOT NULL COMMENT 'Model name associated with the cached response.',
  response_status VARCHAR(50) NOT NULL COMMENT 'Original response path for the cached entry, such as provider_success or fallback.',
  human_review_required TINYINT(1) NOT NULL DEFAULT 0 COMMENT 'Marks cached responses that still require human review.',
  is_advisory_label_shown TINYINT(1) NOT NULL DEFAULT 1 COMMENT 'Tracks whether the advisory label should be shown with the cached answer.',
  tokens_used INT NOT NULL DEFAULT 0 COMMENT 'Tokens reported or estimated for the original response.',
  prompt_tokens INT NOT NULL DEFAULT 0 COMMENT 'Prompt tokens from the original response when available.',
  completion_tokens INT NOT NULL DEFAULT 0 COMMENT 'Completion tokens from the original response when available.',
  response_time_ms INT NOT NULL DEFAULT 0 COMMENT 'Latency of the original uncached request in milliseconds.',
  hit_count INT NOT NULL DEFAULT 0 COMMENT 'Number of cache hits served from this row.',
  created_at_utc DATETIME(6) NOT NULL COMMENT 'Time the cached response was created.',
  last_hit_at_utc DATETIME(6) NOT NULL COMMENT 'Time the cached response was last served from cache.',
  expires_at_utc DATETIME(6) NOT NULL COMMENT 'When this cached response should be treated as stale and re-computed.',
  PRIMARY KEY (id),
  UNIQUE KEY ux_ai_hr_query_cache_tenant_key (tenant_id, cache_key),
  KEY ix_ai_hr_query_cache_tenant_expires (tenant_id, expires_at_utc),
  KEY ix_ai_hr_query_cache_tenant_intent_module (tenant_id, intent_classified, module)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
