DO $$
BEGIN
  WITH seeded_positions(company_id, vehicle_id, device_id, driver_id, vehicle_code, device_serial, driver_name, lat, lng, speed_mph, heading, engine_status, telemetry_status, risk_level, alert_count, open_alert_count, stale_seconds, last_event_time, received_at, source_event_id, correlation_id, causation_id, source_channel, next_action, summary_json) AS (
    VALUES
      (1, 1, 1, 1, 'TRK-101', 'ELD-001-TRK101', 'Stage 7 Driver 1', 24.7136000, 46.6753000, 54.0, 92, 'Moving', 'healthy', 'low', 0, 0, 120, NOW() - INTERVAL '2 minutes', NOW() - INTERVAL '2 minutes', 91001, 'telemetry-seed-1', 'telemetry-seed-1', 'device', 'Continue route monitoring', '{"source":"local-test-seed","scenario":"steady"}'::jsonb),
      (1, 2, 2, 2, 'VAN-102', 'ELD-002-TRK102', 'Stage 7 Driver 2', 24.7742650, 46.7385860, 41.0, 84, 'Moving', 'watch', 'medium', 1, 1, 310, NOW() - INTERVAL '5 minutes', NOW() - INTERVAL '5 minutes', 91002, 'telemetry-seed-2', 'telemetry-seed-2', 'device', 'Check open alert and route timing', '{"source":"local-test-seed","scenario":"watch"}'::jsonb),
      (1, 4, 4, 4, 'REEFER-104', 'ELD-004-TRK104', 'Stage 7 Driver 4', 21.4858000, 39.1925000, 8.0, 180, 'Idle', 'stale', 'high', 2, 1, 1800, NOW() - INTERVAL '32 minutes', NOW() - INTERVAL '32 minutes', 91004, 'telemetry-seed-4', 'telemetry-seed-4', 'device', 'Investigate stale device and reefer risk', '{"source":"local-test-seed","scenario":"stale"}'::jsonb)
  )
  INSERT INTO latest_vehicle_positions (
      company_id, vehicle_id, device_id, driver_id, lat, lng, speed_mph, heading,
      accuracy_meters, engine_status, fuel_level, odometer_miles, battery_voltage,
      event_time, received_at, event_count
  )
  SELECT company_id, vehicle_id, device_id, driver_id, lat, lng, speed_mph, heading,
         8.0, engine_status, 78.5, 142550.0, 13.6,
         last_event_time, received_at, 3
  FROM seeded_positions
  ON CONFLICT (company_id, vehicle_id) DO UPDATE SET
      device_id = EXCLUDED.device_id,
      driver_id = EXCLUDED.driver_id,
      lat = EXCLUDED.lat,
      lng = EXCLUDED.lng,
      speed_mph = EXCLUDED.speed_mph,
      heading = EXCLUDED.heading,
      engine_status = EXCLUDED.engine_status,
      fuel_level = EXCLUDED.fuel_level,
      odometer_miles = EXCLUDED.odometer_miles,
      battery_voltage = EXCLUDED.battery_voltage,
      event_time = EXCLUDED.event_time,
      received_at = EXCLUDED.received_at,
      event_count = EXCLUDED.event_count;

  WITH seeded_positions(company_id, vehicle_id, device_id, driver_id, vehicle_code, device_serial, driver_name, lat, lng, speed_mph, heading, engine_status, telemetry_status, risk_level, alert_count, open_alert_count, stale_seconds, last_event_time, received_at, source_event_id, correlation_id, causation_id, source_channel, next_action, summary_json) AS (
    VALUES
      (1, 1, 1, 1, 'TRK-101', 'ELD-001-TRK101', 'Stage 7 Driver 1', 24.7136000, 46.6753000, 54.0, 92, 'Moving', 'healthy', 'low', 0, 0, 120, NOW() - INTERVAL '2 minutes', NOW() - INTERVAL '2 minutes', 91001, 'telemetry-seed-1', 'telemetry-seed-1', 'device', 'Continue route monitoring', '{"source":"local-test-seed","scenario":"steady"}'::jsonb),
      (1, 2, 2, 2, 'VAN-102', 'ELD-002-TRK102', 'Stage 7 Driver 2', 24.7742650, 46.7385860, 41.0, 84, 'Moving', 'watch', 'medium', 1, 1, 310, NOW() - INTERVAL '5 minutes', NOW() - INTERVAL '5 minutes', 91002, 'telemetry-seed-2', 'telemetry-seed-2', 'device', 'Check open alert and route timing', '{"source":"local-test-seed","scenario":"watch"}'::jsonb),
      (1, 4, 4, 4, 'REEFER-104', 'ELD-004-TRK104', 'Stage 7 Driver 4', 21.4858000, 39.1925000, 8.0, 180, 'Idle', 'stale', 'high', 2, 1, 1800, NOW() - INTERVAL '32 minutes', NOW() - INTERVAL '32 minutes', 91004, 'telemetry-seed-4', 'telemetry-seed-4', 'device', 'Investigate stale device and reefer risk', '{"source":"local-test-seed","scenario":"stale"}'::jsonb)
  )
  INSERT INTO telemetry_live_asset_states (
      company_id, vehicle_id, device_id, driver_id, vehicle_code, device_serial, driver_name,
      lat, lng, speed_mph, heading, engine_status, telemetry_status, risk_level,
      alert_count, open_alert_count, stale_seconds, last_event_time, received_at,
      source_event_id, correlation_id, causation_id, source_channel, next_action, summary_json, updated_at
  )
  SELECT company_id, vehicle_id, device_id, driver_id, vehicle_code, device_serial, driver_name,
         lat, lng, speed_mph, heading, engine_status, telemetry_status, risk_level,
         alert_count, open_alert_count, stale_seconds, last_event_time, received_at,
         source_event_id, correlation_id, causation_id, source_channel, next_action, summary_json, NOW()
  FROM seeded_positions
  ON CONFLICT (company_id, vehicle_id) DO UPDATE SET
      device_id = EXCLUDED.device_id,
      driver_id = EXCLUDED.driver_id,
      vehicle_code = EXCLUDED.vehicle_code,
      device_serial = EXCLUDED.device_serial,
      driver_name = EXCLUDED.driver_name,
      lat = EXCLUDED.lat,
      lng = EXCLUDED.lng,
      speed_mph = EXCLUDED.speed_mph,
      heading = EXCLUDED.heading,
      engine_status = EXCLUDED.engine_status,
      telemetry_status = EXCLUDED.telemetry_status,
      risk_level = EXCLUDED.risk_level,
      alert_count = EXCLUDED.alert_count,
      open_alert_count = EXCLUDED.open_alert_count,
      stale_seconds = EXCLUDED.stale_seconds,
      last_event_time = EXCLUDED.last_event_time,
      received_at = EXCLUDED.received_at,
      source_event_id = EXCLUDED.source_event_id,
      correlation_id = EXCLUDED.correlation_id,
      causation_id = EXCLUDED.causation_id,
      source_channel = EXCLUDED.source_channel,
      next_action = EXCLUDED.next_action,
      summary_json = EXCLUDED.summary_json,
      updated_at = NOW();

  INSERT INTO telemetry_alerts (
      company_id, vehicle_id, device_id, driver_id, alert_type, severity, message, source_event_id, status,
      acknowledged_at, acknowledged_by, resolved_at, resolved_by, created_at, updated_at
  )
  SELECT 1, 2, 2, 2, 'speeding', 'Medium', 'Vehicle exceeded the configured speed threshold on the local test seed.', 91002, 'Open', NULL, NULL, NULL, NULL, NOW() - INTERVAL '5 minutes', NULL
  WHERE NOT EXISTS (
    SELECT 1 FROM telemetry_alerts WHERE company_id=1 AND source_event_id=91002 AND alert_type='speeding'
  );

  INSERT INTO telemetry_alerts (
      company_id, vehicle_id, device_id, driver_id, alert_type, severity, message, source_event_id, status,
      acknowledged_at, acknowledged_by, resolved_at, resolved_by, created_at, updated_at
  )
  SELECT 1, 4, 4, 4, 'stale_device', 'High', 'Temperature and GPS heartbeat are stale on the local test seed.', 91004, 'Open', NULL, NULL, NULL, NULL, NOW() - INTERVAL '30 minutes', NULL
  WHERE NOT EXISTS (
    SELECT 1 FROM telemetry_alerts WHERE company_id=1 AND source_event_id=91004 AND alert_type='stale_device'
  );
END $$;
