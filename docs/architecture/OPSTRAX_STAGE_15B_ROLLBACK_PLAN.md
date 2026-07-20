# Stage 15B Rollback Plan

| Area | Rollback Method | Risk | Data/Migration Consideration | Verification After Rollback |
|---|---|---|---|---|
| Frontend-only rollback | Revert the specific frontend commit group. | Low | None if no schema changes were bundled. | Rebuild frontend and verify route rendering. |
| Backend endpoint rollback | Revert the backend commit group only. | Medium | Ensure no dependent frontend paths are left pointing to missing endpoints. | Run backend build/test and endpoint smoke checks. |
| Schema service rollback | Revert service registration or schema service changes as a group. | Medium | Confirm local-only initialization is not partially applied. | Start the app and verify startup completes. |
| Migration rollback | Apply inverse DDL only in local/dev or recreate disposable DB. | Medium | Never roll back production in this workflow. | Re-run schema checks against disposable DB. |
| Docs rollback | Remove only the review docs if needed. | Low | No data impact. | Confirm docs tree remains coherent. |
| Test rollback | Revert test additions only after code stability is proven. | Low | No data impact. | Ensure the existing test suite still passes. |
| Full branch rollback | Reset only if explicitly approved by the user. | High | Could discard valuable verified work. | Re-run the baseline build/test/lint suite. |

