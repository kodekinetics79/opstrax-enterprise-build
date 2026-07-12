-- 006_tenant_profile_and_countries.sql
-- Owner-applied migration (run as the DB owner, NOT the restricted opstrax_app role).
--
-- WHY THIS IS A MANUAL/OWNER STEP:
--   When the API runs as the restricted opstrax_app role with RLS enforced
--   (Rls__EnforceTenantContext=true), Program.cs deliberately SKIPS schema init —
--   the DDL/seed here (and any future schema change) MUST be applied out-of-band by
--   the owner. The code in PlatformSchemaService.EnsureTenantProfileColumnsAsync and
--   CountryProfileSchemaService.SeedProfilesAsync only runs when the app connects as
--   an owner-capable role, so this file mirrors that logic for the RLS deployment.
--
-- SAFETY: fully idempotent (ADD COLUMN IF NOT EXISTS + ON CONFLICT upsert). Safe to
--   re-run. Apply this BEFORE (or with) deploying the matching app build, otherwise
--   GET /api/platform/tenants 500s on the missing columns.

-- ── Extended tenant provisioning attributes (New Tenant form) ────────────────
ALTER TABLE companies ADD COLUMN IF NOT EXISTS legal_name            VARCHAR(220);
ALTER TABLE companies ADD COLUMN IF NOT EXISTS website               VARCHAR(200);
ALTER TABLE companies ADD COLUMN IF NOT EXISTS fleet_size            INT;
ALTER TABLE companies ADD COLUMN IF NOT EXISTS tax_id                VARCHAR(80);
ALTER TABLE companies ADD COLUMN IF NOT EXISTS primary_contact_name  VARCHAR(160);
ALTER TABLE companies ADD COLUMN IF NOT EXISTS primary_contact_email VARCHAR(200);
ALTER TABLE companies ADD COLUMN IF NOT EXISTS primary_contact_phone VARCHAR(40);
ALTER TABLE companies ADD COLUMN IF NOT EXISTS billing_email         VARCHAR(200);

ALTER TABLE tenant_subscriptions ADD COLUMN IF NOT EXISTS billing_cycle VARCHAR(20) NOT NULL DEFAULT 'monthly';

-- ── Country profiles: baseline of 24 major logistics markets ─────────────────
INSERT INTO country_profiles
  (country_code, country_name, default_currency, default_locale, text_direction,
   calendar_system, invoicing_scheme, tax_id_label, default_tax_rate,
   data_residency_note, auto_enabled_features, updated_at)
VALUES
 ('US','United States','USD','en-US','ltr','gregorian','standard','EIN / Tax ID',NULL,NULL,'[]'::jsonb,NOW()),
 ('CA','Canada','CAD','en-CA','ltr','gregorian','standard','GST/HST Number',0.0500,NULL,'[]'::jsonb,NOW()),
 ('GB','United Kingdom','GBP','en-GB','ltr','gregorian','standard','VAT Number',0.2000,NULL,'[]'::jsonb,NOW()),
 ('IE','Ireland','EUR','en-IE','ltr','gregorian','standard','VAT Number',0.2300,'EU data residency (GDPR).','[]'::jsonb,NOW()),
 ('DE','Germany','EUR','de-DE','ltr','gregorian','standard','USt-IdNr.',0.1900,'EU data residency (GDPR).','[]'::jsonb,NOW()),
 ('FR','France','EUR','fr-FR','ltr','gregorian','standard','TVA Number',0.2000,'EU data residency (GDPR).','[]'::jsonb,NOW()),
 ('NL','Netherlands','EUR','nl-NL','ltr','gregorian','standard','BTW Number',0.2100,'EU data residency (GDPR).','[]'::jsonb,NOW()),
 ('ES','Spain','EUR','es-ES','ltr','gregorian','standard','NIF / VAT',0.2100,'EU data residency (GDPR).','[]'::jsonb,NOW()),
 ('IT','Italy','EUR','it-IT','ltr','gregorian','standard','Partita IVA',0.2200,'EU data residency (GDPR).','[]'::jsonb,NOW()),
 ('AU','Australia','AUD','en-AU','ltr','gregorian','standard','ABN',0.1000,NULL,'[]'::jsonb,NOW()),
 ('NZ','New Zealand','NZD','en-NZ','ltr','gregorian','standard','GST Number',0.1500,NULL,'[]'::jsonb,NOW()),
 ('SG','Singapore','SGD','en-SG','ltr','gregorian','standard','GST Reg. No.',0.0900,NULL,'[]'::jsonb,NOW()),
 ('IN','India','INR','en-IN','ltr','gregorian','standard','GSTIN',0.1800,NULL,'[]'::jsonb,NOW()),
 ('JP','Japan','JPY','ja-JP','ltr','gregorian','standard','Corporate Number',0.1000,NULL,'[]'::jsonb,NOW()),
 ('BR','Brazil','BRL','pt-BR','ltr','gregorian','standard','CNPJ',0.1700,'Brazil data residency (LGPD).','[]'::jsonb,NOW()),
 ('MX','Mexico','MXN','es-MX','ltr','gregorian','standard','RFC',0.1600,NULL,'[]'::jsonb,NOW()),
 ('ZA','South Africa','ZAR','en-ZA','ltr','gregorian','standard','VAT Number',0.1500,NULL,'[]'::jsonb,NOW()),
 ('SA','Saudi Arabia','SAR','ar-SA','rtl','gregorian_hijri_dual','zatca_phase2','VAT Number',0.1500,'KSA data residency expected for regulated invoicing (ZATCA).','["zatca_invoicing","hijri_calendar_toggle","arabic_rtl"]'::jsonb,NOW()),
 ('AE','United Arab Emirates','AED','ar-AE','rtl','gregorian','standard','TRN',0.0500,NULL,'["arabic_rtl"]'::jsonb,NOW()),
 ('QA','Qatar','QAR','ar-QA','rtl','gregorian','standard','Tax Card No.',NULL,NULL,'["arabic_rtl"]'::jsonb,NOW()),
 ('KW','Kuwait','KWD','ar-KW','rtl','gregorian','standard','Tax No.',NULL,NULL,'["arabic_rtl"]'::jsonb,NOW()),
 ('BH','Bahrain','BHD','ar-BH','rtl','gregorian','standard','VAT Number',0.1000,NULL,'["arabic_rtl"]'::jsonb,NOW()),
 ('OM','Oman','OMR','ar-OM','rtl','gregorian','standard','VAT Number',0.0500,NULL,'["arabic_rtl"]'::jsonb,NOW()),
 ('EG','Egypt','EGP','ar-EG','rtl','gregorian','standard','Tax Reg. No.',0.1400,NULL,'["arabic_rtl"]'::jsonb,NOW())
ON CONFLICT (country_code) DO UPDATE SET
  country_name          = EXCLUDED.country_name,
  default_currency      = EXCLUDED.default_currency,
  default_locale        = EXCLUDED.default_locale,
  text_direction        = EXCLUDED.text_direction,
  calendar_system       = EXCLUDED.calendar_system,
  invoicing_scheme      = EXCLUDED.invoicing_scheme,
  tax_id_label          = EXCLUDED.tax_id_label,
  default_tax_rate      = EXCLUDED.default_tax_rate,
  data_residency_note   = EXCLUDED.data_residency_note,
  auto_enabled_features = EXCLUDED.auto_enabled_features,
  updated_at            = NOW();
