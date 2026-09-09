-- Stage 132 -- non-forgeable authenticated-principal authority and private-row RLS
--
-- Stage58 binds tenant authority to an app backend PID, transaction id and expiry.
-- Tenant authority alone is deliberately insufficient for personal/session/recipient
-- records. This terminal overlay adds an active user id to the signed capability and
-- replaces the affected tables' generic tenant policies with exact command policies.
-- A tenant-only v1 ticket remains valid for shared operational tables, but returns NULL
-- from current_user_id() and therefore has zero authority over every private app policy.

BEGIN;

DO $preflight$
DECLARE missing text[];
BEGIN
  IF to_regprocedure('opstrax_security.issue_tenant_ticket(bigint,integer,bigint,integer)') IS NULL
     OR to_regprocedure('opstrax_security.current_tenant_id()') IS NULL
     OR to_regclass('opstrax_security.tenant_ticket_key') IS NULL THEN
    RAISE EXCEPTION 'Stage132 requires the Stage58 non-forgeable tenant authority';
  END IF;

  SELECT array_agg(name ORDER BY name) INTO missing
  FROM (VALUES
    ('mobile_device_tokens'),('user_notification_prefs'),('password_reset_tokens'),
    ('user_mfa_status'),('user_locale_preferences'),('telemetry_stream_ticket_nonces'),
    ('user_sessions'),('mfa_login_challenge_consumptions'),('notification_recipients'),
    ('alert_notification_deliveries'),('report_execution_log'),('saved_reports'),
    ('scheduled_reports'),('messaging_conversations'),('messaging_messages'),('coaching_notes')
  ) required(name)
  WHERE to_regclass('public.'||name) IS NULL;
  IF cardinality(missing)>0 THEN
    RAISE EXCEPTION 'Stage132 private-table prerequisites missing: %',missing;
  END IF;
END
$preflight$;

CREATE OR REPLACE FUNCTION opstrax_security.issue_principal_ticket(
  p_tenant_id bigint,
  p_user_id bigint,
  p_backend_pid integer,
  p_txid bigint,
  p_ttl_seconds integer DEFAULT 120
) RETURNS text
LANGUAGE plpgsql
VOLATILE
SECURITY DEFINER
SET search_path = pg_catalog, opstrax_security, public
AS $function$
DECLARE
  issued_nonce text;
  expires_epoch bigint;
  payload text;
  signature text;
  secret bytea;
BEGIN
  IF session_user <> 'opstrax_system' THEN
    RAISE EXCEPTION 'principal ticket issuance requires opstrax_system' USING ERRCODE='42501';
  END IF;
  IF p_tenant_id IS NULL OR p_tenant_id<=0 OR p_user_id IS NULL OR p_user_id<=0
     OR p_backend_pid IS NULL OR p_backend_pid<=0 OR p_txid IS NULL OR p_txid<=0
     OR p_ttl_seconds IS NULL OR p_ttl_seconds<5 OR p_ttl_seconds>300 THEN
    RAISE EXCEPTION 'invalid principal ticket binding' USING ERRCODE='22023';
  END IF;
  IF NOT EXISTS (
    SELECT 1 FROM public.users u
    WHERE u.id=p_user_id AND u.company_id=p_tenant_id AND lower(coalesce(u.status,''))='active'
  ) THEN
    RAISE EXCEPTION 'principal is not an active member of the tenant' USING ERRCODE='42501';
  END IF;

  SELECT key_material INTO STRICT secret
  FROM opstrax_security.tenant_ticket_key WHERE singleton;
  issued_nonce := encode(public.gen_random_bytes(16),'hex');
  expires_epoch := extract(epoch FROM clock_timestamp())::bigint+p_ttl_seconds;
  payload := concat_ws(':','v2',p_tenant_id,p_user_id,p_backend_pid,p_txid,expires_epoch,issued_nonce);
  signature := encode(public.hmac(convert_to(payload,'UTF8'),secret,'sha256'),'hex');
  RETURN payload||':'||signature;
END
$function$;

-- Preserve v1 tenant-only tickets for background tenant work and accept v2 for HTTP
-- request scopes. Both formats retain the Stage58 PID/transaction/expiry/HMAC checks.
CREATE OR REPLACE FUNCTION opstrax_security.current_tenant_id()
RETURNS bigint
LANGUAGE plpgsql
STABLE
SECURITY DEFINER
SET search_path = pg_catalog, opstrax_security
AS $function$
DECLARE
  ticket text;
  fields text[];
  payload text;
  secret bytea;
  tenant_index integer := 2;
  pid_index integer;
  txid_index integer;
  expiry_index integer;
  nonce_index integer;
  signature_index integer;
  ticket_tenant bigint;
  ticket_pid integer;
  ticket_txid bigint;
  expires_epoch bigint;
  supplied_signature bytea;
  expected_signature bytea;
BEGIN
  IF session_user <> 'opstrax_app' THEN RETURN NULL; END IF;
  ticket := NULLIF(current_setting('app.tenant_ticket',true),'');
  IF ticket IS NULL OR length(ticket)>384 THEN RETURN NULL; END IF;
  fields := string_to_array(ticket,':');

  IF cardinality(fields)=7 AND fields[1]='v1' THEN
    pid_index:=3; txid_index:=4; expiry_index:=5; nonce_index:=6; signature_index:=7;
  ELSIF cardinality(fields)=8 AND fields[1]='v2'
        AND fields[3]~'^[1-9][0-9]{0,18}$' THEN
    pid_index:=4; txid_index:=5; expiry_index:=6; nonce_index:=7; signature_index:=8;
  ELSE RETURN NULL;
  END IF;

  IF fields[tenant_index]!~'^[1-9][0-9]{0,18}$'
     OR fields[pid_index]!~'^[1-9][0-9]{0,9}$'
     OR fields[txid_index]!~'^[1-9][0-9]{0,18}$'
     OR fields[expiry_index]!~'^[1-9][0-9]{0,18}$'
     OR fields[nonce_index]!~'^[0-9a-f]{32}$'
     OR fields[signature_index]!~'^[0-9a-f]{64}$' THEN RETURN NULL; END IF;

  ticket_tenant:=fields[tenant_index]::bigint;
  ticket_pid:=fields[pid_index]::integer;
  ticket_txid:=fields[txid_index]::bigint;
  expires_epoch:=fields[expiry_index]::bigint;
  IF ticket_pid<>pg_backend_pid() OR ticket_txid<>txid_current()::bigint
     OR expires_epoch<extract(epoch FROM statement_timestamp())::bigint THEN RETURN NULL; END IF;

  SELECT key_material INTO STRICT secret FROM opstrax_security.tenant_ticket_key WHERE singleton;
  payload:=array_to_string(fields[1:nonce_index],':');
  supplied_signature:=decode(fields[signature_index],'hex');
  expected_signature:=public.hmac(convert_to(payload,'UTF8'),secret,'sha256');
  IF supplied_signature<>expected_signature THEN RETURN NULL; END IF;
  RETURN ticket_tenant;
EXCEPTION WHEN OTHERS THEN RETURN NULL;
END
$function$;

CREATE OR REPLACE FUNCTION opstrax_security.current_user_id()
RETURNS bigint
LANGUAGE plpgsql
STABLE
SECURITY DEFINER
SET search_path = pg_catalog, opstrax_security
AS $function$
DECLARE
  ticket text;
  fields text[];
  payload text;
  secret bytea;
  ticket_tenant bigint;
  ticket_user bigint;
  ticket_pid integer;
  ticket_txid bigint;
  expires_epoch bigint;
BEGIN
  IF session_user <> 'opstrax_app' THEN RETURN NULL; END IF;
  ticket:=NULLIF(current_setting('app.tenant_ticket',true),'');
  IF ticket IS NULL OR length(ticket)>384 THEN RETURN NULL; END IF;
  fields:=string_to_array(ticket,':');
  IF cardinality(fields)<>8 OR fields[1]<>'v2'
     OR fields[2]!~'^[1-9][0-9]{0,18}$' OR fields[3]!~'^[1-9][0-9]{0,18}$'
     OR fields[4]!~'^[1-9][0-9]{0,9}$' OR fields[5]!~'^[1-9][0-9]{0,18}$'
     OR fields[6]!~'^[1-9][0-9]{0,18}$' OR fields[7]!~'^[0-9a-f]{32}$'
     OR fields[8]!~'^[0-9a-f]{64}$' THEN RETURN NULL; END IF;

  ticket_tenant:=fields[2]::bigint; ticket_user:=fields[3]::bigint;
  ticket_pid:=fields[4]::integer; ticket_txid:=fields[5]::bigint;
  expires_epoch:=fields[6]::bigint;
  IF ticket_pid<>pg_backend_pid() OR ticket_txid<>txid_current()::bigint
     OR expires_epoch<extract(epoch FROM statement_timestamp())::bigint THEN RETURN NULL; END IF;
  SELECT key_material INTO STRICT secret FROM opstrax_security.tenant_ticket_key WHERE singleton;
  payload:=array_to_string(fields[1:7],':');
  IF decode(fields[8],'hex')<>public.hmac(convert_to(payload,'UTF8'),secret,'sha256') THEN RETURN NULL; END IF;
  IF ticket_tenant IS DISTINCT FROM opstrax_security.current_tenant_id() THEN RETURN NULL; END IF;
  IF NOT EXISTS (
    SELECT 1 FROM public.users u
    WHERE u.id=ticket_user AND u.company_id=ticket_tenant
      AND lower(coalesce(u.status,''))='active'
  ) THEN RETURN NULL; END IF;
  RETURN ticket_user;
EXCEPTION WHEN OTHERS THEN RETURN NULL;
END
$function$;

-- Invoker helpers do not bypass table RLS. They derive the caller from current_user_id()
-- and resolve only persisted role/user grants; a request body or custom GUC cannot add one.
CREATE OR REPLACE FUNCTION opstrax_security.current_principal_has_permission(p_permission text)
RETURNS boolean
LANGUAGE sql
STABLE
SECURITY INVOKER
SET search_path = pg_catalog, public, opstrax_security
AS $function$
SELECT COALESCE(EXISTS (
  SELECT 1
  FROM public.users u
  LEFT JOIN public.roles r ON r.id=u.role_id AND (r.company_id IS NULL OR r.company_id=u.company_id)
  WHERE u.id=opstrax_security.current_user_id()
    AND u.company_id=opstrax_security.current_tenant_id()
    AND lower(coalesce(u.status,''))='active'
    AND (
      (COALESCE(u.role_id,0)>0 AND (
        COALESCE(r.permissions_json,'[]'::jsonb) ? p_permission
        OR COALESCE(r.permissions_json,'[]'::jsonb) ? '*'
        OR EXISTS (SELECT 1 FROM public.role_permissions rp
                   WHERE rp.role_id=u.role_id AND rp.permission_key IN (p_permission,'*'))
      ))
      OR (COALESCE(u.role_id,0)=0 AND (
        COALESCE(u.permissions_json,'[]'::jsonb) ? p_permission
        OR COALESCE(u.permissions_json,'[]'::jsonb) ? '*'
      ))
    )
),false)
$function$;

CREATE OR REPLACE FUNCTION opstrax_security.current_principal_has_role(p_role text)
RETURNS boolean
LANGUAGE sql
STABLE
SECURITY INVOKER
SET search_path = pg_catalog, public, opstrax_security
AS $function$
SELECT COALESCE(EXISTS (
  SELECT 1 FROM public.users u
  WHERE u.id=opstrax_security.current_user_id()
    AND u.company_id=opstrax_security.current_tenant_id()
    AND lower(coalesce(u.status,''))='active'
    AND lower(trim(u.role_name))=lower(trim(p_role))
),false)
$function$;

REVOKE ALL ON FUNCTION opstrax_security.issue_principal_ticket(bigint,bigint,integer,bigint,integer) FROM PUBLIC,opstrax_app,opstrax_system;
GRANT EXECUTE ON FUNCTION opstrax_security.issue_principal_ticket(bigint,bigint,integer,bigint,integer) TO opstrax_system;
REVOKE ALL ON FUNCTION opstrax_security.current_user_id() FROM PUBLIC,opstrax_app,opstrax_system;
GRANT EXECUTE ON FUNCTION opstrax_security.current_user_id() TO opstrax_app;
REVOKE ALL ON FUNCTION opstrax_security.current_principal_has_permission(text) FROM PUBLIC,opstrax_app,opstrax_system;
GRANT EXECUTE ON FUNCTION opstrax_security.current_principal_has_permission(text) TO opstrax_app;
REVOKE ALL ON FUNCTION opstrax_security.current_principal_has_role(text) FROM PUBLIC,opstrax_app,opstrax_system;
GRANT EXECUTE ON FUNCTION opstrax_security.current_principal_has_role(text) TO opstrax_app;

-- Reassert the Stage58 special-table contract. Older migration-parity suites and
-- interrupted deployments can otherwise leave superseded PUBLIC/GUC policies in
-- place before this terminal overlay runs.
DO $reset_special_policies$
DECLARE rec record;
BEGIN
  FOR rec IN
    SELECT p.tablename,p.policyname FROM pg_policies p
    WHERE p.schemaname='public' AND p.tablename IN ('roles','report_catalog','role_permissions')
  LOOP EXECUTE format('DROP POLICY %I ON public.%I',rec.policyname,rec.tablename); END LOOP;

  ALTER TABLE roles ENABLE ROW LEVEL SECURITY; ALTER TABLE roles FORCE ROW LEVEL SECURITY;
  ALTER TABLE report_catalog ENABLE ROW LEVEL SECURITY; ALTER TABLE report_catalog FORCE ROW LEVEL SECURITY;
  ALTER TABLE role_permissions ENABLE ROW LEVEL SECURITY; ALTER TABLE role_permissions FORCE ROW LEVEL SECURITY;

  CREATE POLICY roles_app_select ON roles FOR SELECT TO opstrax_app
    USING(company_id IS NULL OR company_id=(SELECT opstrax_security.current_tenant_id()));
  CREATE POLICY roles_app_insert ON roles FOR INSERT TO opstrax_app
    WITH CHECK(company_id IS NOT NULL AND company_id=(SELECT opstrax_security.current_tenant_id()));
  CREATE POLICY roles_app_update ON roles FOR UPDATE TO opstrax_app
    USING(company_id IS NOT NULL AND company_id=(SELECT opstrax_security.current_tenant_id()))
    WITH CHECK(company_id IS NOT NULL AND company_id=(SELECT opstrax_security.current_tenant_id()));
  CREATE POLICY roles_app_delete ON roles FOR DELETE TO opstrax_app
    USING(company_id IS NOT NULL AND company_id=(SELECT opstrax_security.current_tenant_id()));
  CREATE POLICY system_control_plane ON roles FOR ALL TO opstrax_system USING(true) WITH CHECK(true);

  CREATE POLICY report_catalog_app_select ON report_catalog FOR SELECT TO opstrax_app
    USING(tenant_id IS NULL OR tenant_id=(SELECT opstrax_security.current_tenant_id()));
  CREATE POLICY report_catalog_app_insert ON report_catalog FOR INSERT TO opstrax_app
    WITH CHECK(tenant_id IS NOT NULL AND tenant_id=(SELECT opstrax_security.current_tenant_id()));
  CREATE POLICY report_catalog_app_update ON report_catalog FOR UPDATE TO opstrax_app
    USING(tenant_id IS NOT NULL AND tenant_id=(SELECT opstrax_security.current_tenant_id()))
    WITH CHECK(tenant_id IS NOT NULL AND tenant_id=(SELECT opstrax_security.current_tenant_id()));
  CREATE POLICY report_catalog_app_delete ON report_catalog FOR DELETE TO opstrax_app
    USING(tenant_id IS NOT NULL AND tenant_id=(SELECT opstrax_security.current_tenant_id()));
  CREATE POLICY system_control_plane ON report_catalog FOR ALL TO opstrax_system USING(true) WITH CHECK(true);

  CREATE POLICY role_permissions_app_select ON role_permissions FOR SELECT TO opstrax_app
    USING (EXISTS (SELECT 1 FROM roles r WHERE r.id=role_id
      AND (r.company_id IS NULL OR r.company_id=(SELECT opstrax_security.current_tenant_id()))));
  CREATE POLICY role_permissions_app_insert ON role_permissions FOR INSERT TO opstrax_app
    WITH CHECK (EXISTS (SELECT 1 FROM roles r WHERE r.id=role_id
      AND r.company_id=(SELECT opstrax_security.current_tenant_id())));
  CREATE POLICY role_permissions_app_update ON role_permissions FOR UPDATE TO opstrax_app
    USING (EXISTS (SELECT 1 FROM roles r WHERE r.id=role_id
      AND r.company_id=(SELECT opstrax_security.current_tenant_id())))
    WITH CHECK (EXISTS (SELECT 1 FROM roles r WHERE r.id=role_id
      AND r.company_id=(SELECT opstrax_security.current_tenant_id())));
  CREATE POLICY role_permissions_app_delete ON role_permissions FOR DELETE TO opstrax_app
    USING (EXISTS (SELECT 1 FROM roles r WHERE r.id=role_id
      AND r.company_id=(SELECT opstrax_security.current_tenant_id())));
  CREATE POLICY system_control_plane ON role_permissions FOR ALL TO opstrax_system USING(true) WITH CHECK(true);
END
$reset_special_policies$;

DO $reset_private_policies$
DECLARE rec record;
BEGIN
  FOR rec IN
    SELECT p.tablename,p.policyname FROM pg_policies p
    WHERE p.schemaname='public' AND p.tablename IN (
      'mobile_device_tokens','user_notification_prefs','password_reset_tokens',
      'user_mfa_status','user_locale_preferences','telemetry_stream_ticket_nonces',
      'user_sessions','mfa_login_challenge_consumptions','notification_recipients',
      'alert_notification_deliveries','report_execution_log','saved_reports',
      'scheduled_reports','messaging_conversations','messaging_messages','coaching_notes')
  LOOP EXECUTE format('DROP POLICY %I ON public.%I',rec.policyname,rec.tablename); END LOOP;

  FOR rec IN SELECT name FROM (VALUES
    ('mobile_device_tokens'),('user_notification_prefs'),('password_reset_tokens'),
    ('user_mfa_status'),('user_locale_preferences'),('telemetry_stream_ticket_nonces'),
    ('user_sessions'),('mfa_login_challenge_consumptions'),('notification_recipients'),
    ('alert_notification_deliveries'),('report_execution_log'),('saved_reports'),
    ('scheduled_reports'),('messaging_conversations'),('messaging_messages'),('coaching_notes')
  ) tables(name)
  LOOP
    EXECUTE format('ALTER TABLE public.%I ENABLE ROW LEVEL SECURITY',rec.name);
    EXECUTE format('ALTER TABLE public.%I FORCE ROW LEVEL SECURITY',rec.name);
    EXECUTE format('CREATE POLICY system_control_plane ON public.%I FOR ALL TO opstrax_system USING (true) WITH CHECK (true)',rec.name);
  END LOOP;
END
$reset_private_policies$;

-- Strict self-service records: owner and tenant are immutable authorization fields.
CREATE POLICY principal_app_select ON mobile_device_tokens FOR SELECT TO opstrax_app
  USING (company_id=(SELECT opstrax_security.current_tenant_id()) AND user_id=(SELECT opstrax_security.current_user_id()));
CREATE POLICY principal_app_insert ON mobile_device_tokens FOR INSERT TO opstrax_app
  WITH CHECK (company_id=(SELECT opstrax_security.current_tenant_id()) AND user_id=(SELECT opstrax_security.current_user_id()));
CREATE POLICY principal_app_update ON mobile_device_tokens FOR UPDATE TO opstrax_app
  USING (company_id=(SELECT opstrax_security.current_tenant_id()) AND user_id=(SELECT opstrax_security.current_user_id()))
  WITH CHECK (company_id=(SELECT opstrax_security.current_tenant_id()) AND user_id=(SELECT opstrax_security.current_user_id()));

CREATE POLICY principal_app_select ON user_notification_prefs FOR SELECT TO opstrax_app
  USING (company_id=(SELECT opstrax_security.current_tenant_id()) AND user_id=(SELECT opstrax_security.current_user_id()));
CREATE POLICY principal_app_insert ON user_notification_prefs FOR INSERT TO opstrax_app
  WITH CHECK (company_id=(SELECT opstrax_security.current_tenant_id()) AND user_id=(SELECT opstrax_security.current_user_id()));
CREATE POLICY principal_app_update ON user_notification_prefs FOR UPDATE TO opstrax_app
  USING (company_id=(SELECT opstrax_security.current_tenant_id()) AND user_id=(SELECT opstrax_security.current_user_id()))
  WITH CHECK (company_id=(SELECT opstrax_security.current_tenant_id()) AND user_id=(SELECT opstrax_security.current_user_id()));

CREATE POLICY principal_app_select ON user_locale_preferences FOR SELECT TO opstrax_app
  USING (user_id=(SELECT opstrax_security.current_user_id()) AND EXISTS (
    SELECT 1 FROM users u WHERE u.id=user_locale_preferences.user_id
      AND u.company_id=(SELECT opstrax_security.current_tenant_id())));
CREATE POLICY principal_app_insert ON user_locale_preferences FOR INSERT TO opstrax_app
  WITH CHECK (user_id=(SELECT opstrax_security.current_user_id()) AND EXISTS (
    SELECT 1 FROM users u WHERE u.id=user_locale_preferences.user_id
      AND u.company_id=(SELECT opstrax_security.current_tenant_id())));
CREATE POLICY principal_app_update ON user_locale_preferences FOR UPDATE TO opstrax_app
  USING (user_id=(SELECT opstrax_security.current_user_id()) AND EXISTS (
    SELECT 1 FROM users u WHERE u.id=user_locale_preferences.user_id
      AND u.company_id=(SELECT opstrax_security.current_tenant_id())))
  WITH CHECK (user_id=(SELECT opstrax_security.current_user_id()) AND EXISTS (
    SELECT 1 FROM users u WHERE u.id=user_locale_preferences.user_id
      AND u.company_id=(SELECT opstrax_security.current_tenant_id())));

-- MFA is self-service, with explicit persisted admin/security permissions for tenant
-- support screens. Pre-login verification remains on opstrax_system.
CREATE POLICY principal_app_select ON user_mfa_status FOR SELECT TO opstrax_app
  USING (EXISTS (SELECT 1 FROM users target WHERE target.id=user_mfa_status.user_id
    AND target.company_id=(SELECT opstrax_security.current_tenant_id())) AND
    (user_id=(SELECT opstrax_security.current_user_id())
     OR opstrax_security.current_principal_has_permission('users:view')
     OR opstrax_security.current_principal_has_permission('security:view')));
CREATE POLICY principal_app_insert ON user_mfa_status FOR INSERT TO opstrax_app
  WITH CHECK (EXISTS (SELECT 1 FROM users target WHERE target.id=user_mfa_status.user_id
    AND target.company_id=(SELECT opstrax_security.current_tenant_id())) AND
    (user_id=(SELECT opstrax_security.current_user_id())
     OR opstrax_security.current_principal_has_permission('users:update')
     OR opstrax_security.current_principal_has_permission('security:manage')));
CREATE POLICY principal_app_update ON user_mfa_status FOR UPDATE TO opstrax_app
  USING (EXISTS (SELECT 1 FROM users target WHERE target.id=user_mfa_status.user_id
    AND target.company_id=(SELECT opstrax_security.current_tenant_id())) AND
    (user_id=(SELECT opstrax_security.current_user_id())
     OR opstrax_security.current_principal_has_permission('users:update')
     OR opstrax_security.current_principal_has_permission('security:manage')))
  WITH CHECK (EXISTS (SELECT 1 FROM users target WHERE target.id=user_mfa_status.user_id
    AND target.company_id=(SELECT opstrax_security.current_tenant_id())) AND
    (user_id=(SELECT opstrax_security.current_user_id())
     OR opstrax_security.current_principal_has_permission('users:update')
     OR opstrax_security.current_principal_has_permission('security:manage')));

-- Session creation and pre-auth token/challenge processing are system-only. An active
-- principal can inspect/revoke itself; tenant admins get only the verbs their API uses.
CREATE POLICY principal_app_select ON user_sessions FOR SELECT TO opstrax_app
  USING (company_id=(SELECT opstrax_security.current_tenant_id()) AND
    (user_id=(SELECT opstrax_security.current_user_id())
     OR opstrax_security.current_principal_has_permission('users:view')));
CREATE POLICY principal_app_update ON user_sessions FOR UPDATE TO opstrax_app
  USING (company_id=(SELECT opstrax_security.current_tenant_id()) AND user_id=(SELECT opstrax_security.current_user_id()))
  WITH CHECK (company_id=(SELECT opstrax_security.current_tenant_id()) AND user_id=(SELECT opstrax_security.current_user_id()));
CREATE POLICY principal_app_delete ON user_sessions FOR DELETE TO opstrax_app
  USING (company_id=(SELECT opstrax_security.current_tenant_id()) AND
    (user_id=(SELECT opstrax_security.current_user_id())
     OR opstrax_security.current_principal_has_permission('users:update')));

CREATE POLICY principal_app_insert ON telemetry_stream_ticket_nonces FOR INSERT TO opstrax_app
  WITH CHECK (audit_company_id=(SELECT opstrax_security.current_tenant_id())
    AND user_id=(SELECT opstrax_security.current_user_id()));

-- Recipient state is private to its user. Dispatchers may inspect delivery/read evidence
-- for the operational broadcast surface; app insertion remains forbidden and is performed
-- by NotificationService through the separate system lane.
CREATE POLICY principal_app_select ON notification_recipients FOR SELECT TO opstrax_app
  USING (company_id=(SELECT opstrax_security.current_tenant_id()) AND (
    user_id=(SELECT opstrax_security.current_user_id())
    OR (user_id IS NULL AND role_target IS NOT NULL AND opstrax_security.current_principal_has_role(role_target))
    OR opstrax_security.current_principal_has_permission('notifications:manage')
    OR opstrax_security.current_principal_has_permission('dispatch:view')));
CREATE POLICY principal_app_update ON notification_recipients FOR UPDATE TO opstrax_app
  USING (company_id=(SELECT opstrax_security.current_tenant_id()) AND (
    user_id=(SELECT opstrax_security.current_user_id())
    OR (user_id IS NULL AND role_target IS NOT NULL AND opstrax_security.current_principal_has_role(role_target))
    OR opstrax_security.current_principal_has_permission('notifications:manage')))
  WITH CHECK (company_id=(SELECT opstrax_security.current_tenant_id()) AND (
    user_id=(SELECT opstrax_security.current_user_id())
    OR (user_id IS NULL AND role_target IS NOT NULL AND opstrax_security.current_principal_has_role(role_target))
    OR opstrax_security.current_principal_has_permission('notifications:manage')));

-- Saved/scheduled report definitions are private by default. Explicit shared visibility
-- and reports:export are the reviewed collaboration/admin exceptions.
CREATE POLICY principal_app_select ON saved_reports FOR SELECT TO opstrax_app
  USING (company_id=(SELECT opstrax_security.current_tenant_id()) AND (
    owner_user_id=(SELECT opstrax_security.current_user_id()) OR visibility='tenant_shared'
    OR (visibility='role_shared' AND shared_role IS NOT NULL AND opstrax_security.current_principal_has_role(shared_role))
    OR opstrax_security.current_principal_has_permission('reports:export')));
CREATE POLICY principal_app_insert ON saved_reports FOR INSERT TO opstrax_app
  WITH CHECK (company_id=(SELECT opstrax_security.current_tenant_id()) AND owner_user_id=(SELECT opstrax_security.current_user_id()));
CREATE POLICY principal_app_update ON saved_reports FOR UPDATE TO opstrax_app
  USING (company_id=(SELECT opstrax_security.current_tenant_id()) AND
    (owner_user_id=(SELECT opstrax_security.current_user_id()) OR opstrax_security.current_principal_has_permission('reports:export')))
  WITH CHECK (company_id=(SELECT opstrax_security.current_tenant_id()) AND
    (owner_user_id=(SELECT opstrax_security.current_user_id()) OR opstrax_security.current_principal_has_permission('reports:export')));

CREATE POLICY principal_app_select ON scheduled_reports FOR SELECT TO opstrax_app
  USING (tenant_id=(SELECT opstrax_security.current_tenant_id()) AND
    (owner_user_id=(SELECT opstrax_security.current_user_id()) OR opstrax_security.current_principal_has_permission('reports:export')));
CREATE POLICY principal_app_insert ON scheduled_reports FOR INSERT TO opstrax_app
  WITH CHECK (tenant_id=(SELECT opstrax_security.current_tenant_id())
    AND owner_user_id=(SELECT opstrax_security.current_user_id())
    AND created_by_user_id=(SELECT opstrax_security.current_user_id()));
CREATE POLICY principal_app_update ON scheduled_reports FOR UPDATE TO opstrax_app
  USING (tenant_id=(SELECT opstrax_security.current_tenant_id()) AND
    (owner_user_id=(SELECT opstrax_security.current_user_id()) OR opstrax_security.current_principal_has_permission('reports:export')))
  WITH CHECK (tenant_id=(SELECT opstrax_security.current_tenant_id()) AND
    (owner_user_id=(SELECT opstrax_security.current_user_id()) OR opstrax_security.current_principal_has_permission('reports:export')));

CREATE POLICY principal_app_select ON report_execution_log FOR SELECT TO opstrax_app
  USING (company_id=(SELECT opstrax_security.current_tenant_id()) AND
    (user_id=(SELECT opstrax_security.current_user_id()) OR opstrax_security.current_principal_has_permission('reports:export')));
CREATE POLICY principal_app_insert ON report_execution_log FOR INSERT TO opstrax_app
  WITH CHECK (company_id=(SELECT opstrax_security.current_tenant_id()) AND user_id=(SELECT opstrax_security.current_user_id()));

-- Driver conversations are individual to the bound driver and the creator. The
-- non-driver messages:send permission is the explicit dispatcher workspace exception.
CREATE POLICY principal_app_select ON messaging_conversations FOR SELECT TO opstrax_app
  USING (company_id=(SELECT opstrax_security.current_tenant_id()) AND (
    created_by=(SELECT opstrax_security.current_user_id())
    OR EXISTS (SELECT 1 FROM drivers d WHERE d.id=messaging_conversations.driver_id
      AND d.company_id=messaging_conversations.company_id AND d.user_id=(SELECT opstrax_security.current_user_id()))
    OR (NOT opstrax_security.current_principal_has_role('Driver')
      AND opstrax_security.current_principal_has_permission('messages:send'))));
CREATE POLICY principal_app_insert ON messaging_conversations FOR INSERT TO opstrax_app
  WITH CHECK (company_id=(SELECT opstrax_security.current_tenant_id())
    AND created_by=(SELECT opstrax_security.current_user_id()) AND (
      EXISTS (SELECT 1 FROM drivers d WHERE d.id=messaging_conversations.driver_id
        AND d.company_id=messaging_conversations.company_id AND d.user_id=(SELECT opstrax_security.current_user_id()))
      OR (NOT opstrax_security.current_principal_has_role('Driver')
        AND opstrax_security.current_principal_has_permission('messages:send'))));
CREATE POLICY principal_app_update ON messaging_conversations FOR UPDATE TO opstrax_app
  USING (company_id=(SELECT opstrax_security.current_tenant_id()) AND (
    created_by=(SELECT opstrax_security.current_user_id())
    OR EXISTS (SELECT 1 FROM drivers d WHERE d.id=messaging_conversations.driver_id
      AND d.company_id=messaging_conversations.company_id AND d.user_id=(SELECT opstrax_security.current_user_id()))
    OR (NOT opstrax_security.current_principal_has_role('Driver')
      AND opstrax_security.current_principal_has_permission('messages:send'))))
  WITH CHECK (company_id=(SELECT opstrax_security.current_tenant_id()) AND (
    created_by=(SELECT opstrax_security.current_user_id())
    OR EXISTS (SELECT 1 FROM drivers d WHERE d.id=messaging_conversations.driver_id
      AND d.company_id=messaging_conversations.company_id AND d.user_id=(SELECT opstrax_security.current_user_id()))
    OR (NOT opstrax_security.current_principal_has_role('Driver')
      AND opstrax_security.current_principal_has_permission('messages:send'))));

CREATE POLICY principal_app_select ON messaging_messages FOR SELECT TO opstrax_app
  USING (company_id=(SELECT opstrax_security.current_tenant_id()) AND EXISTS (
    SELECT 1 FROM messaging_conversations c WHERE c.id=messaging_messages.conversation_id
      AND c.company_id=messaging_messages.company_id));
CREATE POLICY principal_app_insert ON messaging_messages FOR INSERT TO opstrax_app
  WITH CHECK (company_id=(SELECT opstrax_security.current_tenant_id())
    AND sender_user_id=(SELECT opstrax_security.current_user_id()) AND EXISTS (
      SELECT 1 FROM messaging_conversations c WHERE c.id=messaging_messages.conversation_id
        AND c.company_id=messaging_messages.company_id));
CREATE POLICY principal_app_update ON messaging_messages FOR UPDATE TO opstrax_app
  USING (company_id=(SELECT opstrax_security.current_tenant_id()) AND EXISTS (
    SELECT 1 FROM messaging_conversations c WHERE c.id=messaging_messages.conversation_id
      AND c.company_id=messaging_messages.company_id))
  WITH CHECK (company_id=(SELECT opstrax_security.current_tenant_id()) AND EXISTS (
    SELECT 1 FROM messaging_conversations c WHERE c.id=messaging_messages.conversation_id
      AND c.company_id=messaging_messages.company_id));

-- Coaching notes are a safety-team record, not a personal notebook. The persisted
-- safety write permissions are therefore the explicit operational exception; author spoofing
-- remains impossible and the ledger is append-only to the app identity.
CREATE POLICY principal_app_select ON coaching_notes FOR SELECT TO opstrax_app
  USING (company_id=(SELECT opstrax_security.current_tenant_id())
    AND opstrax_security.current_principal_has_permission('safety:view'));
CREATE POLICY principal_app_insert ON coaching_notes FOR INSERT TO opstrax_app
  WITH CHECK (company_id=(SELECT opstrax_security.current_tenant_id())
    AND created_by_user_id=(SELECT opstrax_security.current_user_id())
    AND (opstrax_security.current_principal_has_permission('safety:review')
      OR opstrax_security.current_principal_has_permission('safety:update')
      OR opstrax_security.current_principal_has_permission('safety:manage')));

-- Exact table grants. Policies cannot widen a verb the role does not possess.
REVOKE ALL ON TABLE
  mobile_device_tokens,user_notification_prefs,password_reset_tokens,user_mfa_status,
  user_locale_preferences,telemetry_stream_ticket_nonces,user_sessions,
  mfa_login_challenge_consumptions,notification_recipients,alert_notification_deliveries,
  report_execution_log,saved_reports,scheduled_reports,messaging_conversations,
  messaging_messages,coaching_notes
FROM PUBLIC,opstrax_app,opstrax_system;

GRANT SELECT,INSERT,UPDATE ON mobile_device_tokens,user_notification_prefs,user_mfa_status,user_locale_preferences TO opstrax_app;
GRANT INSERT ON telemetry_stream_ticket_nonces TO opstrax_app;
GRANT SELECT,UPDATE,DELETE ON user_sessions TO opstrax_app;
GRANT SELECT,UPDATE ON notification_recipients TO opstrax_app;
GRANT SELECT,INSERT ON report_execution_log,coaching_notes TO opstrax_app;
GRANT SELECT,INSERT,UPDATE ON saved_reports,scheduled_reports,messaging_conversations,messaging_messages TO opstrax_app;

GRANT SELECT,INSERT,UPDATE,DELETE ON
  mobile_device_tokens,user_notification_prefs,password_reset_tokens,user_mfa_status,
  user_locale_preferences,telemetry_stream_ticket_nonces,user_sessions,
  mfa_login_challenge_consumptions,notification_recipients,alert_notification_deliveries,
  report_execution_log,saved_reports,scheduled_reports,messaging_conversations,
  messaging_messages,coaching_notes
TO opstrax_system;

DO $sequence_acl$
DECLARE rec record; allow_app boolean;
BEGIN
  FOR rec IN
    SELECT tbl.relname table_name,seq.oid::regclass::text sequence_name
    FROM pg_class tbl JOIN pg_namespace n ON n.oid=tbl.relnamespace AND n.nspname='public'
    JOIN pg_depend d ON d.refobjid=tbl.oid AND d.refobjsubid>0 AND d.deptype IN ('a','i')
    JOIN pg_class seq ON seq.oid=d.objid AND seq.relkind='S'
    WHERE tbl.relname IN ('mobile_device_tokens','user_notification_prefs','password_reset_tokens',
      'user_mfa_status','user_locale_preferences','telemetry_stream_ticket_nonces','user_sessions',
      'mfa_login_challenge_consumptions','notification_recipients','alert_notification_deliveries',
      'report_execution_log','saved_reports','scheduled_reports','messaging_conversations',
      'messaging_messages','coaching_notes')
  LOOP
    EXECUTE format('REVOKE ALL ON SEQUENCE %s FROM PUBLIC,opstrax_app,opstrax_system',rec.sequence_name);
    allow_app:=rec.table_name IN ('mobile_device_tokens','user_locale_preferences','report_execution_log',
      'saved_reports','scheduled_reports','messaging_conversations','messaging_messages','coaching_notes');
    IF rec.table_name='telemetry_stream_ticket_nonces' THEN
      EXECUTE format('GRANT USAGE ON SEQUENCE %s TO opstrax_app,opstrax_system',rec.sequence_name);
    ELSE
      EXECUTE format('GRANT USAGE,SELECT ON SEQUENCE %s TO opstrax_system',rec.sequence_name);
      IF allow_app THEN EXECUTE format('GRANT USAGE,SELECT ON SEQUENCE %s TO opstrax_app',rec.sequence_name); END IF;
    END IF;
  END LOOP;
END
$sequence_acl$;

-- Stage58 readiness helpers are replaced so generic/shared and special/catalog
-- contracts remain exact while Stage132 owns the private policy allow-list.
CREATE OR REPLACE FUNCTION opstrax_security.generic_policy_contract_valid()
RETURNS boolean LANGUAGE sql STABLE SECURITY INVOKER
SET search_path = pg_catalog, public
AS $function$
WITH tenant_scope AS (
  SELECT c.relname table_name,
    CASE WHEN c.relname='companies' THEN 'id'
         WHEN EXISTS(SELECT 1 FROM information_schema.columns x WHERE x.table_schema='public' AND x.table_name=c.relname AND x.column_name='company_id' AND x.data_type='bigint') THEN 'company_id'
         ELSE 'tenant_id' END tenant_col
  FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace
  WHERE n.nspname='public' AND c.relkind IN ('r','p')
    AND c.relname NOT IN ('platform_invoices','gps_gateway_replay','platform_impersonation_sessions','roles','report_catalog',
      'device_command_capabilities','device_connectivity_observations','device_connectivity_profiles',
      'device_firmware_campaign_targets','device_firmware_campaigns','device_retirement_records',
      'device_rma_cases','device_rma_events','device_rma_replacements','device_rma_support_actions',
      'device_spare_pool_entries','device_spare_pool_events','device_support_tier_events',
      'latest_device_signals','telematics_device_commands',
      'mobile_device_tokens','user_notification_prefs','password_reset_tokens','user_mfa_status','user_locale_preferences',
      'telemetry_stream_ticket_nonces','user_sessions','mfa_login_challenge_consumptions','notification_recipients',
      'alert_notification_deliveries','report_execution_log','saved_reports','scheduled_reports','messaging_conversations',
      'messaging_messages','coaching_notes')
    AND (c.relname='companies' OR EXISTS(SELECT 1 FROM information_schema.columns x
      WHERE x.table_schema='public' AND x.table_name=c.relname
        AND x.column_name IN ('company_id','tenant_id') AND x.data_type='bigint'))
), expected(table_name,policy_name,permissive,command_name,role_name,qual,check_expr) AS (
  SELECT table_name,'tenant_ticket_app','PERMISSIVE','ALL','opstrax_app',
    format('(%s = ( SELECT opstrax_security.current_tenant_id() AS current_tenant_id))',tenant_col),
    format('(%s = ( SELECT opstrax_security.current_tenant_id() AS current_tenant_id))',tenant_col)
  FROM tenant_scope
  UNION ALL SELECT table_name,'system_control_plane','PERMISSIVE','ALL','opstrax_system','true','true' FROM tenant_scope
), actual AS (
  SELECT p.tablename,p.policyname,p.permissive,p.cmd,array_to_string(p.roles,','),
    regexp_replace(COALESCE(p.qual,''),'\s+',' ','g'),regexp_replace(COALESCE(p.with_check,''),'\s+',' ','g')
  FROM pg_policies p JOIN tenant_scope t ON t.table_name=p.tablename WHERE p.schemaname='public'
)
SELECT (SELECT count(*) FROM actual)=(SELECT count(*) FROM expected)
  AND NOT EXISTS(SELECT * FROM expected EXCEPT SELECT * FROM actual)
  AND NOT EXISTS(SELECT * FROM actual EXCEPT SELECT * FROM expected)
$function$;

CREATE OR REPLACE FUNCTION opstrax_security.special_policy_contract_valid()
RETURNS boolean LANGUAGE sql STABLE SECURITY INVOKER
SET search_path = pg_catalog, public
AS $function$
WITH app_expr AS (
  SELECT table_name,column_name,prefix,command_name,policy_name,
    CASE command_name WHEN 'SELECT' THEN format('((%s IS NULL) OR (%s = ( SELECT opstrax_security.current_tenant_id() AS current_tenant_id)))',column_name,column_name)
      WHEN 'INSERT' THEN '' ELSE format('((%s IS NOT NULL) AND (%s = ( SELECT opstrax_security.current_tenant_id() AS current_tenant_id)))',column_name,column_name) END qual,
    CASE command_name WHEN 'SELECT' THEN '' WHEN 'DELETE' THEN ''
      ELSE format('((%s IS NOT NULL) AND (%s = ( SELECT opstrax_security.current_tenant_id() AS current_tenant_id)))',column_name,column_name) END check_expr
  FROM (VALUES ('roles','company_id','roles'),('report_catalog','tenant_id','report_catalog')) t(table_name,column_name,prefix)
  CROSS JOIN LATERAL (VALUES ('SELECT',prefix||'_app_select'),('INSERT',prefix||'_app_insert'),
    ('UPDATE',prefix||'_app_update'),('DELETE',prefix||'_app_delete')) c(command_name,policy_name)
), expected(table_name,policy_name,permissive,command_name,role_name,qual,check_expr) AS (
  SELECT table_name,policy_name,'PERMISSIVE',command_name,'opstrax_app',qual,check_expr FROM app_expr
  UNION ALL SELECT table_name,'system_control_plane','PERMISSIVE','ALL','opstrax_system','true','true'
    FROM (VALUES ('roles'),('report_catalog'),('role_permissions')) s(table_name)
  UNION ALL VALUES
    ('role_permissions','role_permissions_app_select','PERMISSIVE','SELECT','opstrax_app','(EXISTS ( SELECT 1 FROM roles r WHERE ((r.id = role_permissions.role_id) AND ((r.company_id IS NULL) OR (r.company_id = ( SELECT opstrax_security.current_tenant_id() AS current_tenant_id))))))',''),
    ('role_permissions','role_permissions_app_insert','PERMISSIVE','INSERT','opstrax_app','', '(EXISTS ( SELECT 1 FROM roles r WHERE ((r.id = role_permissions.role_id) AND (r.company_id = ( SELECT opstrax_security.current_tenant_id() AS current_tenant_id)))))'),
    ('role_permissions','role_permissions_app_update','PERMISSIVE','UPDATE','opstrax_app','(EXISTS ( SELECT 1 FROM roles r WHERE ((r.id = role_permissions.role_id) AND (r.company_id = ( SELECT opstrax_security.current_tenant_id() AS current_tenant_id)))))','(EXISTS ( SELECT 1 FROM roles r WHERE ((r.id = role_permissions.role_id) AND (r.company_id = ( SELECT opstrax_security.current_tenant_id() AS current_tenant_id)))))'),
    ('role_permissions','role_permissions_app_delete','PERMISSIVE','DELETE','opstrax_app','(EXISTS ( SELECT 1 FROM roles r WHERE ((r.id = role_permissions.role_id) AND (r.company_id = ( SELECT opstrax_security.current_tenant_id() AS current_tenant_id)))))','')
), actual AS (
  SELECT tablename,policyname,permissive,cmd,array_to_string(roles,','),
    regexp_replace(COALESCE(qual,''),'\s+',' ','g'),regexp_replace(COALESCE(with_check,''),'\s+',' ','g')
  FROM pg_policies WHERE schemaname='public' AND tablename IN ('roles','report_catalog','role_permissions')
)
SELECT (SELECT count(*) FROM expected)=15 AND (SELECT count(*) FROM actual)=15
  AND NOT EXISTS(SELECT * FROM expected EXCEPT SELECT * FROM actual)
  AND NOT EXISTS(SELECT * FROM actual EXCEPT SELECT * FROM expected)
$function$;

CREATE OR REPLACE FUNCTION opstrax_security.private_policy_contract_valid()
RETURNS boolean LANGUAGE plpgsql STABLE SECURITY INVOKER
SET search_path = pg_catalog, public, opstrax_security
AS $function$
DECLARE
  bad_count integer;
  actual_policy_fingerprint text;
BEGIN
  IF to_regprocedure('opstrax_security.issue_principal_ticket(bigint,bigint,integer,bigint,integer)') IS NULL
     OR to_regprocedure('opstrax_security.current_user_id()') IS NULL
     OR has_function_privilege('opstrax_app','opstrax_security.issue_principal_ticket(bigint,bigint,integer,bigint,integer)','EXECUTE')
     OR NOT has_function_privilege('opstrax_system','opstrax_security.issue_principal_ticket(bigint,bigint,integer,bigint,integer)','EXECUTE')
     OR NOT has_function_privilege('opstrax_app','opstrax_security.current_user_id()','EXECUTE')
     OR has_function_privilege('opstrax_system','opstrax_security.current_user_id()','EXECUTE') THEN RETURN false; END IF;

  SELECT count(*) INTO bad_count FROM (VALUES
    ('mobile_device_tokens',4),('user_notification_prefs',4),('password_reset_tokens',1),
    ('user_mfa_status',4),('user_locale_preferences',4),('telemetry_stream_ticket_nonces',2),
    ('user_sessions',4),('mfa_login_challenge_consumptions',1),('notification_recipients',3),
    ('alert_notification_deliveries',1),('report_execution_log',3),('saved_reports',4),
    ('scheduled_reports',4),('messaging_conversations',4),('messaging_messages',4),('coaching_notes',3)
  ) expected(table_name,policy_count)
  JOIN pg_class c ON c.oid=to_regclass('public.'||expected.table_name)
  WHERE NOT c.relrowsecurity OR NOT c.relforcerowsecurity
    OR (SELECT count(*) FROM pg_policies p WHERE p.schemaname='public' AND p.tablename=expected.table_name)<>expected.policy_count;
  IF bad_count>0 THEN RETURN false; END IF;

  -- Lock every reviewed policy field, including the full USING/WITH CHECK
  -- expressions. The DDL above remains the readable authority; this fingerprint
  -- prevents a superficially similar but permissive expression from passing.
  SELECT md5(string_agg(concat_ws('|',
    p.tablename,p.policyname,p.permissive,p.cmd,array_to_string(p.roles,','),
    regexp_replace(COALESCE(p.qual,''),'[[:space:]]+',' ','g'),
    regexp_replace(COALESCE(p.with_check,''),'[[:space:]]+',' ','g')),
    chr(10) ORDER BY p.tablename,p.policyname))
  INTO actual_policy_fingerprint
  FROM pg_policies p
  WHERE p.schemaname='public' AND p.tablename IN (
    'mobile_device_tokens','user_notification_prefs','password_reset_tokens','user_mfa_status','user_locale_preferences',
    'telemetry_stream_ticket_nonces','user_sessions','mfa_login_challenge_consumptions','notification_recipients',
    'alert_notification_deliveries','report_execution_log','saved_reports','scheduled_reports','messaging_conversations',
    'messaging_messages','coaching_notes');
  IF actual_policy_fingerprint IS DISTINCT FROM 'edc44159d2a13aee6d8cfa4de0e27a64' THEN RETURN false; END IF;

  IF EXISTS (
    SELECT 1 FROM pg_policies p
    WHERE p.schemaname='public' AND p.tablename IN (
      'mobile_device_tokens','user_notification_prefs','password_reset_tokens','user_mfa_status','user_locale_preferences',
      'telemetry_stream_ticket_nonces','user_sessions','mfa_login_challenge_consumptions','notification_recipients',
      'alert_notification_deliveries','report_execution_log','saved_reports','scheduled_reports','messaging_conversations',
      'messaging_messages','coaching_notes')
      AND (p.roles='{public}'::name[] OR COALESCE(p.qual,'') LIKE '%current_setting%'
        OR COALESCE(p.with_check,'') LIKE '%current_setting%'
        OR (p.roles='{opstrax_app}'::name[]
          AND COALESCE(p.qual,p.with_check,'') NOT LIKE '%current_user_id()%'
          AND COALESCE(p.qual,p.with_check,'') NOT LIKE '%current_principal_has_%'
          AND NOT (p.tablename='messaging_messages'
            AND COALESCE(p.qual,p.with_check,'') LIKE '%FROM messaging_conversations%')))
  ) THEN RETURN false; END IF;

  IF EXISTS (
    SELECT 1 FROM (VALUES
      ('mobile_device_tokens','SELECT'),('mobile_device_tokens','INSERT'),('mobile_device_tokens','UPDATE'),
      ('user_notification_prefs','SELECT'),('user_notification_prefs','INSERT'),('user_notification_prefs','UPDATE'),
      ('user_mfa_status','SELECT'),('user_mfa_status','INSERT'),('user_mfa_status','UPDATE'),
      ('user_locale_preferences','SELECT'),('user_locale_preferences','INSERT'),('user_locale_preferences','UPDATE'),
      ('telemetry_stream_ticket_nonces','INSERT'),('user_sessions','SELECT'),('user_sessions','UPDATE'),('user_sessions','DELETE'),
      ('notification_recipients','SELECT'),('notification_recipients','UPDATE'),
      ('report_execution_log','SELECT'),('report_execution_log','INSERT'),
      ('saved_reports','SELECT'),('saved_reports','INSERT'),('saved_reports','UPDATE'),
      ('scheduled_reports','SELECT'),('scheduled_reports','INSERT'),('scheduled_reports','UPDATE'),
      ('messaging_conversations','SELECT'),('messaging_conversations','INSERT'),('messaging_conversations','UPDATE'),
      ('messaging_messages','SELECT'),('messaging_messages','INSERT'),('messaging_messages','UPDATE'),
      ('coaching_notes','SELECT'),('coaching_notes','INSERT')
    ) expected(table_name,command_name)
    LEFT JOIN pg_policies p ON p.schemaname='public' AND p.tablename=expected.table_name
      AND p.policyname='principal_app_'||lower(expected.command_name) AND p.cmd=expected.command_name
      AND p.roles='{opstrax_app}'::name[]
    WHERE p.policyname IS NULL
  ) OR EXISTS (
    SELECT 1 FROM (VALUES
      ('mobile_device_tokens'),('user_notification_prefs'),('password_reset_tokens'),('user_mfa_status'),
      ('user_locale_preferences'),('telemetry_stream_ticket_nonces'),('user_sessions'),
      ('mfa_login_challenge_consumptions'),('notification_recipients'),('alert_notification_deliveries'),
      ('report_execution_log'),('saved_reports'),('scheduled_reports'),('messaging_conversations'),
      ('messaging_messages'),('coaching_notes')
    ) expected(table_name)
    LEFT JOIN pg_policies p ON p.schemaname='public' AND p.tablename=expected.table_name
      AND p.policyname='system_control_plane' AND p.cmd='ALL' AND p.roles='{opstrax_system}'::name[]
      AND p.qual='true' AND p.with_check='true'
    WHERE p.policyname IS NULL
  ) THEN RETURN false; END IF;

  -- ACL allow-list: every app verb must match the reviewed matrix exactly.
  IF EXISTS (
    SELECT 1 FROM (VALUES
      ('mobile_device_tokens',true,true,true,false),('user_notification_prefs',true,true,true,false),
      ('password_reset_tokens',false,false,false,false),('user_mfa_status',true,true,true,false),
      ('user_locale_preferences',true,true,true,false),('telemetry_stream_ticket_nonces',false,true,false,false),
      ('user_sessions',true,false,true,true),('mfa_login_challenge_consumptions',false,false,false,false),
      ('notification_recipients',true,false,true,false),('alert_notification_deliveries',false,false,false,false),
      ('report_execution_log',true,true,false,false),('saved_reports',true,true,true,false),
      ('scheduled_reports',true,true,true,false),('messaging_conversations',true,true,true,false),
      ('messaging_messages',true,true,true,false),('coaching_notes',true,true,false,false)
    ) expected(table_name,s,i,u,d)
    WHERE has_table_privilege('opstrax_app','public.'||table_name,'SELECT')<>s
       OR has_table_privilege('opstrax_app','public.'||table_name,'INSERT')<>i
       OR has_table_privilege('opstrax_app','public.'||table_name,'UPDATE')<>u
       OR has_table_privilege('opstrax_app','public.'||table_name,'DELETE')<>d
       OR has_table_privilege('opstrax_app','public.'||table_name,'TRUNCATE,REFERENCES,TRIGGER')
  ) THEN RETURN false; END IF;

  IF EXISTS (
    SELECT 1 FROM (VALUES
      ('mobile_device_tokens'),('user_notification_prefs'),('password_reset_tokens'),('user_mfa_status'),
      ('user_locale_preferences'),('telemetry_stream_ticket_nonces'),('user_sessions'),
      ('mfa_login_challenge_consumptions'),('notification_recipients'),('alert_notification_deliveries'),
      ('report_execution_log'),('saved_reports'),('scheduled_reports'),('messaging_conversations'),
      ('messaging_messages'),('coaching_notes')
    ) expected(table_name)
    WHERE NOT has_table_privilege('opstrax_system','public.'||table_name,'SELECT')
       OR NOT has_table_privilege('opstrax_system','public.'||table_name,'INSERT')
       OR NOT has_table_privilege('opstrax_system','public.'||table_name,'UPDATE')
       OR NOT has_table_privilege('opstrax_system','public.'||table_name,'DELETE')
       OR has_table_privilege('opstrax_system','public.'||table_name,'TRUNCATE,REFERENCES,TRIGGER')
  ) THEN RETURN false; END IF;
  RETURN true;
END
$function$;

-- Late DeviceOps evidence/projection tables intentionally expose either read-only
-- tenant views or no application access. Keep their migration-owned policies exact;
-- they must never be swept into the generic FOR ALL contract by boot reconciliation.
DROP FUNCTION IF EXISTS opstrax_security.bounded_read_policy_contract_valid();
CREATE OR REPLACE FUNCTION opstrax_security.migration_owned_policy_contract_valid()
RETURNS boolean LANGUAGE sql STABLE SECURITY INVOKER
SET search_path = pg_catalog, public
AS $function$
WITH expected(table_name,policy_command,app_select,app_insert,app_update,system_update) AS (VALUES
  ('camera_provider_event_inbox','ALL',false,false,false,true),
  ('camera_provider_media_references','ALL',false,false,false,true),
  ('device_installation_artifact_references','ALL',true,true,false,false),
  ('device_installation_checklist_observations','ALL',true,true,false,false),
  ('device_installation_work_package_links','ALL',true,true,false,false),
  ('device_installation_work_packages','ALL',true,true,false,false),
  ('device_command_capabilities','SELECT',true,false,false,true),
  ('device_connectivity_observations','SELECT',false,false,false,false),
  ('device_connectivity_profiles','SELECT',false,false,false,true),
  ('device_firmware_campaign_targets','SELECT',true,false,false,false),
  ('device_firmware_campaigns','SELECT',true,false,false,false),
  ('device_retirement_records','SELECT',true,false,false,false),
  ('device_rma_cases','SELECT',true,false,false,false),('device_rma_events','SELECT',true,false,false,false),
  ('device_rma_replacements','SELECT',true,false,false,false),('device_rma_support_actions','SELECT',true,false,false,false),
  ('device_spare_pool_entries','SELECT',true,false,false,false),('device_spare_pool_events','SELECT',true,false,false,false),
  ('device_support_tier_events','SELECT',true,false,false,false),('latest_device_signals','SELECT',true,false,false,true),
  ('telematics_device_commands','SELECT',true,false,false,true)
), relations AS (
  SELECT expected.*,c.oid,c.relrowsecurity,c.relforcerowsecurity
  FROM expected LEFT JOIN pg_class c ON c.oid=to_regclass('public.'||expected.table_name)
)
SELECT NOT EXISTS (
  SELECT 1 FROM relations r
  WHERE r.oid IS NULL OR NOT r.relrowsecurity OR NOT r.relforcerowsecurity
    OR (SELECT count(*) FROM pg_policies p WHERE p.schemaname='public' AND p.tablename=r.table_name)<>2
    OR NOT EXISTS (SELECT 1 FROM pg_policies p WHERE p.schemaname='public' AND p.tablename=r.table_name
      AND p.policyname='tenant_ticket_app' AND p.permissive='PERMISSIVE' AND p.cmd=r.policy_command
      AND p.roles='{opstrax_app}'::name[]
      AND p.qual=format('(company_id = ( SELECT opstrax_security.current_tenant_id() AS current_tenant_id))')
      AND ((r.policy_command='SELECT' AND p.with_check IS NULL)
        OR (r.policy_command='ALL' AND p.with_check=p.qual)))
    OR NOT EXISTS (SELECT 1 FROM pg_policies p WHERE p.schemaname='public' AND p.tablename=r.table_name
      AND p.policyname='system_control_plane' AND p.permissive='PERMISSIVE' AND p.cmd='ALL'
      AND p.roles='{opstrax_system}'::name[] AND p.qual='true' AND p.with_check='true')
    OR has_table_privilege('opstrax_app',r.oid,'SELECT')<>r.app_select
    OR has_table_privilege('opstrax_app',r.oid,'INSERT')<>r.app_insert
    OR has_table_privilege('opstrax_app',r.oid,'UPDATE')<>r.app_update
    OR has_table_privilege('opstrax_app',r.oid,'DELETE,TRUNCATE,REFERENCES,TRIGGER')
    OR NOT has_table_privilege('opstrax_system',r.oid,'SELECT')
    OR NOT has_table_privilege('opstrax_system',r.oid,'INSERT')
    OR has_table_privilege('opstrax_system',r.oid,'UPDATE')<>r.system_update
    OR has_table_privilege('opstrax_system',r.oid,'DELETE,TRUNCATE,REFERENCES,TRIGGER')
)
$function$;

REVOKE ALL ON FUNCTION opstrax_security.generic_policy_contract_valid() FROM PUBLIC,opstrax_app,opstrax_system;
REVOKE ALL ON FUNCTION opstrax_security.special_policy_contract_valid() FROM PUBLIC,opstrax_app,opstrax_system;
REVOKE ALL ON FUNCTION opstrax_security.private_policy_contract_valid() FROM PUBLIC,opstrax_app,opstrax_system;
REVOKE ALL ON FUNCTION opstrax_security.migration_owned_policy_contract_valid() FROM PUBLIC,opstrax_app,opstrax_system;
GRANT EXECUTE ON FUNCTION opstrax_security.generic_policy_contract_valid() TO opstrax_system;
GRANT EXECUTE ON FUNCTION opstrax_security.special_policy_contract_valid() TO opstrax_system;
GRANT EXECUTE ON FUNCTION opstrax_security.private_policy_contract_valid() TO opstrax_system;
GRANT EXECUTE ON FUNCTION opstrax_security.migration_owned_policy_contract_valid() TO opstrax_system;

DO $verify$
DECLARE
  security_owner oid;
  private_ok boolean;
  generic_ok boolean;
  migration_owned_ok boolean;
  special_ok boolean;
BEGIN
  SELECT nspowner INTO STRICT security_owner FROM pg_namespace WHERE nspname='opstrax_security';
  IF EXISTS (
    SELECT 1 FROM pg_proc p JOIN pg_namespace n ON n.oid=p.pronamespace
    WHERE n.nspname='opstrax_security'
      AND p.proname IN ('issue_principal_ticket','current_user_id')
      AND (NOT p.prosecdef OR p.proowner<>security_owner
        OR p.proconfig IS NULL OR NOT EXISTS (SELECT 1 FROM unnest(p.proconfig) cfg WHERE cfg LIKE 'search_path=%'))
  ) THEN RAISE EXCEPTION 'Stage132 principal functions are not owner-bound SECURITY DEFINER functions'; END IF;
  private_ok:=opstrax_security.private_policy_contract_valid();
  generic_ok:=opstrax_security.generic_policy_contract_valid();
  migration_owned_ok:=opstrax_security.migration_owned_policy_contract_valid();
  special_ok:=opstrax_security.special_policy_contract_valid();
  IF NOT private_ok OR NOT generic_ok OR NOT migration_owned_ok OR NOT special_ok THEN
    RAISE EXCEPTION 'Stage132 exact policy/ACL contract failed (private=%, generic=%, migration_owned=%, special=%)',
      private_ok,generic_ok,migration_owned_ok,special_ok;
  END IF;
END
$verify$;

INSERT INTO schema_migrations(version,description)
VALUES ('2026_09_08_stage132_private_user_row_authority',
  'Non-forgeable authenticated principal ticket and exact private user/recipient RLS')
ON CONFLICT(version) DO UPDATE SET description=EXCLUDED.description;

COMMIT;
