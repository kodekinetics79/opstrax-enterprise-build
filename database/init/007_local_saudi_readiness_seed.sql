DO $$
BEGIN
  INSERT INTO fleet_tms_readiness_documents (
      company_id, kind, subject_type, subject_id, subject_name, document_type,
      document_number, transport_document_no, permit_no, vat_number,
      commercial_registration_no, country_code, national_address_building_no,
      national_address_additional_no, district, city, region, postal_code,
      document_status, expiry_status, issue_date, hijri_expiry_date, gregorian_expiry_date, notes
  )
  SELECT 1, 'Compliance', 'Branch', 'BR-RIY-001', 'Riyadh North Hub', 'Commercial Registration',
         'CR-RIY-001', '', '', '300000000000001', '1010101010', 'SA', '7421', '2231',
         'Al Olaya', 'Riyadh', 'Riyadh Province', '12211',
         'Active', 'Healthy', CURRENT_DATE - INTERVAL '180 days', NULL, CURRENT_DATE + INTERVAL '365 days',
         'Hub commercial registration for Saudi/GCC readiness.'
  WHERE NOT EXISTS (
    SELECT 1 FROM fleet_tms_readiness_documents
    WHERE company_id=1 AND subject_id='BR-RIY-001' AND document_type='Commercial Registration'
  );

  INSERT INTO fleet_tms_readiness_documents (
      company_id, kind, subject_type, subject_id, subject_name, document_type,
      document_number, transport_document_no, permit_no, vat_number,
      commercial_registration_no, country_code, national_address_building_no,
      national_address_additional_no, district, city, region, postal_code,
      document_status, expiry_status, issue_date, hijri_expiry_date, gregorian_expiry_date, notes
  )
  SELECT 1, 'Transport', 'Vehicle', 'VEH-REEFER-104', 'REEFER-104', 'Transport Operating Card',
         'TOC-REEFER-104', 'TDN-REEFER-104', 'PERM-REEFER-104', '', '',
         'SA', '5150', '4501', 'Al Aziziyah', 'Dammam', 'Eastern Province', '32271',
         'Active', 'ExpiringSoon', CURRENT_DATE - INTERVAL '220 days', NULL, CURRENT_DATE + INTERVAL '12 days',
         'Vehicle transport operating card nearing expiry for local verification.'
  WHERE NOT EXISTS (
    SELECT 1 FROM fleet_tms_readiness_documents
    WHERE company_id=1 AND subject_id='VEH-REEFER-104' AND document_type='Transport Operating Card'
  );

  INSERT INTO fleet_tms_readiness_documents (
      company_id, kind, subject_type, subject_id, subject_name, document_type,
      document_number, transport_document_no, permit_no, vat_number,
      commercial_registration_no, country_code, national_address_building_no,
      national_address_additional_no, district, city, region, postal_code,
      document_status, expiry_status, issue_date, hijri_expiry_date, gregorian_expiry_date, notes
  )
  SELECT 1, 'Driver', 'Driver', 'DRV-004', 'Stage 7 Driver 4', 'Saudi / GCC Driving License',
         'DL-DRIV-004', '', '', '', '',
         'SA', '', '', 'Al Khalidiyah', 'Khobar', 'Eastern Province', '31952',
         'Active', 'Healthy', CURRENT_DATE - INTERVAL '90 days', NULL, CURRENT_DATE + INTERVAL '210 days',
         'Driver license stored for Saudi/GCC readiness checks.'
  WHERE NOT EXISTS (
    SELECT 1 FROM fleet_tms_readiness_documents
    WHERE company_id=1 AND subject_id='DRV-004' AND document_type='Saudi / GCC Driving License'
  );

  INSERT INTO fleet_tms_readiness_documents (
      company_id, kind, subject_type, subject_id, subject_name, document_type,
      document_number, transport_document_no, permit_no, vat_number,
      commercial_registration_no, country_code, national_address_building_no,
      national_address_additional_no, district, city, region, postal_code,
      document_status, expiry_status, issue_date, hijri_expiry_date, gregorian_expiry_date, notes
  )
  SELECT 1, 'Transport', 'Shipment', 'SHP-1002', 'SHP-1002 / Tamimi Markets', 'Shipment Compliance Pack',
         'SHPDOC-1002', '', '', '300000000000003', '1010101010',
         'SA', '', '', 'Al Faisaliyah', 'Jeddah', 'Makkah Province', '22234',
         'Active', 'Healthy', CURRENT_DATE - INTERVAL '30 days', NULL, CURRENT_DATE + INTERVAL '540 days',
         'Delivered shipment retained for invoice-readiness and proof checks.'
  WHERE NOT EXISTS (
    SELECT 1 FROM fleet_tms_readiness_documents
    WHERE company_id=1 AND subject_id='SHP-1002' AND document_type='Shipment Compliance Pack'
  );

  INSERT INTO fleet_tms_readiness_documents (
      company_id, kind, subject_type, subject_id, subject_name, document_type,
      document_number, transport_document_no, permit_no, vat_number,
      commercial_registration_no, country_code, national_address_building_no,
      national_address_additional_no, district, city, region, postal_code,
      document_status, expiry_status, issue_date, hijri_expiry_date, gregorian_expiry_date, notes
  )
  SELECT 1, 'Compliance', 'Carrier', 'CAR-LOCAL-001', 'Local Carrier Ops', 'Carrier VAT Certificate',
         'VAT-CAR-001', '', '', '300000000000009', '2030405060',
         'SA', '8001', '4567', 'Al Khuzama', 'Riyadh', 'Riyadh Province', '11564',
         'Active', 'Healthy', CURRENT_DATE - INTERVAL '60 days', NULL, CURRENT_DATE + INTERVAL '365 days',
         'Carrier readiness record to make the Saudi/GCC ledger feel complete.'
  WHERE NOT EXISTS (
    SELECT 1 FROM fleet_tms_readiness_documents
    WHERE company_id=1 AND subject_id='CAR-LOCAL-001' AND document_type='Carrier VAT Certificate'
  );
END $$;
