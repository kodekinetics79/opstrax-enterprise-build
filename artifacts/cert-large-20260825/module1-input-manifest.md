# Module 1 certification input manifest

Prepared: 2026-08-25
Tenant: `CERT-LARGE-20260825`
Purpose: customer-facing Chrome import/onboarding certification only; these files are not SQL seed inputs.

## Population control

| Population | Required | Prepared/evidenced | Control status |
|---|---:|---:|---|
| Branches | 5 | 5: CL-HQ, NE-HUB, SE-DEPOT, MW-YARD, WEST-HUB | Created through the branch UI; preserved Chrome evidence exists. |
| Role users | 25 | 24 active accounts | The twenty-fifth Customer account is blocked: the Customer/CRM surface is not in the authorized product plan. Do not enable or synthesize the entitlement. Users are created with the product form; there is no customer-facing user CSV import. |
| Vehicles | 1,000 | 5 x 200 valid rows | Exact total reconciled below. |
| Drivers | 1,250 | 5 x 250 valid rows | Exact total reconciled below. |
| Devices | 1,100 | 5 x 220 valid rows | Exact total reconciled below. |
| Trailers/assets | 300 | 5 x 60 valid rows | Exact total reconciled below. The governed asset type code is `TRAILER`. |

All certification identities are synthetic and non-personal. Email addresses use the reserved `example.invalid` domain; names are role/branch labels; identifiers, IMEIs, licence numbers, plates, and alternate vehicle identities are generated certification values.

## Valid customer-import files

The listed row counts exclude the header. SHA-256 values identify the exact files to select in Chrome. Every Module 1 fleet-master CSV includes the downloaded `branchCode` column so a tenant-wide administrator can create records in the named active branch; a branch-scoped operator may use only their own branch code.

| UI surface / branch account | File | Rows | SHA-256 |
|---|---|---:|---|
| Vehicles / CL-HQ Fleet Manager | `input/vehicles_CL-HQ_001-200.csv` | 200 | `a309ad437ff7f59cc5d224f39b9bb7418a5a7a8ae3776f108d6f8f399bc09a93` |
| Vehicles / NE-HUB Fleet Manager | `input/vehicles_NE-HUB_201-400.csv` | 200 | `51a27e48e765947029573807f5b8ffdb4dae6920972cf903082f7b6e33f61ce6` |
| Vehicles / SE-DEPOT Fleet Manager | `input/vehicles_SE-DEPOT_401-600.csv` | 200 | `edf6136b8ea564482c09d94bd5235a09b5019557e0a2f75212cfa6fa9f5e4d26` |
| Vehicles / MW-YARD Fleet Manager | `input/vehicles_MW-YARD_601-800.csv` | 200 | `bd5d1af8370d33d68a4f919a49250f7e3a3bef8e1eef71cf112661207036c6c9` |
| Vehicles / WEST-HUB Fleet Manager | `input/vehicles_WEST-HUB_801-1000.csv` | 200 | `461b8da689ce3b3568e9ed04e24bcf3cb1747194dfd6d7f3640ae69f9b0518c9` |
| Drivers / CL-HQ Fleet Manager | `input/drivers_CL-HQ_001-250.csv` | 250 | `b138f8ff474511c0f196b61bfcee770ca4eee9ccf3bbb51fda0776c18fe038e7` |
| Drivers / NE-HUB Fleet Manager | `input/drivers_NE-HUB_251-500.csv` | 250 | `7362ecaddf40e15ec78933b44b5f195da13982780de666bf3229008027b0055c` |
| Drivers / SE-DEPOT Fleet Manager | `input/drivers_SE-DEPOT_501-750.csv` | 250 | `189c81b71ec69cc2e9442cf1555540e1a9c77f0ecac796072dfbf4805999ad97` |
| Drivers / MW-YARD Fleet Manager | `input/drivers_MW-YARD_751-1000.csv` | 250 | `25a0895d023079dfa612264a9eceb0cfa76814eeb73a2df2e65189d72ee04a2c` |
| Drivers / WEST-HUB Fleet Manager | `input/drivers_WEST-HUB_1001-1250.csv` | 250 | `194e6aae4be6a23a2640777b31e911a8e475c64e0fb93789516aad599db69812` |
| Devices / CL-HQ Fleet Manager | `input/devices_CL-HQ_001-220.csv` | 220 | `0797432ca797ebc309f7bcf0c4cc42f580d9d37dfd5aa90a1aa05c9ada07babf` |
| Devices / NE-HUB Fleet Manager | `input/devices_NE-HUB_221-440.csv` | 220 | `afa6f857e76fa5aa13b410bc2013f4f57bdbd2e1cf175c162a9427e1ab52ea6d` |
| Devices / SE-DEPOT Fleet Manager | `input/devices_SE-DEPOT_441-660.csv` | 220 | `8014896b3293789a99851b222fbc084136e0c7b819f0974a886986838ca3f425` |
| Devices / MW-YARD Fleet Manager | `input/devices_MW-YARD_661-880.csv` | 220 | `9637d6e79a980e12c7ba00ab51970209d9d493df07f9bced74a50939c8c0e8b3` |
| Devices / WEST-HUB Fleet Manager | `input/devices_WEST-HUB_881-1100.csv` | 220 | `0a714d447dda071d1a7373b5595041d7a8e99a9a199570811119d6c829fcdad2` |
| Assets / CL-HQ Fleet Manager | `input/assets_CL-HQ_001-060.csv` | 60 | `0e3a3c00b32e8ab21345385e78dcd76b4e29f4d755a9cf88f0d222e7e078f6ea` |
| Assets / NE-HUB Fleet Manager | `input/assets_NE-HUB_061-120.csv` | 60 | `d6a510772936f0ba7cd867c0b2b316b2505d8fb1f8d60a9e6cf4f320a92817f2` |
| Assets / SE-DEPOT Fleet Manager | `input/assets_SE-DEPOT_121-180.csv` | 60 | `0cc29dc5f41c803651c91ffcc2c15effafd20245fb9ecbd3504ddf7e93b10ce0` |
| Assets / MW-YARD Fleet Manager | `input/assets_MW-YARD_181-240.csv` | 60 | `e2e4c1acd46fc450f0ac9b5cedba7933f109c38599238cde0af6be6ed6f24a4a` |
| Assets / WEST-HUB Fleet Manager | `input/assets_WEST-HUB_241-300.csv` | 60 | `e049468cae88816edd3f7b75a3ca6d75ffb83856e99c639c9110df3e51b944a3` |

Schema verification: vehicles have 13 columns, drivers 6, devices 7, and assets 10, matching the downloaded customer templates. Every data row has the expected column count. Valid primary identities are unique across all five batches. Total rows are exactly 1,000 / 1,250 / 1,100 / 300.

## Controlled rejection files

Run these only in CL-HQ after the corresponding valid batch is present. Preview first; preserve the row-level errors. Do not commit an invalid preview.

| File | Rows | SHA-256 | Expected product result | Correction plan |
|---|---:|---|---|---|
| `input/vehicles_CL-HQ_invalid-controlled.csv` | 6 | `0aa7e250a2f4270e3ad70f99e3bc2355b98a4e89513ba76e15826c3013e0110b` | Six rejected rows: missing code, year below 1950, negative odometer, unsupported alternate-identity kind, invalid alternate value, invalid VIN. | Correct the six fields in the customer sheet, export a new CSV, preview for zero errors, then cancel so the certified 1,000 total is unchanged. |
| `input/vehicles_CL-HQ_duplicates-controlled.csv` | 2 | `b009850be410426f87d15197d5f022940e426610af1300e96d124f637a700b81` | Existing `CLHQ-V-0001` is an update candidate; the second identical row is rejected as a same-file duplicate. No create is permitted. | Remove the repeated row; use the existing-record edit/update workflow only if a deliberate change is required. Verify assignment history and total remain unchanged. |
| `input/drivers_CL-HQ_invalid-controlled.csv` | 3 | `0c3198625dc5245d19529d44c12c5a6d3a820ce5fcbfa72ffb4327c0c52233d5` | Three rejected rows: missing code, missing full name, malformed email. | Correct the fields, preview for zero errors, then cancel. Do not create records above 1,250. |
| `input/drivers_CL-HQ_duplicates-controlled.csv` | 2 | `a74f1c8aa09ba1fc3091ad06c1def15ee53ef5ae88101f6e22358b35633d64b8` | Existing `CLHQ-D-0001` is an update candidate; the second row is a same-file duplicate. No create is permitted. | Remove the repeated row and use the governed update workflow; confirm licence identity and assignments are not duplicated. |
| `input/devices_CL-HQ_invalid-controlled.csv` | 5 | `4454c2cb15f3873b98c1884bf42d7bd4af85309fa8ae410eaf8f45c81d9d0bb0` | Five rejected rows: missing serial, invalid serial characters, 14-digit IMEI, alphabetic IMEI, unsupported hardware role/category. | Correct with unused globally unique serial/IMEI values and a supported category, preview for zero errors, then cancel. |
| `input/devices_CL-HQ_duplicates-controlled.csv` | 2 | `20a0a19385b1ce391fe4f942c28a9e6ca1729c70273104a0b17c9b1c9ea05eea` | Both rows collide with existing global serial/IMEI identity; the second also duplicates the first within the file. Zero imports expected. | Do not rename an installed device to bypass identity governance. Search the existing device and use its supported lifecycle/assignment workflow. |
| `input/assets_CL-HQ_invalid-controlled.csv` | 8 | `66d9a855a5f1ac2867f9685050f85d9646143d0e36ed40995b29b1b016004529` | Eight rejected rows: missing tag, missing name, nonexistent asset type, nonnumeric quantity, zero quantity, invalid returnable flag, invalid status, invalid condition. | Correct against the downloaded template and existing `TRAILER` type, preview for zero errors, then cancel. |
| `input/assets_CL-HQ_duplicates-controlled.csv` | 2 | `dc51c09c7bd9889be3b9ef41317746824af266e2f197a704d51c41b8ab2ec44c` | Existing `CLHQ-TRL-0001` is an update candidate; the second row is a same-file duplicate. No create is permitted. | Remove the repeated row and use the governed update workflow; verify custody/assignment history and total remain unchanged. |

## Chrome workflow and order

1. Log in with the branch-scoped Fleet Manager for the batch under test; record deployed SHA and branch label.
2. Download the real template from that exact UI and compare its ordered header to the selected file.
3. In CL-HQ, preview the controlled invalid file. Capture useful row errors, failed requests, console state, and timing. Do not commit.
4. Correct the source through the customer spreadsheet workflow, export it again, and preview it successfully. Cancel the clean preview to preserve exact totals.
5. Upload the valid branch batch, require the expected create count and zero invalid rows, then commit through the UI.
6. Refresh, log out/in, search the first and last identifiers, filter/sort/page/export, and record the branch count.
7. After a valid CL-HQ batch exists, preview its duplicate file. Require zero creates and useful duplicate/update classification; do not commit.
8. Repeat branches in order CL-HQ, NE-HUB, SE-DEPOT, MW-YARD, WEST-HUB. Reconcile tenant totals in the rendered product: 1,000 vehicles, 1,250 drivers, 1,100 devices, and 300 trailers/assets.
9. Use database queries only as supporting verification after the Chrome-observed result.

The role accounts are intentionally excluded from CSV processing. Complete the 24 authorized accounts in the Add User/onboarding forms and preserve login evidence per role. Keep the Customer row blocked until the missing Customer/CRM entitlement is externally authorized.

## Evidence boundary at manifest preparation

- Five branches and 24 active authorized role accounts have preserved visible-Chrome evidence in the certification artifact tree.
- Five 200-row vehicle batches were visibly imported and persisted on deployed SHA `ec024903...`; later candidate SHAs require the prescribed redeploy/retest cycle.
- Driver import evidence is partial; remaining driver, device, and asset uploads still require visible Chrome execution.
- Assignment-history fixes were closed in code at `045c8a4...`, but full cross-entity customer journeys still require browser retest.
- This manifest certifies only file integrity and intended use. It does not certify Module 1.
