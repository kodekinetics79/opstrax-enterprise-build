DO $$
DECLARE
  v_company_id bigint;
  v_customer1_id bigint;
  v_customer2_id bigint;
  v_customer1_address_id bigint;
  v_customer2_address_id bigint;
  v_job1_id bigint;
  v_job2_id bigint;
  v_trip1_id bigint;
  v_trip2_id bigint;
  v_vehicle1_id bigint;
  v_vehicle2_id bigint;
  v_driver1_id bigint;
  v_driver2_id bigint;
  v_shipment1_id bigint;
  v_shipment2_id bigint;
  v_shipment1_number text;
  v_shipment2_number text;
  v_stop1_id bigint;
  v_stop2_id bigint;
  v_asset_id bigint;
  v_zone1_id bigint;
  v_zone2_id bigint;
  v_device1_id bigint;
  v_device2_id bigint;
  v_reading1_id bigint;
  v_reading2_id bigint;
  v_alert1_id bigint;
  v_alert2_id bigint;
  v_site_access1_id bigint;
  v_site_access2_id bigint;
  v_access_doc1_id bigint;
  v_access_doc2_id bigint;
  v_pickup_auth1_id bigint;
  v_pickup_auth2_id bigint;
  v_handover1_id bigint;
  v_handover2_id bigint;
  v_proof_pkg1_id bigint;
  v_proof_pkg2_id bigint;
  v_artifact1_id bigint;
  v_pod_id bigint;
  v_billing_ready_id bigint;
  v_billing_blocked_id bigint;
  v_approval_req_id bigint;
  v_approval_decision_id bigint;
  v_ai_run_id bigint;
  v_ai_reco_id bigint;
  v_ai_action_id bigint;
  v_domain_event_id bigint;
  v_outbox_id bigint;
  v_inbox_id bigint;
  v_event_log_id bigint;
  v_reco_ready_id bigint;
  v_reco_blocked_id bigint;
  v_assignment_ready_id bigint;
  v_assignment_blocked_id bigint;
BEGIN
  SELECT id INTO v_company_id FROM companies ORDER BY id LIMIT 1;
  SELECT id INTO v_customer1_id FROM customers WHERE company_id = v_company_id ORDER BY id LIMIT 1;
  SELECT id INTO v_customer2_id FROM customers WHERE company_id = v_company_id ORDER BY id OFFSET 1 LIMIT 1;
  SELECT id INTO v_job1_id FROM jobs WHERE company_id = v_company_id ORDER BY id LIMIT 1;
  SELECT id INTO v_job2_id FROM jobs WHERE company_id = v_company_id ORDER BY id OFFSET 1 LIMIT 1;
  SELECT id INTO v_trip1_id FROM trips WHERE company_id = v_company_id ORDER BY id LIMIT 1;
  SELECT id INTO v_trip2_id FROM trips WHERE company_id = v_company_id ORDER BY id OFFSET 1 LIMIT 1;
  SELECT vehicle_id, driver_id INTO v_vehicle1_id, v_driver1_id FROM trips WHERE id = v_trip1_id;
  SELECT vehicle_id, driver_id INTO v_vehicle2_id, v_driver2_id FROM trips WHERE id = v_trip2_id;
  SELECT id INTO v_shipment1_id FROM fleet_tms_shipments WHERE company_id = v_company_id ORDER BY id LIMIT 1;
  SELECT id INTO v_shipment2_id FROM fleet_tms_shipments WHERE company_id = v_company_id ORDER BY id OFFSET 1 LIMIT 1;
  SELECT shipment_number INTO v_shipment1_number FROM fleet_tms_shipments WHERE id = v_shipment1_id;
  SELECT shipment_number INTO v_shipment2_number FROM fleet_tms_shipments WHERE id = v_shipment2_id;
  SELECT id INTO v_stop1_id FROM trip_stops WHERE company_id = v_company_id AND trip_id = v_trip1_id ORDER BY stop_sequence LIMIT 1;
  SELECT id INTO v_stop2_id FROM trip_stops WHERE company_id = v_company_id AND trip_id = v_trip2_id ORDER BY stop_sequence LIMIT 1;
  SELECT id INTO v_asset_id FROM fleet_tms_assets WHERE company_id = v_company_id ORDER BY id LIMIT 1;

  IF NOT EXISTS (SELECT 1 FROM customer_addresses WHERE company_id = v_company_id AND customer_id = v_customer1_id AND address_type = 'Billing') THEN
    INSERT INTO customer_addresses (company_id, customer_id, address_type, address_line, city, state, postal_code, created_at)
    VALUES (v_company_id, v_customer1_id, 'Billing', '100 Riyadh Logistics Park', 'Riyadh', 'Riyadh', '11564', NOW())
    RETURNING id INTO v_customer1_address_id;
  END IF;

  IF NOT EXISTS (SELECT 1 FROM customer_addresses WHERE company_id = v_company_id AND customer_id = v_customer2_id AND address_type = 'Billing') THEN
    INSERT INTO customer_addresses (company_id, customer_id, address_type, address_line, city, state, postal_code, created_at)
    VALUES (v_company_id, v_customer2_id, 'Billing', '88 Jeddah Industrial Zone', 'Jeddah', 'Makkah', '21441', NOW())
    RETURNING id INTO v_customer2_address_id;
  END IF;

  IF NOT EXISTS (SELECT 1 FROM customer_sites WHERE company_id = v_company_id AND site_code = 'SITE-RIY-001') THEN
    INSERT INTO customer_sites (
      company_id, customer_id, site_code, site_name, site_type, address_line1, address_line2,
      city, state, postal_code, country_code, geo_latitude, geo_longitude, access_instructions,
      external_reference, status, source_channel, client_generated_id, idempotency_key,
      correlation_id, causation_id, metadata_json, created_at, updated_at
    )
    VALUES (
      v_company_id, v_customer1_id, 'SITE-RIY-001', 'Riyadh North DC', 'Distribution Center',
      'King Fahd Road', 'Gate 2', 'Riyadh', 'Riyadh', '11564', 'SA',
      24.7136, 46.6753, 'Check in at security gate 2 and call the receiving desk.',
      'seed-site-riy-001', 'active', 'seed', 'seed-site-riy-001', 'seed-site-riy-001',
      'seed-corr-site-001', 'seed-caus-site-001', '{"module":"customer_sites","scenario":"operational"}'::jsonb,
      NOW(), NOW()
    );
  END IF;

  IF NOT EXISTS (SELECT 1 FROM customer_sites WHERE company_id = v_company_id AND site_code = 'SITE-JED-001') THEN
    INSERT INTO customer_sites (
      company_id, customer_id, site_code, site_name, site_type, address_line1, address_line2,
      city, state, postal_code, country_code, geo_latitude, geo_longitude, access_instructions,
      external_reference, status, source_channel, client_generated_id, idempotency_key,
      correlation_id, causation_id, metadata_json, created_at, updated_at
    )
    VALUES (
      v_company_id, v_customer2_id, 'SITE-JED-001', 'Jeddah Pharma Bay', 'Customer Site',
      'Harbor Road', 'Dock 4', 'Jeddah', 'Makkah', '21441', 'SA',
      21.5433, 39.1728, 'Security badge required. Refrigerated bay check-in only.',
      'seed-site-jed-001', 'active', 'seed', 'seed-site-jed-001', 'seed-site-jed-001',
      'seed-corr-site-002', 'seed-caus-site-002', '{"module":"customer_sites","scenario":"cold_chain"}'::jsonb,
      NOW(), NOW()
    );
  END IF;

  IF NOT EXISTS (SELECT 1 FROM job_status_events WHERE company_id = v_company_id AND job_id = v_job1_id AND event_title = 'Job created') THEN
    INSERT INTO job_status_events (company_id, job_id, previous_status, new_status, event_title, event_description, occurred_at, created_by_user_id, created_at)
    VALUES
      (v_company_id, v_job1_id, NULL, 'Queued', 'Job created', 'Seeded test job created for local module verification.', NOW() - INTERVAL '3 days', NULL, NOW()),
      (v_company_id, v_job1_id, 'Queued', 'Assigned', 'Job assigned', 'Assigned to the live test vehicle and driver.', NOW() - INTERVAL '2 days', NULL, NOW()),
      (v_company_id, v_job1_id, 'Assigned', 'Completed', 'Job completed', 'Completed test workflow with proof and billing confidence.', NOW() - INTERVAL '1 day', NULL, NOW());
  END IF;

  IF NOT EXISTS (SELECT 1 FROM job_status_events WHERE company_id = v_company_id AND job_id = v_job2_id AND event_title = 'Job queued for execution') THEN
    INSERT INTO job_status_events (company_id, job_id, previous_status, new_status, event_title, event_description, occurred_at, created_by_user_id, created_at)
    VALUES
      (v_company_id, v_job2_id, NULL, 'Queued', 'Job queued for execution', 'Seeded job waiting on access and proof artifacts.', NOW() - INTERVAL '12 hours', NULL, NOW()),
      (v_company_id, v_job2_id, 'Queued', 'Assigned', 'Job assigned for dispatch', 'Assigned in local test data to show dispatch decision flow.', NOW() - INTERVAL '6 hours', NULL, NOW());
  END IF;

  IF NOT EXISTS (SELECT 1 FROM fleet_tms_temperature_zones WHERE company_id = v_company_id AND code = 'CHILL') THEN
    INSERT INTO fleet_tms_temperature_zones (company_id, code, name, min_celsius, max_celsius, color, is_active, notes, created_at_utc, updated_at_utc)
    VALUES (v_company_id, 'CHILL', 'Chilled (2-8C)', 2, 8, '#22d3ee', true, 'Seeded cold-chain zone for local verification.', NOW(), NOW())
    RETURNING id INTO v_zone1_id;
  ELSE
    SELECT id INTO v_zone1_id FROM fleet_tms_temperature_zones WHERE company_id = v_company_id AND code = 'CHILL' ORDER BY id LIMIT 1;
  END IF;

  IF NOT EXISTS (SELECT 1 FROM fleet_tms_temperature_zones WHERE company_id = v_company_id AND code = 'FROZEN') THEN
    INSERT INTO fleet_tms_temperature_zones (company_id, code, name, min_celsius, max_celsius, color, is_active, notes, created_at_utc, updated_at_utc)
    VALUES (v_company_id, 'FROZEN', 'Frozen (-18C)', -22, -16, '#3b82f6', true, 'Seeded frozen zone for local verification.', NOW(), NOW())
    RETURNING id INTO v_zone2_id;
  ELSE
    SELECT id INTO v_zone2_id FROM fleet_tms_temperature_zones WHERE company_id = v_company_id AND code = 'FROZEN' ORDER BY id LIMIT 1;
  END IF;

  IF NOT EXISTS (SELECT 1 FROM fleet_tms_temperature_devices WHERE company_id = v_company_id AND device_code = 'TMP-SEED-001') THEN
    INSERT INTO fleet_tms_temperature_devices (
      company_id, device_code, name, zone_id, shipment_id, vehicle_number, status,
      last_reported_temperature_celsius, battery_percent, last_ping_at_utc, notes,
      created_at_utc, updated_at_utc, source_channel, client_generated_id, idempotency_key,
      correlation_id, causation_id, metadata_json
    )
    VALUES (
      v_company_id, 'TMP-SEED-001', 'Seed Reefer Sensor 1', v_zone1_id, v_shipment1_id, 'REF-412', 'Active',
      4.2, 92, NOW() - INTERVAL '10 minutes', 'Healthy local cold-chain probe.',
      NOW(), NOW(), 'seed', 'seed-temp-001', 'seed-temp-001', 'seed-corr-temp-001', 'seed-caus-temp-001',
      '{"module":"cold_chain","scenario":"healthy"}'::jsonb
    )
    RETURNING id INTO v_device1_id;
  ELSE
    SELECT id INTO v_device1_id FROM fleet_tms_temperature_devices WHERE company_id = v_company_id AND device_code = 'TMP-SEED-001' LIMIT 1;
  END IF;

  IF NOT EXISTS (SELECT 1 FROM fleet_tms_temperature_devices WHERE company_id = v_company_id AND device_code = 'TMP-SEED-002') THEN
    INSERT INTO fleet_tms_temperature_devices (
      company_id, device_code, name, zone_id, shipment_id, vehicle_number, status,
      last_reported_temperature_celsius, battery_percent, last_ping_at_utc, notes,
      created_at_utc, updated_at_utc, source_channel, client_generated_id, idempotency_key,
      correlation_id, causation_id, metadata_json
    )
    VALUES (
      v_company_id, 'TMP-SEED-002', 'Seed Reefer Sensor 2', v_zone2_id, v_shipment2_id, 'REF-413', 'Active',
      10.8, 66, NOW() - INTERVAL '7 minutes', 'Intentionally above chill threshold to show alerts.',
      NOW(), NOW(), 'seed', 'seed-temp-002', 'seed-temp-002', 'seed-corr-temp-002', 'seed-caus-temp-002',
      '{"module":"cold_chain","scenario":"breach"}'::jsonb
    )
    RETURNING id INTO v_device2_id;
  ELSE
    SELECT id INTO v_device2_id FROM fleet_tms_temperature_devices WHERE company_id = v_company_id AND device_code = 'TMP-SEED-002' LIMIT 1;
  END IF;

  IF NOT EXISTS (SELECT 1 FROM fleet_tms_temperature_readings WHERE company_id = v_company_id AND device_id = v_device1_id AND source = 'Seed') THEN
    INSERT INTO fleet_tms_temperature_readings (
      company_id, device_id, shipment_id, zone_id, temperature_celsius, humidity_percent, latitude, longitude,
      source, status, notes, recorded_at_utc, created_at_utc, source_channel, client_generated_id,
      idempotency_key, correlation_id, causation_id, metadata_json, applied_policy_code, applied_policy_scope,
      applied_min_celsius, applied_max_celsius
    )
    VALUES (
      v_company_id, v_device1_id, v_shipment1_id, v_zone1_id, 4.1, 52, 24.7136, 46.6753,
      'Seed', 'Normal', 'Seeded healthy cold-chain reading.', NOW() - INTERVAL '8 minutes', NOW(),
      'seed', 'seed-reading-001', 'seed-reading-001', 'seed-corr-reading-001', 'seed-caus-reading-001',
      '{"module":"cold_chain","scenario":"healthy"}'::jsonb, 'CHILL', 'shipment', 2, 8
    )
    RETURNING id INTO v_reading1_id;
  ELSE
    SELECT id INTO v_reading1_id FROM fleet_tms_temperature_readings WHERE company_id = v_company_id AND device_id = v_device1_id AND source = 'Seed' ORDER BY id LIMIT 1;
  END IF;

  IF NOT EXISTS (SELECT 1 FROM fleet_tms_temperature_readings WHERE company_id = v_company_id AND device_id = v_device2_id AND source = 'Seed') THEN
    INSERT INTO fleet_tms_temperature_readings (
      company_id, device_id, shipment_id, zone_id, temperature_celsius, humidity_percent, latitude, longitude,
      source, status, notes, recorded_at_utc, created_at_utc, source_channel, client_generated_id,
      idempotency_key, correlation_id, causation_id, metadata_json, applied_policy_code, applied_policy_scope,
      applied_min_celsius, applied_max_celsius
    )
    VALUES (
      v_company_id, v_device2_id, v_shipment2_id, v_zone2_id, 10.8, 58, 21.5433, 39.1728,
      'Seed', 'Breach', 'Seeded out-of-range cold-chain reading for alert coverage.', NOW() - INTERVAL '5 minutes', NOW(),
      'seed', 'seed-reading-002', 'seed-reading-002', 'seed-corr-reading-002', 'seed-caus-reading-002',
      '{"module":"cold_chain","scenario":"breach"}'::jsonb, 'FROZEN', 'shipment', -22, -16
    )
    RETURNING id INTO v_reading2_id;
  ELSE
    SELECT id INTO v_reading2_id FROM fleet_tms_temperature_readings WHERE company_id = v_company_id AND device_id = v_device2_id AND source = 'Seed' ORDER BY id LIMIT 1;
  END IF;

  IF NOT EXISTS (SELECT 1 FROM fleet_tms_temperature_alerts WHERE company_id = v_company_id AND reading_id = v_reading2_id) THEN
    INSERT INTO fleet_tms_temperature_alerts (
      company_id, device_id, shipment_id, reading_id, alert_type, severity, status,
      threshold_min, threshold_max, measured_temperature, triggered_at_utc, resolved_at_utc,
      resolved_by, resolution_notes, notes, source_channel, client_generated_id, idempotency_key,
      correlation_id, causation_id, metadata_json, applied_policy_code, applied_policy_scope,
      acknowledged_at_utc, acknowledged_by, acknowledged_notes
    )
    VALUES (
      v_company_id, v_device2_id, v_shipment2_id, v_reading2_id, 'TemperatureBreach', 'High', 'Open',
      -22, -16, 10.8, NOW() - INTERVAL '4 minutes', NULL,
      'ops.bot', 'Awaiting ops review.', 'Seeded breach alert for cold-chain workspace.', 'seed', 'seed-alert-001', 'seed-alert-001',
      'seed-corr-alert-001', 'seed-caus-alert-001', '{"module":"cold_chain","scenario":"breach"}'::jsonb,
      'FROZEN', 'shipment', NOW() - INTERVAL '3 minutes', 'ops.bot', 'Seeded acknowledgement'
    )
    RETURNING id INTO v_alert1_id;
  ELSE
    SELECT id INTO v_alert1_id FROM fleet_tms_temperature_alerts WHERE company_id = v_company_id AND reading_id = v_reading2_id LIMIT 1;
  END IF;

  IF NOT EXISTS (SELECT 1 FROM fleet_tms_cold_chain_policies WHERE company_id = v_company_id AND policy_code = 'POL-CHILL-001') THEN
    INSERT INTO fleet_tms_cold_chain_policies (
      company_id, policy_code, scope_type, scope_key, min_celsius, max_celsius, humidity_min_percent,
      humidity_max_percent, severity, requires_acknowledgement, status, source_channel, client_generated_id,
      idempotency_key, correlation_id, causation_id, metadata_json, notes, created_at_utc, updated_at_utc
    )
    VALUES (
      v_company_id, 'POL-CHILL-001', 'shipment', v_shipment1_number, 2, 8, 40, 70, 'High', true, 'active',
      'seed', 'seed-policy-001', 'seed-policy-001', 'seed-corr-policy-001', 'seed-caus-policy-001',
      '{"module":"cold_chain","scenario":"policy"}'::jsonb, 'Seed policy for chilled shipments.', NOW(), NOW()
    );
  END IF;

  IF NOT EXISTS (SELECT 1 FROM fleet_tms_cold_chain_policies WHERE company_id = v_company_id AND policy_code = 'POL-FROZEN-001') THEN
    INSERT INTO fleet_tms_cold_chain_policies (
      company_id, policy_code, scope_type, scope_key, min_celsius, max_celsius, humidity_min_percent,
      humidity_max_percent, severity, requires_acknowledgement, status, source_channel, client_generated_id,
      idempotency_key, correlation_id, causation_id, metadata_json, notes, created_at_utc, updated_at_utc
    )
    VALUES (
      v_company_id, 'POL-FROZEN-001', 'shipment', v_shipment2_number, -22, -16, 35, 60, 'Critical', true, 'active',
      'seed', 'seed-policy-002', 'seed-policy-002', 'seed-corr-policy-002', 'seed-caus-policy-002',
      '{"module":"cold_chain","scenario":"policy"}'::jsonb, 'Seed policy for frozen shipments.', NOW(), NOW()
    );
  END IF;

  IF NOT EXISTS (SELECT 1 FROM fleet_tms_cold_chain_event_log WHERE company_id = v_company_id AND aggregate_id = 'seed-cold-chain-001' AND event_type = 'temperature.reading.seeded') THEN
    INSERT INTO fleet_tms_cold_chain_event_log (
      company_id, event_type, aggregate_type, aggregate_id, payload_json, correlation_id, causation_id, idempotency_key,
      status, retry_count, error_message, occurred_at_utc, processed_at_utc, created_at_utc
    )
    VALUES (
      v_company_id, 'temperature.reading.seeded', 'temperature_device', 'seed-cold-chain-001',
      '{"deviceCode":"TMP-SEED-002","scenario":"breach"}'::jsonb, 'seed-corr-cold-001', 'seed-caus-cold-001', 'seed-cold-001',
      'processed', 0, NULL, NOW() - INTERVAL '4 minutes', NOW() - INTERVAL '3 minutes', NOW() - INTERVAL '4 minutes'
    );
  END IF;

  IF NOT EXISTS (SELECT 1 FROM fleet_tms_cold_chain_reports WHERE company_id = v_company_id AND shipment_id = v_shipment1_id) THEN
    INSERT INTO fleet_tms_cold_chain_reports (
      company_id, shipment_id, shipment_number, generated_at_utc, compliance_percent, min_temperature_celsius,
      max_temperature_celsius, total_readings, breach_count, summary_json, notes, source_channel,
      client_generated_id, idempotency_key, correlation_id, causation_id, metadata_json, report_status
    )
    VALUES (
      v_company_id, v_shipment1_id, v_shipment1_number, NOW() - INTERVAL '2 minutes', 100, 2, 8, 4, 0,
      '{"status":"passed","message":"Seeded compliant shipment report"}'::jsonb, 'Seeded cold-chain report.', 'seed',
      'seed-cold-report-001', 'seed-cold-report-001', 'seed-corr-cold-report-001', 'seed-caus-cold-report-001',
      '{"module":"cold_chain","scenario":"healthy"}'::jsonb, 'ready'
    );
  END IF;

  IF NOT EXISTS (SELECT 1 FROM fleet_tms_readiness_documents WHERE company_id = v_company_id AND document_number = 'CR-LOCAL-001') THEN
    INSERT INTO fleet_tms_readiness_documents (
      company_id, kind, subject_type, subject_id, subject_name, document_type, document_number, country_code,
      document_status, expiry_status, issue_date, gregorian_expiry_date, notes, created_at_utc, updated_at_utc
    )
    VALUES
      (v_company_id, 'Compliance', 'Company', 'COMP-LOCAL', 'OpsTrax Local Demo', 'Commercial Registration', 'CR-LOCAL-001', 'SA', 'Active', 'Healthy', CURRENT_DATE - 300, CURRENT_DATE + 220, 'Seeded company registration.', NOW(), NOW()),
      (v_company_id, 'Transport', 'Vehicle', 'REF-412', 'REF-412', 'Transport Operating Card', 'TOC-LOCAL-001', 'SA', 'Active', 'ExpiringSoon', CURRENT_DATE - 90, CURRENT_DATE + 12, 'Seeded transport card.', NOW(), NOW());
  END IF;

  IF NOT EXISTS (SELECT 1 FROM fleet_tms_pods WHERE company_id = v_company_id AND shipment_id = v_shipment1_id) THEN
    INSERT INTO fleet_tms_pods (
      company_id, shipment_id, stop_id, captured_by_user_id, driver_id, vehicle_id, recipient_name, recipient_phone,
      signature_url, photo_url, document_url, notes, delivery_condition, captured_latitude, captured_longitude,
      captured_at, verified_at, verified_by_user_id, status, created_at, updated_at
    )
    VALUES (
      v_company_id, v_shipment1_id, v_stop1_id, NULL, v_driver1_id, v_vehicle1_id, 'Receiving Desk', '+966500000001',
      'https://seed.local/signatures/pod-001.png', 'https://seed.local/photos/pod-001.png', 'https://seed.local/docs/pod-001.pdf',
      'Seeded completed POD for local verification.', 'Good', 24.7136, 46.6753,
      NOW() - INTERVAL '30 minutes', NOW() - INTERVAL '20 minutes', NULL, 'Verified', NOW() - INTERVAL '30 minutes', NOW() - INTERVAL '20 minutes'
    )
    RETURNING id INTO v_pod_id;
  ELSE
    SELECT id INTO v_pod_id FROM fleet_tms_pods WHERE company_id = v_company_id AND shipment_id = v_shipment1_id LIMIT 1;
  END IF;

  IF NOT EXISTS (SELECT 1 FROM fleet_tms_asset_events WHERE company_id = v_company_id AND asset_id = v_asset_id AND event_type = 'check_out') THEN
    INSERT INTO fleet_tms_asset_events (company_id, asset_id, event_type, quantity, location, actor_name, occurred_at_utc, notes)
    VALUES (v_company_id, v_asset_id, 'check_out', 4, 'Riyadh DC', 'seed.bot', NOW() - INTERVAL '1 day', 'Seeded asset check-out event.');
  END IF;

  IF NOT EXISTS (SELECT 1 FROM smart_assignment_recommendations WHERE company_id = v_company_id AND job_id = v_job1_id) THEN
    INSERT INTO smart_assignment_recommendations (
      company_id, job_id, trip_id, recommended_driver_id, recommended_vehicle_id, recommended_crew_id,
      recommendation_type, score, risk_level, confidence_score, reason_json, constraint_json,
      proposed_action_json, status, source_channel, client_generated_id, idempotency_key,
      created_by, correlation_id, causation_id, created_at
    )
    VALUES (
      v_company_id, v_job1_id, v_trip1_id, v_driver1_id, v_vehicle1_id, NULL, 'dispatch.smart_assignment', 0.94,
      'low', 0.96, '{"why":["existing assignment matches readiness","proof chain is complete"]}'::jsonb,
      '{"constraints":["none"]}'::jsonb, '{"action":"accept"}'::jsonb, 'draft', 'seed', 'seed-smart-ready', 'seed-smart-ready',
      NULL, 'seed-corr-smart-ready', 'seed-caus-smart-ready', NOW()
    )
    RETURNING id INTO v_assignment_ready_id;
  ELSE
    SELECT id INTO v_assignment_ready_id FROM smart_assignment_recommendations WHERE company_id = v_company_id AND job_id = v_job1_id ORDER BY id LIMIT 1;
  END IF;

  IF NOT EXISTS (SELECT 1 FROM smart_assignment_recommendations WHERE company_id = v_company_id AND job_id = v_job2_id) THEN
    INSERT INTO smart_assignment_recommendations (
      company_id, job_id, trip_id, recommended_driver_id, recommended_vehicle_id, recommended_crew_id,
      recommendation_type, score, risk_level, confidence_score, reason_json, constraint_json,
      proposed_action_json, status, source_channel, client_generated_id, idempotency_key,
      created_by, correlation_id, causation_id, created_at
    )
    VALUES (
      v_company_id, v_job2_id, v_trip2_id, v_driver2_id, v_vehicle2_id, NULL, 'dispatch.smart_assignment', 0.57,
      'high', 0.63, '{"why":["access documents missing","handover still open"]}'::jsonb,
      '{"constraints":["site access pending","proof package incomplete"]}'::jsonb, '{"action":"request_approval"}'::jsonb, 'draft', 'seed', 'seed-smart-blocked', 'seed-smart-blocked',
      NULL, 'seed-corr-smart-blocked', 'seed-caus-smart-blocked', NOW()
    )
    RETURNING id INTO v_assignment_blocked_id;
  ELSE
    SELECT id INTO v_assignment_blocked_id FROM smart_assignment_recommendations WHERE company_id = v_company_id AND job_id = v_job2_id ORDER BY id LIMIT 1;
  END IF;

  IF NOT EXISTS (SELECT 1 FROM assignment_confirmations WHERE company_id = v_company_id AND job_id = v_job1_id AND status = 'accepted') THEN
    INSERT INTO assignment_confirmations (
      company_id, job_id, trip_id, driver_id, vehicle_id, status, accepted_at, source_channel, client_generated_id,
      idempotency_key, device_id, mobile_app_version, metadata_json, correlation_id, causation_id, created_at, updated_at
    )
    VALUES (
      v_company_id, v_job1_id, v_trip1_id, v_driver1_id, v_vehicle1_id, 'accepted', NOW() - INTERVAL '1 day',
      'seed', 'seed-assign-ready', 'seed-assign-ready', 'seed-device', 'web-seed', '{"scenario":"ready"}'::jsonb,
      'seed-corr-assign-ready', 'seed-caus-assign-ready', NOW(), NOW()
    );
  END IF;

  IF NOT EXISTS (SELECT 1 FROM assignment_confirmations WHERE company_id = v_company_id AND job_id = v_job2_id AND status = 'rejected') THEN
    INSERT INTO assignment_confirmations (
      company_id, job_id, trip_id, driver_id, vehicle_id, status, rejected_at, rejection_reason, source_channel,
      client_generated_id, idempotency_key, device_id, mobile_app_version, metadata_json, correlation_id,
      causation_id, created_at, updated_at
    )
    VALUES (
      v_company_id, v_job2_id, v_trip2_id, v_driver2_id, v_vehicle2_id, 'rejected', NOW() - INTERVAL '8 hours',
      'Missing access documents.', 'seed', 'seed-assign-blocked', 'seed-assign-blocked', 'seed-device', 'web-seed',
      '{"scenario":"blocked"}'::jsonb, 'seed-corr-assign-blocked', 'seed-caus-assign-blocked', NOW(), NOW()
    );
  END IF;

  IF NOT EXISTS (SELECT 1 FROM site_access_requirements WHERE company_id = v_company_id AND job_id = v_job1_id AND requirement_type = 'gate_pass' AND status = 'verified') THEN
    INSERT INTO site_access_requirements (
      company_id, customer_id, address_id, job_id, trip_id, requirement_type, status, required_before,
      instructions, contact_name, contact_phone, source_channel, metadata_json, correlation_id, causation_id,
      created_at, updated_at
    )
    VALUES (
      v_company_id, v_customer1_id, NULL, v_job1_id, v_trip1_id, 'gate_pass', 'verified', NOW() + INTERVAL '2 hours',
      'Verified gate pass on file.', 'Receiving Desk', '+966500000001', 'seed',
      '{"scenario":"ready"}'::jsonb, 'seed-corr-site-access-ready', 'seed-caus-site-access-ready', NOW(), NOW()
    )
    RETURNING id INTO v_site_access1_id;
  ELSE
    SELECT id INTO v_site_access1_id FROM site_access_requirements WHERE company_id = v_company_id AND job_id = v_job1_id ORDER BY id LIMIT 1;
  END IF;

  IF NOT EXISTS (SELECT 1 FROM site_access_requirements WHERE company_id = v_company_id AND job_id = v_job2_id AND requirement_type = 'gate_pass' AND status = 'required') THEN
    INSERT INTO site_access_requirements (
      company_id, customer_id, address_id, job_id, trip_id, requirement_type, status, required_before,
      instructions, contact_name, contact_phone, source_channel, metadata_json, correlation_id, causation_id,
      created_at, updated_at
    )
    VALUES (
      v_company_id, v_customer2_id, NULL, v_job2_id, v_trip2_id, 'gate_pass', 'required', NOW() + INTERVAL '4 hours',
      'Awaiting site authorization before completion.', 'Warehouse Desk', '+966500000002', 'seed',
      '{"scenario":"blocked"}'::jsonb, 'seed-corr-site-access-blocked', 'seed-caus-site-access-blocked', NOW(), NOW()
    )
    RETURNING id INTO v_site_access2_id;
  ELSE
    SELECT id INTO v_site_access2_id FROM site_access_requirements WHERE company_id = v_company_id AND job_id = v_job2_id ORDER BY id LIMIT 1;
  END IF;

  IF NOT EXISTS (SELECT 1 FROM access_documents WHERE company_id = v_company_id AND job_id = v_job1_id AND document_no = 'GP-LOCAL-001') THEN
    INSERT INTO access_documents (
      company_id, job_id, trip_id, site_access_requirement_id, document_type, document_no, status, issued_by, issued_to,
      valid_from, valid_to, file_id, notes, source_channel, captured_at, uploaded_at, captured_by_user_id, device_id,
      mobile_app_version, geo_latitude, geo_longitude, metadata_json, correlation_id, causation_id, idempotency_key, created_at
    )
    VALUES (
      v_company_id, v_job1_id, v_trip1_id, v_site_access1_id, 'gate_pass', 'GP-LOCAL-001', 'verified', 'Riyadh North DC', 'OpsTrax Driver',
      NOW() - INTERVAL '1 hour', NOW() + INTERVAL '1 day', NULL, 'Verified local gate pass.', 'seed', NOW() - INTERVAL '45 minutes',
      NOW() - INTERVAL '40 minutes', NULL, 'seed-device', 'web-seed', 24.7136, 46.6753, '{"scenario":"ready"}'::jsonb,
      'seed-corr-access-doc-ready', 'seed-caus-access-doc-ready', 'seed-access-doc-ready', NOW()
    )
    RETURNING id INTO v_access_doc1_id;
  ELSE
    SELECT id INTO v_access_doc1_id FROM access_documents WHERE company_id = v_company_id AND job_id = v_job1_id AND document_no = 'GP-LOCAL-001' LIMIT 1;
  END IF;

  IF NOT EXISTS (SELECT 1 FROM access_documents WHERE company_id = v_company_id AND job_id = v_job2_id AND document_no = 'GP-LOCAL-002') THEN
    INSERT INTO access_documents (
      company_id, job_id, trip_id, site_access_requirement_id, document_type, document_no, status, issued_by, issued_to,
      valid_from, valid_to, file_id, notes, source_channel, captured_at, uploaded_at, captured_by_user_id, device_id,
      mobile_app_version, geo_latitude, geo_longitude, metadata_json, correlation_id, causation_id, idempotency_key, created_at
    )
    VALUES (
      v_company_id, v_job2_id, v_trip2_id, v_site_access2_id, 'gate_pass', 'GP-LOCAL-002', 'required', 'Jeddah Pharma Bay', 'OpsTrax Driver',
      NOW() - INTERVAL '1 hour', NOW() + INTERVAL '1 day', NULL, 'Pending gate pass.', 'seed', NOW() - INTERVAL '35 minutes',
      NOW() - INTERVAL '30 minutes', NULL, 'seed-device', 'web-seed', 21.5433, 39.1728, '{"scenario":"blocked"}'::jsonb,
      'seed-corr-access-doc-blocked', 'seed-caus-access-doc-blocked', 'seed-access-doc-blocked', NOW()
    )
    RETURNING id INTO v_access_doc2_id;
  ELSE
    SELECT id INTO v_access_doc2_id FROM access_documents WHERE company_id = v_company_id AND job_id = v_job2_id AND document_no = 'GP-LOCAL-002' LIMIT 1;
  END IF;

  IF NOT EXISTS (SELECT 1 FROM pickup_authorizations WHERE company_id = v_company_id AND job_id = v_job1_id AND authorization_no = 'PA-LOCAL-001') THEN
    INSERT INTO pickup_authorizations (
      company_id, job_id, trip_id, warehouse_id, third_party_name, authorization_no, authorized_person_name,
      authorized_person_phone, status, valid_from, valid_to, notes, source_channel, captured_at, uploaded_at,
      captured_by_user_id, device_id, mobile_app_version, metadata_json, correlation_id, causation_id, idempotency_key, created_at
    )
    VALUES (
      v_company_id, v_job1_id, v_trip1_id, NULL, 'Seed 3PL', 'PA-LOCAL-001', 'Noura Al-Faisal', '+966500000101', 'verified',
      NOW() - INTERVAL '2 hours', NOW() + INTERVAL '8 hours', 'Verified pickup authorization.', 'seed',
      NOW() - INTERVAL '90 minutes', NOW() - INTERVAL '80 minutes', NULL, 'seed-device', 'web-seed',
      '{"scenario":"ready"}'::jsonb, 'seed-corr-pickup-ready', 'seed-caus-pickup-ready', 'seed-pickup-ready', NOW()
    )
    RETURNING id INTO v_pickup_auth1_id;
  ELSE
    SELECT id INTO v_pickup_auth1_id FROM pickup_authorizations WHERE company_id = v_company_id AND job_id = v_job1_id AND authorization_no = 'PA-LOCAL-001' LIMIT 1;
  END IF;

  IF NOT EXISTS (SELECT 1 FROM pickup_authorizations WHERE company_id = v_company_id AND job_id = v_job2_id AND authorization_no = 'PA-LOCAL-002') THEN
    INSERT INTO pickup_authorizations (
      company_id, job_id, trip_id, warehouse_id, third_party_name, authorization_no, authorized_person_name,
      authorized_person_phone, status, valid_from, valid_to, notes, source_channel, captured_at, uploaded_at,
      captured_by_user_id, device_id, mobile_app_version, metadata_json, correlation_id, causation_id, idempotency_key, created_at
    )
    VALUES (
      v_company_id, v_job2_id, v_trip2_id, NULL, 'Seed 3PL', 'PA-LOCAL-002', 'Hassan Bari', '+966500000102', 'required',
      NOW() - INTERVAL '2 hours', NOW() + INTERVAL '8 hours', 'Pending pickup authorization.', 'seed',
      NOW() - INTERVAL '85 minutes', NOW() - INTERVAL '70 minutes', NULL, 'seed-device', 'web-seed',
      '{"scenario":"blocked"}'::jsonb, 'seed-corr-pickup-blocked', 'seed-caus-pickup-blocked', 'seed-pickup-blocked', NOW()
    )
    RETURNING id INTO v_pickup_auth2_id;
  ELSE
    SELECT id INTO v_pickup_auth2_id FROM pickup_authorizations WHERE company_id = v_company_id AND job_id = v_job2_id AND authorization_no = 'PA-LOCAL-002' LIMIT 1;
  END IF;

  IF NOT EXISTS (SELECT 1 FROM warehouse_handovers WHERE company_id = v_company_id AND job_id = v_job1_id AND warehouse_reference_no = 'WH-LOCAL-001') THEN
    INSERT INTO warehouse_handovers (
      company_id, job_id, trip_id, warehouse_name, warehouse_reference_no, handover_type, status, scheduled_at,
      completed_at, handled_by_name, notes, source_channel, captured_at, uploaded_at, captured_by_user_id, device_id,
      mobile_app_version, geo_latitude, geo_longitude, metadata_json, correlation_id, causation_id, idempotency_key, created_at
    )
    VALUES (
      v_company_id, v_job1_id, v_trip1_id, 'Riyadh North Warehouse', 'WH-LOCAL-001', 'pickup', 'completed',
      NOW() - INTERVAL '3 hours', NOW() - INTERVAL '2 hours', 'Receiving Desk', 'Completed warehouse handover.', 'seed',
      NOW() - INTERVAL '2 hours 45 minutes', NOW() - INTERVAL '2 hours 30 minutes', NULL, 'seed-device', 'web-seed',
      24.7136, 46.6753, '{"scenario":"ready"}'::jsonb, 'seed-corr-handover-ready', 'seed-caus-handover-ready', 'seed-handover-ready', NOW()
    )
    RETURNING id INTO v_handover1_id;
  ELSE
    SELECT id INTO v_handover1_id FROM warehouse_handovers WHERE company_id = v_company_id AND job_id = v_job1_id AND warehouse_reference_no = 'WH-LOCAL-001' LIMIT 1;
  END IF;

  IF NOT EXISTS (SELECT 1 FROM warehouse_handovers WHERE company_id = v_company_id AND job_id = v_job2_id AND warehouse_reference_no = 'WH-LOCAL-002') THEN
    INSERT INTO warehouse_handovers (
      company_id, job_id, trip_id, warehouse_name, warehouse_reference_no, handover_type, status, scheduled_at,
      completed_at, handled_by_name, notes, source_channel, captured_at, uploaded_at, captured_by_user_id, device_id,
      mobile_app_version, geo_latitude, geo_longitude, metadata_json, correlation_id, causation_id, idempotency_key, created_at
    )
    VALUES (
      v_company_id, v_job2_id, v_trip2_id, 'Jeddah Pharma Bay', 'WH-LOCAL-002', 'pickup', 'scheduled',
      NOW() + INTERVAL '2 hours', NULL, 'Warehouse Lead', 'Pending handover to demonstrate incomplete state.', 'seed',
      NOW() - INTERVAL '75 minutes', NOW() - INTERVAL '60 minutes', NULL, 'seed-device', 'web-seed',
      21.5433, 39.1728, '{"scenario":"blocked"}'::jsonb, 'seed-corr-handover-blocked', 'seed-caus-handover-blocked', 'seed-handover-blocked', NOW()
    )
    RETURNING id INTO v_handover2_id;
  ELSE
    SELECT id INTO v_handover2_id FROM warehouse_handovers WHERE company_id = v_company_id AND job_id = v_job2_id AND warehouse_reference_no = 'WH-LOCAL-002' LIMIT 1;
  END IF;

  IF NOT EXISTS (SELECT 1 FROM proof_packages WHERE company_id = v_company_id AND job_id = v_job1_id AND client_generated_id = 'seed-proof-ready') THEN
    INSERT INTO proof_packages (
      company_id, job_id, trip_id, proof_type, status, completed_at, completed_by_user_id, receiver_name, receiver_phone,
      receiver_signature_file_id, geo_latitude, geo_longitude, notes, validation_status, validation_summary,
      source_channel, client_generated_id, idempotency_key, captured_at, uploaded_at, captured_by_user_id, device_id,
      mobile_app_version, metadata_json, correlation_id, causation_id, created_at
    )
    VALUES (
      v_company_id, v_job1_id, v_trip1_id, 'proof_of_delivery', 'validated', NOW() - INTERVAL '20 minutes', NULL,
      'Receiving Desk', '+966500000001', NULL, 24.7136, 46.6753, 'Validated proof package.', 'passed',
      'Proof passed with complete evidence.', 'seed', 'seed-proof-ready', 'seed-proof-ready',
      NOW() - INTERVAL '25 minutes', NOW() - INTERVAL '22 minutes', NULL, 'seed-device', 'web-seed',
      '{"scenario":"ready"}'::jsonb, 'seed-corr-proof-ready', 'seed-caus-proof-ready', NOW()
    )
    RETURNING id INTO v_proof_pkg1_id;
  ELSE
    SELECT id INTO v_proof_pkg1_id FROM proof_packages WHERE company_id = v_company_id AND job_id = v_job1_id AND client_generated_id = 'seed-proof-ready' LIMIT 1;
  END IF;

  IF NOT EXISTS (SELECT 1 FROM proof_packages WHERE company_id = v_company_id AND job_id = v_job2_id AND client_generated_id = 'seed-proof-blocked') THEN
    INSERT INTO proof_packages (
      company_id, job_id, trip_id, proof_type, status, completed_at, completed_by_user_id, receiver_name, receiver_phone,
      receiver_signature_file_id, geo_latitude, geo_longitude, notes, validation_status, validation_summary,
      source_channel, client_generated_id, idempotency_key, captured_at, uploaded_at, captured_by_user_id, device_id,
      mobile_app_version, metadata_json, correlation_id, causation_id, created_at
    )
    VALUES (
      v_company_id, v_job2_id, v_trip2_id, 'proof_of_delivery', 'submitted', NULL, NULL,
      NULL, NULL, NULL, NULL, NULL, 'Submitted but awaiting evidence to demonstrate the blocked path.', 'pending',
      'Missing artifact and exception note.', 'seed', 'seed-proof-blocked', 'seed-proof-blocked',
      NOW() - INTERVAL '45 minutes', NOW() - INTERVAL '40 minutes', NULL, 'seed-device', 'web-seed',
      '{"scenario":"blocked"}'::jsonb, 'seed-corr-proof-blocked', 'seed-caus-proof-blocked', NOW()
    )
    RETURNING id INTO v_proof_pkg2_id;
  ELSE
    SELECT id INTO v_proof_pkg2_id FROM proof_packages WHERE company_id = v_company_id AND job_id = v_job2_id AND client_generated_id = 'seed-proof-blocked' LIMIT 1;
  END IF;

  IF NOT EXISTS (SELECT 1 FROM proof_artifacts WHERE company_id = v_company_id AND proof_package_id = v_proof_pkg1_id AND idempotency_key = 'seed-proof-artifact-001') THEN
    INSERT INTO proof_artifacts (
      company_id, proof_package_id, artifact_type, file_id, captured_at, uploaded_at, captured_by_user_id,
      geo_latitude, geo_longitude, device_id, mobile_app_version, source_channel, notes, metadata_json,
      idempotency_key, correlation_id, causation_id, created_at
    )
    VALUES (
      v_company_id, v_proof_pkg1_id, 'photo', NULL, NOW() - INTERVAL '25 minutes', NOW() - INTERVAL '24 minutes', NULL,
      24.7136, 46.6753, 'seed-device', 'web-seed', 'seed', 'Receiver handoff photo.', '{"scenario":"ready"}'::jsonb,
      'seed-proof-artifact-001', 'seed-corr-proof-artifact-001', 'seed-caus-proof-artifact-001', NOW()
    )
    RETURNING id INTO v_artifact1_id;
  ELSE
    SELECT id INTO v_artifact1_id FROM proof_artifacts WHERE company_id = v_company_id AND proof_package_id = v_proof_pkg1_id AND idempotency_key = 'seed-proof-artifact-001' LIMIT 1;
  END IF;

  IF NOT EXISTS (SELECT 1 FROM billing_confidence_records WHERE company_id = v_company_id AND proof_package_id = v_proof_pkg1_id) THEN
    INSERT INTO billing_confidence_records (
      company_id, job_id, trip_id, proof_package_id, confidence_score, status, reason_json, summary,
      correlation_id, causation_id, created_at
    )
    VALUES (
      v_company_id, v_job1_id, v_trip1_id, v_proof_pkg1_id, 0.97, 'ready',
      '{"artifacts":1,"blockers":[]}'::jsonb, 'All billing gates satisfied for local test data.',
      'seed-corr-billing-ready', 'seed-caus-billing-ready', NOW()
    )
    RETURNING id INTO v_billing_ready_id;
  END IF;

  IF NOT EXISTS (SELECT 1 FROM billing_confidence_records WHERE company_id = v_company_id AND proof_package_id = v_proof_pkg2_id) THEN
    INSERT INTO billing_confidence_records (
      company_id, job_id, trip_id, proof_package_id, confidence_score, status, reason_json, summary,
      correlation_id, causation_id, created_at
    )
    VALUES (
      v_company_id, v_job2_id, v_trip2_id, v_proof_pkg2_id, 0.31, 'blocked',
      '{"artifacts":0,"blockers":["proof_artifacts:missing","site_access:open"]}'::jsonb, 'Billing blocked until proof and access complete.',
      'seed-corr-billing-blocked', 'seed-caus-billing-blocked', NOW()
    )
    RETURNING id INTO v_billing_blocked_id;
  END IF;

  IF NOT EXISTS (SELECT 1 FROM approval_requests WHERE tenant_id = v_company_id AND action_key = 'operations.access_document.waive' AND resource_id = v_job2_id::text) THEN
    INSERT INTO approval_requests (
      tenant_id, requested_by_actor_type, requested_by_actor_id, action_key, resource_type, resource_id, payload_json,
      risk_level, status, requested_at, correlation_id
    )
    VALUES (
      v_company_id, 'tenant_user', 'seed.user', 'operations.access_document.waive', 'job', v_job2_id::text,
      '{"reason":"missing gate pass for blocked job"}'::jsonb, 'high', 'pending', NOW() - INTERVAL '30 minutes', 'seed-corr-approval-001'
    )
    RETURNING id INTO v_approval_req_id;
  ELSE
    SELECT id INTO v_approval_req_id FROM approval_requests WHERE tenant_id = v_company_id AND action_key = 'operations.access_document.waive' AND resource_id = v_job2_id::text LIMIT 1;
  END IF;

  IF NOT EXISTS (SELECT 1 FROM approval_decisions WHERE approval_request_id = v_approval_req_id) THEN
    INSERT INTO approval_decisions (
      approval_request_id, tenant_id, approver_user_id, approver_actor_type, decision, notes, decided_at, correlation_id
    )
    VALUES (
      v_approval_req_id, v_company_id, 'seed.approver', 'tenant_admin', 'approved', 'Approved in seed data to demonstrate the workflow.', NOW() - INTERVAL '20 minutes', 'seed-corr-approval-001'
    )
    RETURNING id INTO v_approval_decision_id;
  END IF;

  IF NOT EXISTS (SELECT 1 FROM ai_reasoning_runs WHERE tenant_id = v_company_id AND trigger_type = 'stage10.local.seed') THEN
    INSERT INTO ai_reasoning_runs (
      tenant_id, trigger_type, input_json, prompt_template, expected_schema_json, status, confidence_score, output_json,
      error_json, correlation_id, causation_id, started_at, completed_at
    )
    VALUES (
      v_company_id, 'stage10.local.seed',
      '{"jobId":803,"tripId":2,"scenario":"seeded local verification"}'::jsonb,
      'stage10_operations_seed',
      '{"type":"object","required":["recommendations"]}'::jsonb,
      'completed', 0.91,
      '{"recommendations":[{"title":"Keep access gates visible","status":"ready"}]}'::jsonb,
      NULL, 'seed-corr-ai-run-001', 'seed-caus-ai-run-001', NOW() - INTERVAL '5 minutes', NOW() - INTERVAL '4 minutes'
    )
    RETURNING id INTO v_ai_run_id;
  ELSE
    SELECT id INTO v_ai_run_id FROM ai_reasoning_runs WHERE tenant_id = v_company_id AND trigger_type = 'stage10.local.seed' LIMIT 1;
  END IF;

  IF NOT EXISTS (SELECT 1 FROM ai_recommendations WHERE tenant_id = v_company_id AND source_event_id = 'seed-stage10-ops-ready') THEN
    INSERT INTO ai_recommendations (
      tenant_id, recommendation_type, title, summary, confidence_score, urgency_score, impact_json, reason_json,
      proposed_action_json, risk_level, status, source_event_id, actor_type, actor_id, created_at, correlation_id,
      causation_id, company_id, module_key, body, score, description, priority, action_label, action_type
    )
    VALUES (
      v_company_id, 'operations.execution_summary', 'Seeded operational readiness recommendation',
      'Seed data confirms a ready path and a blocked path for execution summary coverage.', 0.93, 0.71,
      '{"impact":"high"}'::jsonb, '{"scenario":"local seed"}'::jsonb, '{"action":"review_and_continue"}'::jsonb,
      'medium', 'active', 'seed-stage10-ops-ready', 'system', 'seed.bot', NOW() - INTERVAL '4 minutes',
      'seed-corr-ai-reco-001', 'seed-caus-ai-reco-001', v_company_id, 'operations-proof-center',
      'Seeded recommendation body for local verification.', 0.93, 'Seeded operational readiness recommendation',
      'High', 'Review readiness', 'review'
    )
    RETURNING id INTO v_ai_reco_id;
  ELSE
    SELECT id INTO v_ai_reco_id FROM ai_recommendations WHERE tenant_id = v_company_id AND source_event_id = 'seed-stage10-ops-ready' LIMIT 1;
  END IF;

  IF NOT EXISTS (SELECT 1 FROM ai_action_requests WHERE tenant_id = v_company_id AND recommendation_id = v_ai_reco_id) THEN
    INSERT INTO ai_action_requests (
      tenant_id, recommendation_id, action_key, resource_type, resource_id, payload_json, risk_level, status,
      requested_by_actor_type, requested_by_actor_id, requested_at, correlation_id, causation_id
    )
    VALUES (
      v_company_id, v_ai_reco_id, 'operations.proof.review', 'proof_package', v_proof_pkg2_id::text,
      '{"scenario":"local seed"}'::jsonb, 'high', 'approval_required', 'system', 'seed.bot',
      NOW() - INTERVAL '4 minutes', 'seed-corr-ai-action-001', 'seed-caus-ai-action-001'
    )
    RETURNING id INTO v_ai_action_id;
  ELSE
    SELECT id INTO v_ai_action_id FROM ai_action_requests WHERE tenant_id = v_company_id AND recommendation_id = v_ai_reco_id LIMIT 1;
  END IF;

  IF NOT EXISTS (SELECT 1 FROM domain_events WHERE tenant_id = v_company_id AND event_type = 'stage10.local.seed.completed') THEN
    INSERT INTO domain_events (
      tenant_id, event_type, aggregate_type, aggregate_id, payload_json, correlation_id, causation_id,
      idempotency_key, occurred_at, processed_at, status, retry_count
    )
    VALUES (
      v_company_id, 'stage10.local.seed.completed', 'local_seed', 'stage10',
      '{"scenario":"local module test data seeded"}'::jsonb, 'seed-corr-domain-001', 'seed-caus-domain-001',
      'seed-domain-001', NOW() - INTERVAL '2 minutes', NOW() - INTERVAL '1 minute', 'processed', 0
    )
    RETURNING id INTO v_domain_event_id;
  END IF;

  IF NOT EXISTS (SELECT 1 FROM outbox_messages WHERE tenant_id = v_company_id AND event_type = 'stage10.local.seed.completed') THEN
    INSERT INTO outbox_messages (
      tenant_id, event_type, aggregate_type, aggregate_id, payload_json, correlation_id, causation_id,
      idempotency_key, created_at, status, retry_count, next_attempt_at, processed_at, claimed_at, claimed_by,
      locked_until, last_error, dead_letter_reason
    )
    VALUES (
      v_company_id, 'stage10.local.seed.completed', 'local_seed', 'stage10',
      '{"scenario":"local module test data seeded"}'::jsonb, 'seed-corr-outbox-001', 'seed-caus-outbox-001',
      'seed-outbox-001', NOW() - INTERVAL '2 minutes', 'processed', 0, NOW() + INTERVAL '1 minute',
      NOW() - INTERVAL '1 minute', NOW() - INTERVAL '2 minutes', 'seed.worker',
      NOW() + INTERVAL '10 minutes', NULL, NULL
    )
    RETURNING id INTO v_outbox_id;
  END IF;

  IF NOT EXISTS (SELECT 1 FROM inbox_messages WHERE tenant_id = v_company_id AND external_id = 'seed-inbox-001') THEN
    INSERT INTO inbox_messages (
      tenant_id, event_type, source, external_id, payload_json, correlation_id, causation_id, received_at, status,
      idempotency_key, payload_hash, retry_count, processed_at, claimed_at, claimed_by, locked_until, last_error,
      dead_letter_reason
    )
    VALUES (
      v_company_id, 'stage10.external.seed', 'seed', 'seed-inbox-001',
      '{"scenario":"local module test data seeded"}'::jsonb, 'seed-corr-inbox-001', 'seed-caus-inbox-001',
      NOW() - INTERVAL '2 minutes', 'processed', 'seed-inbox-001', 'seed-payload-hash-001', 0,
      NOW() - INTERVAL '1 minute', NOW() - INTERVAL '2 minutes', 'seed.worker', NOW() + INTERVAL '10 minutes', NULL, NULL
    )
    RETURNING id INTO v_inbox_id;
  END IF;

  IF NOT EXISTS (SELECT 1 FROM event_processing_logs WHERE tenant_id = v_company_id AND event_type = 'stage10.local.seed.completed' AND processor = 'seed.worker') THEN
    INSERT INTO event_processing_logs (
      tenant_id, event_type, processor, status, message, correlation_id, causation_id, processed_at, retry_count
    )
    VALUES (
      v_company_id, 'stage10.local.seed.completed', 'seed.worker', 'success',
      'Seeded local module data processed successfully.', 'seed-corr-log-001', 'seed-caus-log-001',
      NOW() - INTERVAL '1 minute', 0
    )
    RETURNING id INTO v_event_log_id;
  END IF;
END $$;
