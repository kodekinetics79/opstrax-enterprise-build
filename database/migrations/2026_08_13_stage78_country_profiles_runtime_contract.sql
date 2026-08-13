-- Stage 78 — Protected-environment country profile runtime contract
--
-- Protected Staging and Production skip runtime schema initialization. Tenant
-- provisioning nevertheless resolves country_profiles before inserting a tenant,
-- so this global reference catalog must be owner-migrated and ready before boot.
-- The rows below are localization reference data, not tenant/customer demo data.

BEGIN;

CREATE TABLE IF NOT EXISTS country_profiles (
  country_code VARCHAR(2) PRIMARY KEY,
  country_name VARCHAR(120) NOT NULL,
  default_currency VARCHAR(8) NOT NULL,
  default_locale VARCHAR(20) NOT NULL,
  text_direction VARCHAR(3) NOT NULL DEFAULT 'ltr',
  calendar_system VARCHAR(40) NOT NULL DEFAULT 'gregorian',
  invoicing_scheme VARCHAR(40) NOT NULL DEFAULT 'standard',
  tax_id_label VARCHAR(60) NOT NULL DEFAULT 'Tax ID',
  default_tax_rate NUMERIC(6,4) NULL,
  data_residency_note VARCHAR(400) NULL,
  auto_enabled_features JSONB NOT NULL DEFAULT '[]'::jsonb,
  created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  CONSTRAINT chk_country_text_direction CHECK (text_direction IN ('ltr','rtl'))
);

ALTER TABLE companies ADD COLUMN IF NOT EXISTS country VARCHAR(2) NULL;
ALTER TABLE companies ADD COLUMN IF NOT EXISTS currency VARCHAR(8) NULL;

WITH profile(country_code,country_name,default_currency,default_locale,text_direction,
  calendar_system,invoicing_scheme,tax_id_label,default_tax_rate,data_residency_note,
  auto_enabled_features) AS (VALUES
  ('US','United States','USD','en-US','ltr','gregorian','standard','EIN / Tax ID',NULL,NULL,'[]'::jsonb),
  ('CA','Canada','CAD','en-CA','ltr','gregorian','standard','GST/HST Number',0.0500,NULL,'[]'::jsonb),
  ('GB','United Kingdom','GBP','en-GB','ltr','gregorian','standard','VAT Number',0.2000,NULL,'[]'::jsonb),
  ('IE','Ireland','EUR','en-IE','ltr','gregorian','standard','VAT Number',0.2300,'EU data residency (GDPR).','[]'::jsonb),
  ('DE','Germany','EUR','de-DE','ltr','gregorian','standard','USt-IdNr.',0.1900,'EU data residency (GDPR).','[]'::jsonb),
  ('FR','France','EUR','fr-FR','ltr','gregorian','standard','TVA Number',0.2000,'EU data residency (GDPR).','[]'::jsonb),
  ('NL','Netherlands','EUR','nl-NL','ltr','gregorian','standard','BTW Number',0.2100,'EU data residency (GDPR).','[]'::jsonb),
  ('ES','Spain','EUR','es-ES','ltr','gregorian','standard','NIF / VAT',0.2100,'EU data residency (GDPR).','[]'::jsonb),
  ('IT','Italy','EUR','it-IT','ltr','gregorian','standard','Partita IVA',0.2200,'EU data residency (GDPR).','[]'::jsonb),
  ('AU','Australia','AUD','en-AU','ltr','gregorian','standard','ABN',0.1000,NULL,'[]'::jsonb),
  ('NZ','New Zealand','NZD','en-NZ','ltr','gregorian','standard','GST Number',0.1500,NULL,'[]'::jsonb),
  ('SG','Singapore','SGD','en-SG','ltr','gregorian','standard','GST Reg. No.',0.0900,NULL,'[]'::jsonb),
  ('IN','India','INR','en-IN','ltr','gregorian','standard','GSTIN',0.1800,NULL,'[]'::jsonb),
  ('JP','Japan','JPY','ja-JP','ltr','gregorian','standard','Corporate Number',0.1000,NULL,'[]'::jsonb),
  ('BR','Brazil','BRL','pt-BR','ltr','gregorian','standard','CNPJ',0.1700,'Brazil data residency (LGPD).','[]'::jsonb),
  ('MX','Mexico','MXN','es-MX','ltr','gregorian','standard','RFC',0.1600,NULL,'[]'::jsonb),
  ('ZA','South Africa','ZAR','en-ZA','ltr','gregorian','standard','VAT Number',0.1500,NULL,'[]'::jsonb),
  ('SA','Saudi Arabia','SAR','ar-SA','rtl','gregorian_hijri_dual','zatca_phase2','VAT Number',0.1500,'KSA data residency expected for regulated invoicing (ZATCA).','["zatca_invoicing","hijri_calendar_toggle","arabic_rtl"]'::jsonb),
  ('AE','United Arab Emirates','AED','ar-AE','rtl','gregorian','standard','TRN',0.0500,NULL,'["arabic_rtl"]'::jsonb),
  ('QA','Qatar','QAR','ar-QA','rtl','gregorian','standard','Tax Card No.',NULL,NULL,'["arabic_rtl"]'::jsonb),
  ('KW','Kuwait','KWD','ar-KW','rtl','gregorian','standard','Tax No.',NULL,NULL,'["arabic_rtl"]'::jsonb),
  ('BH','Bahrain','BHD','ar-BH','rtl','gregorian','standard','VAT Number',0.1000,NULL,'["arabic_rtl"]'::jsonb),
  ('OM','Oman','OMR','ar-OM','rtl','gregorian','standard','VAT Number',0.0500,NULL,'["arabic_rtl"]'::jsonb),
  ('EG','Egypt','EGP','ar-EG','rtl','gregorian','standard','Tax Reg. No.',0.1400,NULL,'["arabic_rtl"]'::jsonb)
)
INSERT INTO country_profiles
  (country_code,country_name,default_currency,default_locale,text_direction,
   calendar_system,invoicing_scheme,tax_id_label,default_tax_rate,data_residency_note,
   auto_enabled_features,updated_at)
SELECT country_code,country_name,default_currency,default_locale,text_direction,
       calendar_system,invoicing_scheme,tax_id_label,default_tax_rate,data_residency_note,
       auto_enabled_features,NOW()
FROM profile
ON CONFLICT (country_code) DO UPDATE SET
  country_name=EXCLUDED.country_name,
  default_currency=EXCLUDED.default_currency,
  default_locale=EXCLUDED.default_locale,
  text_direction=EXCLUDED.text_direction,
  calendar_system=EXCLUDED.calendar_system,
  invoicing_scheme=EXCLUDED.invoicing_scheme,
  tax_id_label=EXCLUDED.tax_id_label,
  default_tax_rate=EXCLUDED.default_tax_rate,
  data_residency_note=EXCLUDED.data_residency_note,
  auto_enabled_features=EXCLUDED.auto_enabled_features,
  updated_at=NOW();

INSERT INTO schema_migrations (version, description)
VALUES ('2026_08_13_stage78_country_profiles_runtime_contract',
        'Protected-environment country profile runtime contract')
ON CONFLICT (version) DO NOTHING;

COMMIT;
