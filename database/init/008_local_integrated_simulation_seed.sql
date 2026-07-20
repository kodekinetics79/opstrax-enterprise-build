DO $$
DECLARE
  v_company_id bigint;
  v_shipment1_id bigint;
  v_shipment2_id bigint;
  v_zone_id bigint;
  v_device_id bigint;
  v_reading_id bigint;
  v_asset_type_id bigint;
BEGIN
  SELECT id INTO v_company_id FROM companies ORDER BY id LIMIT 1;
  IF v_company_id IS NULL THEN
    RAISE NOTICE 'No company rows available for integrated simulation seed.';
    RETURN;
  END IF;

  IF NOT EXISTS (SELECT 1 FROM fleet_tms_shipments WHERE company_id = v_company_id AND shipment_number = 'SIM-SHP-001') THEN
    INSERT INTO fleet_tms_shipments (
      company_id, shipment_number, customer_name, customer_segment, origin, destination, city, status, priority, mode,
      piece_count, weight_kg, volume_cbm, declared_value, carrier_name, customer_vat_number,
      customer_commercial_registration_no, customer_national_address_building_no,
      customer_national_address_additional_no, customer_national_address_district,
      customer_national_address_city, customer_national_address_region, customer_national_address_postal_code,
      customer_national_address_country, driver_name, vehicle_number, route_code, pod_status,
      temperature_range, notes, is_invoice_ready, invoice_ready_at_utc, invoice_readiness_notes,
      pickup_scheduled_at_utc, picked_up_at_utc, delivered_at_utc, created_at_utc, updated_at_utc
    )
    VALUES (
      v_company_id, 'SIM-SHP-001', 'Simulation Customer A', 'Retail', 'Riyadh DC', 'Dammam Hub', 'Dammam',
      'PickedUp', 'High', 'Road', 12, 1840.50, 24.50, 35000, 'SIM Carrier', '300000000000001',
      '1010101010', '1001', '2002', 'Olaya', 'Riyadh', 'Riyadh', '11564', 'SA',
      'SIM-DRIVER-001', 'SIM-TRK-001', 'SIM-ROUTE-001', 'Verified', '2-8C',
      'Integrated simulation shipment.', true, NOW(), 'Invoice ready in simulation seed.',
      NOW() - INTERVAL '4 hours', NOW() - INTERVAL '3 hours', NOW() - INTERVAL '1 hour', NOW(), NOW()
    );
  END IF;

  IF NOT EXISTS (SELECT 1 FROM fleet_tms_shipments WHERE company_id = v_company_id AND shipment_number = 'SIM-SHP-002') THEN
    INSERT INTO fleet_tms_shipments (
      company_id, shipment_number, customer_name, customer_segment, origin, destination, city, status, priority, mode,
      piece_count, weight_kg, volume_cbm, declared_value, carrier_name, customer_vat_number,
      customer_commercial_registration_no, customer_national_address_building_no,
      customer_national_address_additional_no, customer_national_address_district,
      customer_national_address_city, customer_national_address_region, customer_national_address_postal_code,
      customer_national_address_country, driver_name, vehicle_number, route_code, pod_status,
      temperature_range, notes, is_invoice_ready, pickup_scheduled_at_utc, created_at_utc, updated_at_utc
    )
    VALUES (
      v_company_id, 'SIM-SHP-002', 'Simulation Customer B', 'Pharma', 'Jeddah Port', 'Riyadh North DC', 'Riyadh',
      'InTransit', 'Critical', 'Road', 8, 920.75, 12.25, 22000, 'SIM Carrier', '300000000000002',
      '2020202020', '3003', '4004', 'Al Aziziyah', 'Jeddah', 'Makkah', '21441', 'SA',
      'SIM-DRIVER-002', 'SIM-TRK-002', 'SIM-ROUTE-002', 'Pending', '2-8C',
      'Blocked path for simulation coverage.', false, NOW() - INTERVAL '2 hours', NOW(), NOW()
    );
  END IF;

  SELECT id INTO v_shipment1_id FROM fleet_tms_shipments WHERE company_id = v_company_id AND shipment_number = 'SIM-SHP-001' LIMIT 1;
  SELECT id INTO v_shipment2_id FROM fleet_tms_shipments WHERE company_id = v_company_id AND shipment_number = 'SIM-SHP-002' LIMIT 1;

  IF NOT EXISTS (SELECT 1 FROM fleet_tms_vehicles WHERE company_id = v_company_id AND vehicle_number = 'SIM-TRK-001') THEN
    INSERT INTO fleet_tms_vehicles (
      company_id, vehicle_number, plate_number, type, status, driver_name, capacity_kg, capacity_cbm, current_load_kg,
      fuel_level_percent, odometer_km, health_status, is_refrigerated, temperature_celsius, last_known_location,
      last_ping_at_utc, last_service_at_utc, next_service_at_utc, notes, created_at_utc, updated_at_utc
    )
    VALUES (
      v_company_id, 'SIM-TRK-001', 'SIM-PLATE-001', 'Reefer', 'OnTrip', 'SIM-DRIVER-001', 25000, 62, 8400,
      77, 142000, 'Healthy', true, 4.2, 'Riyadh DC',
      NOW() - INTERVAL '2 minutes', NOW() - INTERVAL '12 days', NOW() + INTERVAL '18 days',
      'Simulation vehicle.', NOW(), NOW()
    );
  END IF;

  IF NOT EXISTS (SELECT 1 FROM fleet_tms_temperature_zones WHERE company_id = v_company_id AND code = 'SIM-CHILL') THEN
    INSERT INTO fleet_tms_temperature_zones (company_id, code, name, min_celsius, max_celsius, color, is_active, notes, created_at_utc, updated_at_utc)
    VALUES (v_company_id, 'SIM-CHILL', 'Simulation Chilled', 2, 8, '#22d3ee', true, 'Simulation zone', NOW(), NOW());
  END IF;

  SELECT id INTO v_zone_id FROM fleet_tms_temperature_zones WHERE company_id = v_company_id AND code = 'SIM-CHILL' LIMIT 1;

  IF NOT EXISTS (SELECT 1 FROM fleet_tms_temperature_devices WHERE company_id = v_company_id AND device_code = 'SIM-TMP-001') THEN
    INSERT INTO fleet_tms_temperature_devices (
      company_id, device_code, name, zone_id, shipment_id, vehicle_number, status,
      last_reported_temperature_celsius, battery_percent, last_ping_at_utc, notes, created_at_utc, updated_at_utc
    )
    VALUES (
      v_company_id, 'SIM-TMP-001', 'Simulation Reefer Sensor', v_zone_id, v_shipment1_id, 'SIM-TRK-001', 'Active',
      4.1, 92, NOW() - INTERVAL '3 minutes', 'Simulation cold-chain sensor.', NOW(), NOW()
    )
    RETURNING id INTO v_device_id;
  ELSE
    SELECT id INTO v_device_id FROM fleet_tms_temperature_devices WHERE company_id = v_company_id AND device_code = 'SIM-TMP-001' LIMIT 1;
  END IF;

  IF NOT EXISTS (SELECT 1 FROM fleet_tms_temperature_readings WHERE company_id = v_company_id AND device_id = v_device_id AND source = 'Simulation') THEN
    INSERT INTO fleet_tms_temperature_readings (
      company_id, device_id, shipment_id, zone_id, temperature_celsius, humidity_percent, latitude, longitude,
      source, status, notes, recorded_at_utc, created_at_utc
    )
    VALUES (
      v_company_id, v_device_id, v_shipment1_id, v_zone_id, 4.6, 53, 24.7136, 46.6753,
      'Simulation', 'Normal', 'Simulation compliant reading.', NOW() - INTERVAL '2 minutes', NOW()
    )
    RETURNING id INTO v_reading_id;
  ELSE
    SELECT id INTO v_reading_id FROM fleet_tms_temperature_readings WHERE company_id = v_company_id AND device_id = v_device_id AND source = 'Simulation' LIMIT 1;
  END IF;

  IF NOT EXISTS (SELECT 1 FROM fleet_tms_temperature_alerts WHERE company_id = v_company_id AND reading_id = v_reading_id) THEN
    INSERT INTO fleet_tms_temperature_alerts (
      company_id, device_id, shipment_id, reading_id, alert_type, severity, status,
      threshold_min, threshold_max, measured_temperature, triggered_at_utc, resolved_at_utc, resolved_by,
      resolution_notes, notes
    )
    VALUES (
      v_company_id, v_device_id, v_shipment1_id, v_reading_id, 'TemperatureBreach', 'High', 'Open',
      2, 8, 10.8, NOW() - INTERVAL '1 minute', NULL, '', '', 'Simulation alert for breach coverage'
    );
  END IF;

  IF NOT EXISTS (SELECT 1 FROM fleet_tms_readiness_documents WHERE company_id = v_company_id AND subject_id = 'SIM-TRK-001') THEN
    INSERT INTO fleet_tms_readiness_documents (
      company_id, kind, subject_type, subject_id, subject_name, document_type, document_number, transport_document_no,
      permit_no, vat_number, commercial_registration_no, country_code, national_address_building_no,
      national_address_additional_no, district, city, region, postal_code, document_status, expiry_status,
      issue_date, gregorian_expiry_date, notes
    )
    VALUES (
      v_company_id, 'Transport', 'Vehicle', 'SIM-TRK-001', 'Simulation Reefer', 'Transport Operating Card',
      'SIM-TOC-001', 'SIM-TDN-001', 'SIM-PERMIT-001', '', '', 'SA', '1001', '2002', 'Olaya', 'Riyadh',
      'Riyadh Province', '11564', 'Active', 'Healthy', CURRENT_DATE - INTERVAL '180 days',
      CURRENT_DATE + INTERVAL '180 days', 'Simulation Saudi/GCC readiness document.'
    );
  END IF;

  IF NOT EXISTS (SELECT 1 FROM fleet_tms_asset_types WHERE company_id = v_company_id AND code = 'SIM-PALLET') THEN
    INSERT INTO fleet_tms_asset_types (company_id, code, name, description, is_returnable, created_at_utc, updated_at_utc)
    VALUES (v_company_id, 'SIM-PALLET', 'Simulation Pallet', 'Returnable pallet for simulation', true, NOW(), NOW())
    RETURNING id INTO v_asset_type_id;
  ELSE
    SELECT id INTO v_asset_type_id FROM fleet_tms_asset_types WHERE company_id = v_company_id AND code = 'SIM-PALLET' LIMIT 1;
  END IF;

  IF NOT EXISTS (SELECT 1 FROM fleet_tms_assets WHERE company_id = v_company_id AND asset_tag = 'SIM-AST-001') THEN
    INSERT INTO fleet_tms_assets (
      company_id, asset_type_id, asset_tag, name, status, current_location, condition, is_returnable,
      quantity, unit_of_measure, notes, last_seen_at_utc, created_at_utc, updated_at_utc
    )
    VALUES (
      v_company_id, v_asset_type_id, 'SIM-AST-001', 'Simulation Pallet Stack', 'InUse', 'Riyadh DC', 'Good', true,
      4, 'Each', 'Simulation returnable asset.', NOW() - INTERVAL '8 minutes', NOW(), NOW()
    );
  END IF;

  IF NOT EXISTS (SELECT 1 FROM fleet_tms_asset_events WHERE company_id = v_company_id AND asset_id IN (SELECT id FROM fleet_tms_assets WHERE company_id = v_company_id AND asset_tag = 'SIM-AST-001' LIMIT 1) AND event_type = 'check_out') THEN
    INSERT INTO fleet_tms_asset_events (company_id, asset_id, event_type, quantity, location, actor_name, occurred_at_utc, notes)
    SELECT v_company_id, a.id, 'check_out', 4, 'Riyadh DC', 'simulation.seed', NOW() - INTERVAL '18 minutes', 'Simulation asset check-out.'
    FROM fleet_tms_assets a
    WHERE a.company_id = v_company_id AND a.asset_tag = 'SIM-AST-001';
  END IF;

  IF NOT EXISTS (SELECT 1 FROM fleet_tms_dispatch_orders WHERE company_id = v_company_id AND order_number = 'SIM-ORD-001') THEN
    INSERT INTO fleet_tms_dispatch_orders (
      company_id, order_number, customer_name, customer_segment, sales_channel, city, area, status, priority, item_count,
      order_value, route_code, driver_name, vehicle_number, dispatch_notes, created_at_utc, promised_at_utc,
      dispatched_at_utc, delivered_at_utc, updated_at_utc
    )
    VALUES (
      v_company_id, 'SIM-ORD-001', 'Simulation Customer', 'Retail', 'Portal', 'Riyadh', 'North Hub', 'Dispatched',
      'High', 7, 18250, 'SIM-ROUTE-001', 'SIM-DRIVER-001', 'SIM-TRK-001',
      'Simulation dispatch order.', NOW() - INTERVAL '2 hours', NOW() + INTERVAL '2 hours',
      NOW() - INTERVAL '90 minutes', NULL, NOW()
    );
  END IF;

  IF NOT EXISTS (SELECT 1 FROM fleet_tms_delivery_routes WHERE company_id = v_company_id AND route_code = 'SIM-ROUTE-001') THEN
    INSERT INTO fleet_tms_delivery_routes (
      company_id, route_code, hub, territory, driver_name, vehicle_number, status, planned_stops, completed_stops,
      distance_km, completion_percent, current_stop, next_stop, planned_for_date, departure_time_utc, eta_complete_utc, notes
    )
    VALUES (
      v_company_id, 'SIM-ROUTE-001', 'Riyadh North Hub', 'North Corridor', 'SIM-DRIVER-001', 'SIM-TRK-001', 'Active',
      3, 2, 42.5, 66.7, 'Warehouse', 'Customer Drop', CURRENT_DATE, NOW() - INTERVAL '2 hours',
      NOW() + INTERVAL '1 hour', 'Simulation route.'
    );
  END IF;

  IF NOT EXISTS (SELECT 1 FROM fleet_tms_last_mile_stops WHERE company_id = v_company_id AND order_number = 'SIM-ORD-001') THEN
    INSERT INTO fleet_tms_last_mile_stops (
      company_id, order_number, route_code, customer_name, address_line, city, region, postal_code, country,
      saudi_national_address_building_no, saudi_national_address_additional_no, saudi_national_address_district,
      status, proof_status, recipient_name, attempt_count, rider_name, time_window, eta_utc, delivered_at_utc,
      exception_reason, created_at_utc, updated_at_utc
    )
    VALUES (
      v_company_id, 'SIM-ORD-001', 'SIM-ROUTE-001', 'Simulation Customer', 'King Fahd Road', 'Riyadh', 'Riyadh Province',
      '11564', 'Saudi Arabia', '1001', '2002', 'Olaya', 'OutForDelivery', 'Captured', 'Receiving Desk', 0,
      'SIM-DRIVER-001', '14:00-16:00', NOW() + INTERVAL '45 minutes', NULL, '', NOW() - INTERVAL '2 hours', NOW()
    );
  END IF;

  IF NOT EXISTS (SELECT 1 FROM fleet_tms_shipment_stops WHERE company_id = v_company_id AND shipment_id = v_shipment1_id AND stop_type = 'Pickup') THEN
    INSERT INTO fleet_tms_shipment_stops (
      company_id, shipment_id, stop_type, sequence_no, location_name, contact_name, contact_phone, address_line1, city,
      region, postal_code, country, saudi_national_address_building_no, saudi_national_address_additional_no,
      saudi_national_address_district, latitude, longitude, planned_arrival_at, actual_arrival_at, completed_at,
      status, notes, created_at, updated_at
    )
    VALUES (
      v_company_id, v_shipment1_id, 'Pickup', 1, 'Riyadh DC', 'Warehouse Lead', '+96650001001', 'King Fahd Road', 'Riyadh',
      'Riyadh', '11564', 'Saudi Arabia', '1001', '2002', 'Olaya', 24.7136, 46.6753,
      NOW() - INTERVAL '4 hours', NOW() - INTERVAL '4 hours', NOW() - INTERVAL '4 hours',
      'Completed', 'Simulation pickup stop.', NOW(), NOW()
    );
  END IF;

  IF NOT EXISTS (SELECT 1 FROM fleet_tms_pods WHERE company_id = v_company_id AND shipment_id = v_shipment1_id) THEN
    INSERT INTO fleet_tms_pods (
      company_id, shipment_id, stop_id, captured_by_user_id, driver_id, vehicle_id, recipient_name, recipient_phone,
      signature_url, photo_url, document_url, notes, delivery_condition, captured_latitude, captured_longitude,
      captured_at, verified_at, status, created_at, updated_at
    )
    SELECT v_company_id, v_shipment1_id, st.id, NULL, NULL, NULL, 'Receiving Desk', '+96650001002',
           'https://seed.local/signatures/sim-pod.png', 'https://seed.local/photos/sim-pod.png', 'https://seed.local/docs/sim-pod.pdf',
           'Simulation POD for integrated coverage.', 'Good', 24.7136, 46.6753,
           NOW() - INTERVAL '45 minutes', NOW() - INTERVAL '35 minutes', 'Verified', NOW() - INTERVAL '45 minutes', NOW() - INTERVAL '35 minutes'
    FROM fleet_tms_shipment_stops st
    WHERE st.company_id = v_company_id AND st.shipment_id = v_shipment1_id AND st.stop_type = 'Pickup'
    LIMIT 1;
  END IF;
END $$;
