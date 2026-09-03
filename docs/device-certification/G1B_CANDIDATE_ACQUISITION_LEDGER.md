# G1B Physical Candidate Acquisition Ledger

**Issue:** #109  
**Parent:** #110  
**Authority:** `CR-2026-09-03-03`  
**Current execution state:** ACTIVE-BLOCKED — no exact physical tuple frozen yet  
**Commercial truth:** GT06 software **PILOT** / hardware **NOT CERTIFIED**

## Stop condition

Do not admit a device identifier to the certification listener until the exact unit is physically in hand and the identity record below is complete enough to bind test evidence to a real manufacturer/model/hardware-revision/firmware tuple.

A marketplace title such as “GT06 4G tracker” is not sufficient identity evidence.

## Procurement requirement

Acquire **2 identical production-candidate units minimum; 3 preferred** from the same manufacturer/model/SKU/lot where practical.

Retain for the evidence archive:

- seller/manufacturer name;
- listing/SKU/product page snapshot;
- invoice/order receipt;
- package and device-label photographs;
- device serial/IMEI photographs in protected evidence;
- printed/electronic manual and configuration commands;
- FCC ID / ISED identifier where applicable;
- modem/radio/LTE-band documentation;
- exact accessories/harness/power requirements.

## Candidate freeze form

| Field | Unit A | Unit B | Unit C (preferred) |
|---|---|---|---|
| Manufacturer |  |  |  |
| Model |  |  |  |
| SKU/part number |  |  |  |
| Hardware revision |  |  |  |
| Firmware |  |  |  |
| IMEI/serial protected-evidence reference |  |  |  |
| Modem/chipset |  |  |  |
| FCC ID |  |  |  |
| ISED ID |  |  |  |
| Input voltage |  |  |  |
| Backup battery |  |  |  |
| Harness/install type |  |  |  |
| SIM/carrier |  |  |  |
| APN |  |  |  |
| LTE bands |  |  |  |
| Configuration method |  |  |  |
| Seller/order reference |  |  |  |

## Candidate freeze acceptance

Candidate stage passes only when:

1. at least two units are physically available;
2. the units are demonstrably the same manufacturer/model/hardware-revision/firmware candidate;
3. protected identifiers are recorded without public disclosure;
4. intended-market radio/compliance evidence is attached or the intended market is explicitly limited;
5. SIM/data and safe bench power are available;
6. the operator can configure the destination host/port;
7. the CTO/Hardware Certification SME freezes the tuple for the run.

After freeze, create one run ID and do not substitute another hardware or firmware revision mid-run. A changed tuple starts a new certification run.

## Next gate after freeze

`Protocol Identified` begins with a controlled first connection while raw/sanitized bytes, gateway session logs, device actions and UTC timestamps are captured. Protocol must be proven from the physical candidate; software-family similarity alone is supporting evidence only.
