import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { createRequire } from "node:module";
import { runInNewContext } from "node:vm";
import test from "node:test";
import { createElement } from "react";
import { renderToStaticMarkup } from "react-dom/server";
import axios from "axios";
import ts from "typescript";

const require = createRequire(import.meta.url);
const source = readFileSync(new URL("../src/pages/LoginPage.tsx", import.meta.url), "utf8");
const tree = ts.createSourceFile("LoginPage.tsx", source, ts.ScriptTarget.Latest, true, ts.ScriptKind.TSX);
const component = tree.statements.find(node => ts.isFunctionDeclaration(node) && node.name?.text === "LoginPage");
assert.ok(component, "the production login component must exist");
const apiSource = readFileSync(new URL("../src/services/authApi.ts", import.meta.url), "utf8");
const apiTree = ts.createSourceFile("authApi.ts", apiSource, ts.ScriptTarget.Latest, true, ts.ScriptKind.TS);
const challengeGuard = apiTree.statements.find(node => ts.isFunctionDeclaration(node) && node.name?.text === "isMfaChallenge");
assert.ok(challengeGuard, "the production signed-challenge discriminator must exist");

// Exercise production callbacks and actual JSX with controlled dependencies.
// These are source/server-markup tests, not mounted React, browser-autofill,
// viewport, live authentication, provider or certification evidence.
const declarations = component.body.statements.filter(ts.isVariableStatement).flatMap(statement => Array.from(statement.declarationList.declarations));
const hookNames = name => declarations.filter(declaration => ts.isCallExpression(declaration.initializer) && declaration.initializer.expression.getText(tree) === name)
  .map(declaration => ts.isArrayBindingPattern(declaration.name) ? declaration.name.elements[0].name.getText(tree) : declaration.name.getText(tree));
const stateNames = hookNames("useState"), refNames = hookNames("useRef"), mutationNames = hookNames("useMutation");
const relevant = tree.statements.filter(node => !ts.isImportDeclaration(node));
const code = ts.transpileModule(`${relevant.map(node => node.getText(tree)).join("\n")}\n${challengeGuard.getText(apiTree)}\nexport const helpers = { getLoginErrorMessage, getMfaErrorMessage };`, {
  compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022, jsx: ts.JsxEmit.ReactJSX },
}).outputText;
const plain = value => JSON.parse(JSON.stringify(value));

function find(node, predicate) {
  if (Array.isArray(node)) {
    for (const child of node) { const found = find(child, predicate); if (found) return found; }
    return null;
  }
  if (!node || typeof node !== "object") return null;
  if (predicate(node)) return node;
  return find(node.props?.children, predicate);
}
const byId = (element, id) => find(element, node => node.props?.id === id);
const submitButton = element => find(element, node => node.type === "button" && node.props.type === "submit");
const form = element => find(element, node => node.type === "form");

function fixture(initial = {}, search = "") {
  const state = { ...initial }, refs = {}, mutations = {}, calls = [];
  let stateIndex, refIndex, mutationIndex;
  const exports = {};
  const api = {
    bootstrap: async () => { calls.push(["bootstrap"]); },
    login: async (...args) => { calls.push(["login", ...args]); return { id: "session-from-api" }; },
    ssoDiscover: async (...args) => { calls.push(["discover", ...args]); return { ssoConfigured: false, connection: null }; },
    mfaLoginVerify: async (...args) => { calls.push(["mfa", ...args]); return { id: "verified-session" }; },
  };
  const blankIcon = () => null;
  const Link = ({ to, children, ...props }) => createElement("a", { ...props, href: to }, children);
  runInNewContext(code, {
    exports, require, axios, URLSearchParams, API_BASE_URL: "http://local-api.invalid", authApi: api,
    AlertCircle: blankIcon, ArrowRight: blankIcon, Building2: blankIcon, ClipboardCheck: blankIcon,
    Route: blankIcon, ShieldCheck: blankIcon, Wrench: blankIcon, OpsTraxLogo: blankIcon, Link,
    window: { location: { search, assign: url => calls.push(["redirect", url]) } },
    useAuth: () => ({ setSession: session => calls.push(["session", session]) }),
    useNavigate: () => (route, options) => calls.push(["navigate", route, options]),
    getLandingRouteForSession: session => { calls.push(["landing", session]); return "/owned-workspace"; },
    flushSync: callback => { calls.push(["flush-start"]); callback(); calls.push(["flush-end"]); },
    requestAnimationFrame: callback => { callback(); return 1; },
    useEffect: () => {}, useCallback: callback => callback,
    useState: initialValue => {
      const name = stateNames[stateIndex++];
      if (!(name in state)) state[name] = initialValue;
      return [state[name], update => { state[name] = typeof update === "function" ? update(state[name]) : update; }];
    },
    useRef: () => {
      const name = refNames[refIndex++];
      refs[name] ??= { current: { value: "", focus: () => calls.push(["focus", name]) } };
      return refs[name];
    },
    useMutation: config => {
      const name = mutationNames[mutationIndex++];
      mutations[name] ??= { isPending: false, isError: false, error: null };
      const mutation = mutations[name];
      mutation.config = config;
      mutation.mutate = payload => calls.push(["mutate", name, payload]);
      mutation.reset = () => { calls.push(["reset", name]); mutation.isError = false; mutation.error = null; };
      return mutation;
    },
  });
  const render = () => { stateIndex = refIndex = mutationIndex = 0; return exports.LoginPage(); };
  const submit = () => { form(render()).props.onSubmit({ preventDefault: () => calls.push(["prevent-default"]) }); };
  render();
  return { state, refs, mutations, calls, render, submit, helpers: exports.helpers };
}
const identity = { companyCode: "  MERIDIAN  ", email: "  admin@example.invalid  " };
const challenge = { mfaRequired: true, challengeToken: "signed-fixture-challenge", email: "admin@example.invalid" };
const axiosError = (status, message, code) => new axios.AxiosError(message, code, undefined, undefined,
  status ? { status, data: { message } } : undefined);

test("Organization/email validation gates discovery and submits trimmed tenant identity", async () => {
  const login = fixture({ email: identity.email });
  login.submit();
  assert.equal(login.state.companyCodeError, "Enter your organization code.");
  assert.equal(login.calls.some(call => call[0] === "mutate"), false);
  login.state.companyCode = identity.companyCode;
  login.state.email = "not-an-email";
  login.submit();
  assert.equal(login.state.emailError, "Enter a valid work email address.");
  assert.equal(login.calls.some(call => call[0] === "mutate"), false);
  login.state.email = identity.email;
  login.submit();
  const submitted = login.calls.find(call => call[0] === "mutate");
  assert.deepEqual(plain(submitted), ["mutate", "identify", { email: "admin@example.invalid", companyCode: "MERIDIAN" }]);
  await login.mutations.identify.config.mutationFn(submitted[2]);
  assert.deepEqual(login.calls.at(-1), ["discover", "admin@example.invalid", "MERIDIAN"]);
  login.mutations.identify.config.onSuccess({ ssoConfigured: true, connection: { id: 42, displayName: "Fixture SSO" } });
  assert.equal(login.state.step, "authenticate");
  assert.equal(login.state.ssoConn.id, 42);
  login.mutations.identify.config.onError(new Error("discovery unavailable"));
  assert.equal(login.state.ssoConn, null, "discovery failure keeps the existing password fallback");
  assert.ok(byId(login.render(), "login-password"));
});

test("Password submission reads the browser-owned value and preserves bootstrap ordering", async () => {
  const login = fixture({ ...identity, step: "authenticate", password: "stale-react-state" });
  login.submit();
  assert.equal(login.state.passwordError, "Enter your password.", "an empty actual field must not resubmit stale state");
  assert.equal(login.calls.some(call => call[0] === "mutate"), false);
  assert.deepEqual(login.calls.at(-1), ["focus", "passwordRef"]);
  login.refs.passwordRef.current.value = "FixturePasswordOnly!23";
  login.submit();
  const submitted = login.calls.find(call => call[0] === "mutate");
  assert.deepEqual(plain(submitted), ["mutate", "login", { companyCode: "MERIDIAN", email: "admin@example.invalid", password: "FixturePasswordOnly!23" }]);
  await login.mutations.login.config.mutationFn(submitted[2]);
  assert.deepEqual(login.calls.slice(-2), [["bootstrap"], ["login", "admin@example.invalid", "FixturePasswordOnly!23", "MERIDIAN"]]);
});

test("Browser-filled fields synchronize only nonempty values into memory", () => {
  const login = fixture({ ...identity, step: "authenticate", password: "retained-state" });
  login.refs.companyCodeRef.current.value = "TENANT-FROM-FIELD";
  login.refs.emailRef.current.value = "filled@example.invalid";
  login.refs.passwordRef.current.value = "FixtureFilledPassword!23";
  form(login.render()).props.onInputCapture();
  assert.equal(login.state.companyCode, "TENANT-FROM-FIELD");
  assert.equal(login.state.email, "filled@example.invalid");
  assert.equal(login.state.password, "FixtureFilledPassword!23");
  login.refs.passwordRef.current.value = "";
  form(login.render()).props.onInputCapture();
  assert.equal(login.state.password, "FixtureFilledPassword!23");
});

test("Rejected credentials clear the actual password and retain non-enumerating retry guidance", () => {
  const login = fixture({ ...identity, step: "authenticate", password: "FixtureRejectedPassword!23" });
  login.refs.passwordRef.current.value = login.state.password;
  login.mutations.login.config.onError(axiosError(401, "account does not exist"));
  assert.equal(login.state.password, "");
  assert.equal(login.refs.passwordRef.current.value, "");
  assert.equal(login.state.email, identity.email);
  assert.equal(login.state.companyCode, identity.companyCode);
  assert.deepEqual(login.calls.at(-1), ["focus", "passwordRef"]);
  login.mutations.login.isError = true;
  login.mutations.login.error = axiosError(401, "account does not exist");
  const markup = renderToStaticMarkup(login.render());
  assert.match(markup, /role="alert"/);
  assert.match(markup, /organization code, email, or password was not recognized/);
  assert.doesNotMatch(markup, /account does not exist|FixtureRejectedPassword/);
});

test("Signed MFA challenges do not create a session and verified sessions retain routing order", async () => {
  const login = fixture({ ...identity, step: "authenticate" });
  login.mutations.login.config.onSuccess(challenge);
  assert.equal(login.state.step, "mfa");
  assert.equal(login.state.mfaChallenge, challenge);
  assert.equal(login.calls.some(call => ["session", "navigate"].includes(call[0])), false);
  let element = login.render();
  const codeField = byId(element, "login-mfa-code");
  assert.equal(codeField.props.autoComplete, "one-time-code");
  assert.equal(codeField.props.inputMode, "numeric");
  assert.equal(codeField.props.maxLength, 6);
  codeField.props.onChange({ target: { value: "12a34b56" } });
  assert.equal(login.state.mfaCode, "123456");
  login.state.mfaCode = " 123456 ";
  login.submit();
  assert.deepEqual(plain(login.calls.at(-1)), ["mutate", "mfaVerify", "123456"]);
  const session = await login.mutations.mfaVerify.config.mutationFn("123456");
  assert.deepEqual(login.calls.at(-1), ["mfa", challenge.challengeToken, "123456"]);
  login.mutations.mfaVerify.config.onSuccess(session);
  assert.deepEqual(plain(login.calls.slice(-5)), [["flush-start"], ["session", session], ["flush-end"], ["landing", session], ["navigate", "/owned-workspace", { replace: true }]]);
  element = login.render();
  assert.match(renderToStaticMarkup(element), /aria-current="step"[^>]*><span>3/);
});

test("Wrong MFA codes retain the signed challenge; expired/consumed challenges restart password entry", async () => {
  const login = fixture({ ...identity, step: "mfa", mfaChallenge: challenge, mfaCode: "654321" });
  login.mutations.mfaVerify.config.onError(axiosError(401, "invalid code"));
  assert.equal(login.state.mfaCode, "");
  assert.equal(login.state.mfaChallenge, challenge);
  assert.equal(login.state.step, "mfa");
  assert.deepEqual(login.calls.at(-1), ["focus", "mfaCodeRef"]);
  for (const message of ["challenge expired", "challenge already used"]) {
    login.state.step = "mfa";
    login.state.mfaChallenge = challenge;
    login.state.password = "FixtureOldPassword!23";
    login.refs.passwordRef.current.value = login.state.password;
    login.render();
    login.mutations.mfaVerify.config.onError(axiosError(401, message));
    assert.equal(login.state.mfaChallenge, null);
    assert.equal(login.state.step, "authenticate");
    assert.equal(login.refs.passwordRef.current.value, "");
    assert.deepEqual(login.calls.at(-1), ["focus", "passwordRef"]);
  }
  login.render();
  await assert.rejects(login.mutations.mfaVerify.config.mutationFn("123456"), /sign-in session expired/);
});

test("Normal credential success uses the same synchronous session and permission-routing handoff", () => {
  const login = fixture({ ...identity, step: "authenticate" });
  const session = { id: "fixture-authenticated-session", permissions: ["owned-workspace"] };
  login.mutations.login.config.onSuccess(session);
  assert.deepEqual(plain(login.calls), [["flush-start"], ["session", session], ["flush-end"], ["landing", session], ["navigate", "/owned-workspace", { replace: true }]]);
});

test("Change returns to identity entry without retaining password or MFA challenge state", () => {
  const login = fixture({ ...identity, step: "mfa", ssoConn: { id: 42 }, password: "FixturePassword!23", passwordError: "old error", mfaChallenge: challenge, mfaCode: "123456" });
  const change = find(login.render(), node => node.type === "button" && node.props.children === "Change");
  assert.equal(change.props.type, "button");
  change.props.onClick();
  assert.equal(login.state.step, "identify");
  for (const name of ["password", "passwordError", "mfaCode"]) assert.equal(login.state[name], "");
  assert.equal(login.state.ssoConn, null);
  assert.equal(login.state.mfaChallenge, null);
  assert.equal(login.state.email, identity.email);
  assert.equal(login.state.companyCode, identity.companyCode);
  assert.deepEqual(login.calls.filter(call => call[0] === "reset"), [["reset", "login"], ["reset", "mfaVerify"]]);
});

test("The redesigned form preserves labels, errors, uncontrolled password and recovery/show controls", () => {
  const login = fixture({ ...identity, companyCodeError: "Enter your organization code." });
  let element = login.render();
  assert.equal(byId(element, "login-company-code").props["aria-describedby"], "login-company-code-error");
  assert.equal(byId(element, "login-company-code-error").props.role, "alert");
  assert.ok(find(element, node => node.type === "label" && node.props.htmlFor === "login-email"));
  assert.equal(find(element, node => node.type === "section").props["aria-labelledby"], "login-title");
  assert.equal(byId(element, "login-title").type, "h1");
  assert.equal(find(element, node => node.type === "details").props.open, undefined, "role/access help is secondary and collapsed initially");
  assert.equal(byId(element, "login-password"), null, "hidden authentication controls do not precede identification");
  login.state.step = "authenticate";
  login.state.passwordError = "Enter your password.";
  element = login.render();
  const username = find(element, node => node.type === "input" && node.props.name === "username");
  assert.equal(username.props.readOnly, true);
  assert.equal(username.props.tabIndex, -1);
  assert.equal(username.props.autoComplete, "username");
  const password = byId(element, "login-password");
  assert.equal(password.props.defaultValue, "");
  assert.equal("value" in password.props, false, "React must not erase the browser-owned saved credential");
  assert.equal(password.props.autoComplete, "current-password");
  assert.equal(password.props["aria-describedby"], "login-password-error");
  assert.ok(find(element, node => node.props?.to === "/forgot-password"));
  let show = find(element, node => node.props?.["aria-label"] === "Show password");
  assert.equal(show.props.type, "button");
  assert.equal(show.props["aria-pressed"], false);
  show.props.onClick();
  element = login.render();
  assert.equal(byId(element, "login-password").props.type, "text");
  show = find(element, node => node.props?.["aria-label"] === "Hide password");
  assert.equal(show.props["aria-pressed"], true);
});

test("SSO renders only for a discovered connection and uses the owning API start route", () => {
  const login = fixture({ ...identity, step: "authenticate", ssoConn: { id: 42, displayName: "Fixture identity provider" } });
  const element = login.render();
  assert.equal(byId(element, "login-password"), null);
  assert.equal(submitButton(element), null, "SSO has one distinct primary action");
  const sso = find(element, node => node.type === "button" && node.props.className === "login2-sso");
  assert.match(renderToStaticMarkup(element), /Continue with Fixture identity provider/);
  sso.props.onClick();
  assert.deepEqual(login.calls.at(-1), ["redirect", "http://local-api.invalid/api/auth/sso/start/42"]);
  login.submit();
  assert.deepEqual(login.calls.at(-1), ["redirect", "http://local-api.invalid/api/auth/sso/start/42"]);
  assert.equal(login.calls.some(call => call[0] === "mutate"), false);
});

test("Pending controls and SSO/credential failure messages remain truthful", () => {
  const login = fixture(identity, "?sso_error=sso_tenant_suspended");
  login.mutations.identify.isPending = true;
  assert.equal(submitButton(login.render()).props.disabled, true);
  assert.match(renderToStaticMarkup(login.render()), /Checking/);
  assert.match(renderToStaticMarkup(login.render()), /access is currently suspended/);
  login.state.step = "authenticate";
  login.mutations.login.isPending = true;
  assert.equal(submitButton(login.render()).props.disabled, true);
  login.mutations.login.isPending = false;
  assert.equal(submitButton(login.render()).props.disabled, false, "empty React password state cannot block native autofill submission");
  login.state.step = "mfa";
  login.state.mfaCode = "12345";
  assert.equal(submitButton(login.render()).props.disabled, true);
  login.state.mfaCode = "123456";
  assert.equal(submitButton(login.render()).props.disabled, false);
  login.mutations.mfaVerify.isPending = true;
  assert.equal(submitButton(login.render()).props.disabled, true);
  assert.match(login.helpers.getLoginErrorMessage(axiosError(429, "internal detail")), /Too many sign-in attempts/);
  assert.match(login.helpers.getLoginErrorMessage(axiosError(403, "internal detail")), /Security verification/);
  assert.match(login.helpers.getLoginErrorMessage(axiosError(null, "offline")), /could not reach/);
  assert.match(login.helpers.getLoginErrorMessage(axiosError(null, "timeout", "ECONNABORTED")), /taking too long/);
  assert.match(login.helpers.getMfaErrorMessage(axiosError(401, "invalid code")), /code was not accepted/);
  assert.match(login.helpers.getMfaErrorMessage(axiosError(401, "challenge expired")), /session expired/);
});
