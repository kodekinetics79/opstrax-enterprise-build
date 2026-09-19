-- Stage 142 — canonical tenant operating-market isolation
-- companies.country is the only market authority. Tenant locale, regional
-- compliance packs and tax configuration cannot cross that boundary.

BEGIN;

DELETE FROM tenant_locale_settings older
USING tenant_locale_settings newer
WHERE older.tenant_id IS NOT NULL
  AND older.tenant_id = newer.tenant_id
  AND (COALESCE(older.updated_at, older.created_at), older.id)
      < (COALESCE(newer.updated_at, newer.created_at), newer.id);

CREATE UNIQUE INDEX IF NOT EXISTS uq_tenant_locale_settings_tenant
  ON tenant_locale_settings(tenant_id);

CREATE TABLE IF NOT EXISTS market_pack_countries (
  pack_code VARCHAR(80) NOT NULL REFERENCES market_packs(code) ON DELETE CASCADE,
  country_code VARCHAR(2) NOT NULL,
  PRIMARY KEY (pack_code, country_code)
);

INSERT INTO market_pack_countries(pack_code,country_code) VALUES
  ('canada_na','US'),('canada_na','CA'),
  ('saudi_gcc','SA')
ON CONFLICT DO NOTHING;

-- Reconcile existing locale rows before installing the fail-closed trigger.
UPDATE tenant_locale_settings tls
   SET default_country=UPPER(c.country),
       currency=p.default_currency,
       timezone=CASE UPPER(c.country)
         WHEN 'SA' THEN 'Asia/Riyadh' WHEN 'CA' THEN 'America/Toronto'
         WHEN 'US' THEN 'America/New_York' ELSE tls.timezone END,
       distance_unit=CASE WHEN UPPER(c.country)='US' THEN 'Miles' ELSE 'Kilometers' END,
       volume_unit=CASE WHEN UPPER(c.country)='US' THEN 'Gallons' ELSE 'Liters' END,
       updated_at=NOW()
  FROM companies c
  JOIN country_profiles p ON p.country_code=UPPER(c.country)
 WHERE tls.tenant_id=c.id AND UPPER(c.country) IN ('SA','CA','US');

-- A stale assignment from an earlier country must not remain active.
UPDATE tenant_market_packs tmp
   SET status='disabled', updated_at=NOW()
  FROM companies c
 WHERE tmp.company_id=c.id AND LOWER(tmp.status)='active'
   AND NOT EXISTS (
     SELECT 1 FROM market_pack_countries mpc
      WHERE mpc.pack_code=tmp.pack_code AND mpc.country_code=UPPER(c.country));

CREATE OR REPLACE FUNCTION enforce_tenant_locale_market()
RETURNS TRIGGER LANGUAGE plpgsql AS $$
DECLARE
  v_country TEXT;
  v_currency TEXT;
  v_timezone TEXT;
  v_distance TEXT;
  v_volume TEXT;
BEGIN
  SELECT UPPER(c.country), p.default_currency,
         CASE UPPER(c.country)
           WHEN 'SA' THEN 'Asia/Riyadh' WHEN 'AE' THEN 'Asia/Dubai'
           WHEN 'QA' THEN 'Asia/Qatar' WHEN 'KW' THEN 'Asia/Kuwait'
           WHEN 'BH' THEN 'Asia/Bahrain' WHEN 'OM' THEN 'Asia/Muscat'
           WHEN 'CA' THEN 'America/Toronto' WHEN 'US' THEN 'America/New_York'
           ELSE 'UTC' END,
         CASE WHEN UPPER(c.country)='US' THEN 'Miles' ELSE 'Kilometers' END,
         CASE WHEN UPPER(c.country)='US' THEN 'Gallons' ELSE 'Liters' END
    INTO v_country,v_currency,v_timezone,v_distance,v_volume
    FROM companies c
    JOIN country_profiles p ON p.country_code=UPPER(c.country)
   WHERE c.id=NEW.tenant_id;

  IF v_country IS NULL THEN
    RAISE EXCEPTION 'tenant operating market is not assigned' USING ERRCODE='23514';
  END IF;
  IF UPPER(NEW.default_country)<>v_country OR UPPER(NEW.currency)<>UPPER(v_currency)
     OR NEW.timezone<>v_timezone OR NEW.distance_unit<>v_distance OR NEW.volume_unit<>v_volume THEN
    RAISE EXCEPTION 'locale regulatory fields must match tenant operating market %',v_country USING ERRCODE='23514';
  END IF;
  RETURN NEW;
END $$;

DROP TRIGGER IF EXISTS trg_tenant_locale_market ON tenant_locale_settings;
CREATE TRIGGER trg_tenant_locale_market
BEFORE INSERT OR UPDATE OF tenant_id,default_country,timezone,currency,distance_unit,volume_unit
ON tenant_locale_settings FOR EACH ROW EXECUTE FUNCTION enforce_tenant_locale_market();

CREATE OR REPLACE FUNCTION enforce_tenant_market_pack()
RETURNS TRIGGER LANGUAGE plpgsql AS $$
DECLARE v_country TEXT;
BEGIN
  IF LOWER(NEW.status)<>'active' THEN RETURN NEW; END IF;
  SELECT UPPER(country) INTO v_country FROM companies WHERE id=NEW.company_id;
  IF v_country IS NULL OR NOT EXISTS (
    SELECT 1 FROM market_pack_countries
     WHERE pack_code=NEW.pack_code AND country_code=v_country
  ) THEN
    RAISE EXCEPTION 'market pack % is incompatible with tenant country %',NEW.pack_code,COALESCE(v_country,'unassigned') USING ERRCODE='23514';
  END IF;
  RETURN NEW;
END $$;

DROP TRIGGER IF EXISTS trg_tenant_market_pack ON tenant_market_packs;
CREATE TRIGGER trg_tenant_market_pack
BEFORE INSERT OR UPDATE OF company_id,pack_code,status
ON tenant_market_packs FOR EACH ROW EXECUTE FUNCTION enforce_tenant_market_pack();

CREATE OR REPLACE FUNCTION enforce_compliance_record_market()
RETURNS TRIGGER LANGUAGE plpgsql AS $$
DECLARE v_country TEXT;
BEGIN
  SELECT UPPER(country) INTO v_country FROM companies WHERE id=NEW.company_id;
  IF v_country IS NULL OR NOT EXISTS (
    SELECT 1 FROM market_pack_countries
     WHERE pack_code=NEW.pack_code AND country_code=v_country
  ) THEN
    RAISE EXCEPTION 'compliance pack % is incompatible with tenant country %',NEW.pack_code,COALESCE(v_country,'unassigned') USING ERRCODE='23514';
  END IF;
  RETURN NEW;
END $$;

DROP TRIGGER IF EXISTS trg_compliance_record_market ON compliance_records;
CREATE TRIGGER trg_compliance_record_market
BEFORE INSERT OR UPDATE OF company_id,pack_code
ON compliance_records FOR EACH ROW EXECUTE FUNCTION enforce_compliance_record_market();

-- Country-bearing tenant records always inherit the company operating country.
UPDATE branches b SET country_code=UPPER(c.country), timezone=tls.timezone, updated_at=NOW()
  FROM companies c
  JOIN tenant_locale_settings tls ON tls.tenant_id=c.id
 WHERE b.company_id=c.id AND UPPER(c.country) IN ('SA','CA','US')
   AND (UPPER(COALESCE(b.country_code,''))<>UPPER(c.country) OR b.timezone<>tls.timezone);
UPDATE dvir_templates d SET country_code=UPPER(c.country)
  FROM companies c WHERE d.company_id=c.id AND UPPER(c.country) IN ('SA','CA','US')
   AND UPPER(COALESCE(d.country_code,''))<>UPPER(c.country);
UPDATE dvir_reports d SET country_code=UPPER(c.country)
  FROM companies c WHERE d.company_id=c.id AND UPPER(c.country) IN ('SA','CA','US')
   AND UPPER(COALESCE(d.country_code,''))<>UPPER(c.country);

CREATE OR REPLACE FUNCTION enforce_tenant_record_country()
RETURNS TRIGGER LANGUAGE plpgsql AS $$
DECLARE v_country TEXT;
BEGIN
  SELECT UPPER(country) INTO v_country FROM companies WHERE id=NEW.company_id;
  IF v_country IS NULL OR UPPER(COALESCE(NEW.country_code,''))<>v_country THEN
    RAISE EXCEPTION 'record country must match tenant operating country %',COALESCE(v_country,'unassigned') USING ERRCODE='23514';
  END IF;
  RETURN NEW;
END $$;

DROP TRIGGER IF EXISTS trg_branch_market_country ON branches;
CREATE TRIGGER trg_branch_market_country BEFORE INSERT OR UPDATE OF company_id,country_code
ON branches FOR EACH ROW EXECUTE FUNCTION enforce_tenant_record_country();

DROP TRIGGER IF EXISTS trg_dvir_template_market_country ON dvir_templates;
CREATE TRIGGER trg_dvir_template_market_country BEFORE INSERT OR UPDATE OF company_id,country_code
ON dvir_templates FOR EACH ROW EXECUTE FUNCTION enforce_tenant_record_country();

DROP TRIGGER IF EXISTS trg_dvir_report_market_country ON dvir_reports;
CREATE TRIGGER trg_dvir_report_market_country BEFORE INSERT OR UPDATE OF company_id,country_code
ON dvir_reports FOR EACH ROW EXECUTE FUNCTION enforce_tenant_record_country();

-- Direct SQL country changes must complete the same cascade as the platform API.
-- Deferred evaluation permits the atomic service to update the company first and
-- its dependent rows afterward, while rejecting an incomplete transaction.
CREATE OR REPLACE FUNCTION enforce_company_market_cascade()
RETURNS TRIGGER LANGUAGE plpgsql AS $$
DECLARE v_country TEXT := UPPER(NEW.country); v_pack TEXT;
BEGIN
  v_pack := CASE v_country WHEN 'SA' THEN 'saudi_gcc' WHEN 'CA' THEN 'canada_na' WHEN 'US' THEN 'canada_na' ELSE NULL END;
  IF v_pack IS NULL THEN
    RAISE EXCEPTION 'tenant country % does not have an approved operating-market pack',v_country USING ERRCODE='23514';
  END IF;
  IF NOT EXISTS (
    SELECT 1 FROM tenant_locale_settings tls
     JOIN country_profiles cp ON cp.country_code=v_country
      WHERE tls.tenant_id=NEW.id AND UPPER(tls.default_country)=v_country
        AND UPPER(tls.currency)=UPPER(cp.default_currency)
        AND tls.timezone=CASE v_country WHEN 'SA' THEN 'Asia/Riyadh' WHEN 'CA' THEN 'America/Toronto' ELSE 'America/New_York' END
        AND tls.distance_unit=CASE WHEN v_country='US' THEN 'Miles' ELSE 'Kilometers' END
        AND tls.volume_unit=CASE WHEN v_country='US' THEN 'Gallons' ELSE 'Liters' END
  ) THEN RAISE EXCEPTION 'tenant market cascade is incomplete for country %',v_country USING ERRCODE='23514';
  END IF;
  IF NOT EXISTS (SELECT 1 FROM tenant_market_packs WHERE company_id=NEW.id AND pack_code=v_pack AND LOWER(status)='active')
     OR EXISTS (SELECT 1 FROM tenant_market_packs WHERE company_id=NEW.id AND pack_code<>v_pack AND LOWER(status)='active') THEN
    RAISE EXCEPTION 'tenant market-pack cascade is incomplete for country %',v_country USING ERRCODE='23514';
  END IF;
  IF EXISTS (SELECT 1 FROM branches WHERE company_id=NEW.id AND deleted_at IS NULL AND UPPER(COALESCE(country_code,''))<>v_country)
     OR EXISTS (SELECT 1 FROM dvir_templates WHERE company_id=NEW.id AND UPPER(COALESCE(country_code,''))<>v_country)
     OR EXISTS (SELECT 1 FROM dvir_reports WHERE company_id=NEW.id AND UPPER(COALESCE(country_code,''))<>v_country) THEN
    RAISE EXCEPTION 'tenant country-bearing records were not cascaded to %',v_country USING ERRCODE='23514';
  END IF;
  RETURN NEW;
END $$;

DROP TRIGGER IF EXISTS trg_company_market_cascade ON companies;
CREATE CONSTRAINT TRIGGER trg_company_market_cascade
AFTER UPDATE OF country ON companies DEFERRABLE INITIALLY DEFERRED
FOR EACH ROW WHEN (OLD.country IS DISTINCT FROM NEW.country)
EXECUTE FUNCTION enforce_company_market_cascade();

CREATE OR REPLACE FUNCTION expected_tax_regime(p_company_id BIGINT)
RETURNS TEXT LANGUAGE sql STABLE AS $$
  SELECT CASE UPPER(country) WHEN 'SA' THEN 'zatca_vat' WHEN 'CA' THEN 'gst'
         WHEN 'US' THEN 'us_sales_tax' ELSE NULL END
    FROM companies WHERE id=p_company_id
$$;

CREATE OR REPLACE FUNCTION enforce_tax_profile_market()
RETURNS TRIGGER LANGUAGE plpgsql AS $$
DECLARE v_country TEXT; v_regime TEXT;
BEGIN
  SELECT UPPER(country) INTO v_country FROM companies WHERE id=NEW.company_id;
  v_regime := expected_tax_regime(NEW.company_id);
  IF v_country IS NULL OR v_regime IS NULL OR LOWER(NEW.regime)<>v_regime THEN
    RAISE EXCEPTION 'tax regime % is incompatible with tenant country %',NEW.regime,COALESCE(v_country,'unassigned') USING ERRCODE='23514';
  END IF;
  RETURN NEW;
END $$;

CREATE OR REPLACE FUNCTION enforce_seller_tax_market()
RETURNS TRIGGER LANGUAGE plpgsql AS $$
DECLARE v_country TEXT; v_regime TEXT;
BEGIN
  SELECT UPPER(country) INTO v_country FROM companies WHERE id=NEW.company_id;
  v_regime := expected_tax_regime(NEW.company_id);
  IF v_country IS NULL OR v_regime IS NULL OR LOWER(NEW.regime)<>v_regime THEN
    RAISE EXCEPTION 'tax regime % is incompatible with tenant country %',NEW.regime,COALESCE(v_country,'unassigned') USING ERRCODE='23514';
  END IF;
  IF UPPER(NEW.jurisdiction)<>v_country THEN
    RAISE EXCEPTION 'seller jurisdiction must match tenant country %',v_country USING ERRCODE='23514';
  END IF;
  RETURN NEW;
END $$;

DROP TRIGGER IF EXISTS trg_tax_profile_market ON tax_profiles;
CREATE TRIGGER trg_tax_profile_market BEFORE INSERT OR UPDATE OF company_id,regime
ON tax_profiles FOR EACH ROW EXECUTE FUNCTION enforce_tax_profile_market();

DROP TRIGGER IF EXISTS trg_seller_tax_market ON seller_tax_registration;
CREATE TRIGGER trg_seller_tax_market BEFORE INSERT OR UPDATE OF company_id,jurisdiction,regime
ON seller_tax_registration FOR EACH ROW EXECUTE FUNCTION enforce_seller_tax_market();

INSERT INTO schema_migrations(version,description)
VALUES ('2026_09_15_stage142_tenant_market_isolation','Canonical tenant operating-market isolation')
ON CONFLICT(version) DO NOTHING;

COMMIT;
