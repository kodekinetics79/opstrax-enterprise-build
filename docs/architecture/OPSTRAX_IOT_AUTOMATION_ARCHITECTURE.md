# OpsTrax IoT Automation Architecture

## Device / Sensor Coverage
- GPS
- ELD
- Dashcam
- OBD / CAN
- Fuel
- Temperature
- Door
- Tire
- Trailer
- Panic
- RFID / NFC / Bluetooth
- Smart locks
- Yard sensors

## Ingestion Flow
Device -> Signed ingestion -> Normalization -> Event creation -> Risk scoring -> AI reasoning -> Safe action request -> Approval -> Service execution -> Audit

## Safety Rules
- No unsigned or replayable device commands.
- Physical commands must be approval gated.
- Commands must be tenant-scoped and fully audited.
- Fail closed on signature, nonce, or timestamp violations.

