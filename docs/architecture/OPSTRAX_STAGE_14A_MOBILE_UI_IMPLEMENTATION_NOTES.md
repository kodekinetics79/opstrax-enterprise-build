# Opstrax Stage 14A Mobile UI Implementation Notes

## Delivered Screens

- Login screen with secure backend authentication.
- Dashboard with role model, job selection, and live job preview.
- Workflow tab for assignment, access, pickup, handover, and proof summary.
- Proof tab for package and evidence artifacts.
- Telemetry tab for live state, safety, and maintenance previews.
- Settings tab for session, permissions, and contract notes.

## UI Principles

- Use honest loading, empty, and error states.
- Show read-only states instead of hiding entire surfaces when possible.
- Keep the client enterprise-clean, not toy-like.
- Do not fabricate data to fill a missing API response.

## Implementation Notes

- Navigation is React Navigation with a secure auth gate.
- Session persistence uses `expo-secure-store`.
- A selected job id is persisted locally to make the workflow sticky.
- The web export is enabled so the shell can be bundled and reviewed.

