-- Stage 97: catalog presence must never be persisted as provider connectivity.
-- Preserve tenant-entered configuration, but clear unsupported operational claims
-- for built-in entries that have never completed a real provider handshake.

UPDATE integrations
SET status = CASE
        WHEN COALESCE(config_json, '{}'::jsonb) = '{}'::jsonb THEN 'Disconnected'
        ELSE 'Pending'
    END,
    sync_label = 'Never',
    last_sync_at = NULL,
    sync_last_attempt_at = NULL,
    sync_last_completed_at = NULL,
    sync_last_ok = NULL,
    provider_last_event_at = NULL,
    updated_at = NOW()
WHERE COALESCE(is_custom, false) = false
  AND last_tested_at IS NULL
  AND integration_key IN (
      'sap-s4hana', 'oracle-netsuite', 'microsoft-dynamics',
      'quickbooks-online', 'xero', 'sage-intacct', 'samsara', 'geotab',
      'verizon-connect', 'motive', 'platform-science', 'wex-fuel-card',
      'fleetcor', 'comdata', 'shell-fleet', 'google-maps-platform',
      'here-maps', 'mapbox', 'ptv-route-optimiser', 'whatsapp-business',
      'twilio-sms', 'slack', 'microsoft-teams', 'sendgrid-email',
      'sap-extended-wms', 'manhattan-wms', 'aws-iot-core', 'azure-iot-hub',
      'trimble-tmt', 'fmcsa-portal', 'ifta-reporting'
  );
