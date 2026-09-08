# G1B Preferred Physical Candidate Research — U.S.-First

**Status:** PROCUREMENT RECOMMENDATION ONLY — NOT A CERTIFICATION  
**Issue:** #109  
**Authority:** `CR-2026-09-03-03`  
**Research date:** 2026-09-03

## Preferred first candidate

**Manufacturer:** Shenzhen Jimi IoT Co., Ltd.  
**Family:** JM-VL03  
**Preferred North-American variant to procure:** **JM-VL03MX / VL03MX** — seller/device labeling must be verified before freeze  
**FCC family identifier:** `2AMLF-JM-VL03`  
**Certification target:** exact physical manufacturer/model/hardware-revision/firmware tuple received, not the family name

## Why this is preferred over a generic “GT06 4G” listing

Manufacturer material for the JM-VL03 family documents a North-American `JM-VL03MX` variant with LTE-FDD bands B2/B4/B5/B7/B12/B13/B17, 9–90 VDC input, Micro-SIM, ACC input, relay output, IP65 enclosure and GNSS tracking. FCC records for `2AMLF-JM-VL03` explicitly list `VL03MX` among the covered adding models. A 2023 FCC permissive-change record also identifies family hardware `VT81-MB` and software `VT81_V141_WAAP_ME_V14.0_230530.1317` for that filing.

This gives the certification run a manufacturer/FCC evidence trail that generic marketplace-clone products usually cannot provide.

## Mandatory caution — protocol remains unverified

Do **not** label this physical candidate “GT06 compatible” from documentation or third-party support lists alone.

Public integration references indicate the JM-VL03 family is handled in the GT06/Concox ecosystem, but 4G Jimi variants have had protocol differences in the field. Therefore OpsTrax will treat the protocol as **UNKNOWN UNTIL PHYSICAL CAPTURE**.

The candidate must pass the normal G1B `Protocol Identified` stage from real device-originated bytes. If its login/location/event dialect differs from the current decoder, that becomes a bounded protocol defect/adapter task followed by the identical physical retest. No guessed parser behavior is accepted.

## Procurement instruction

Order **3 units preferred, 2 minimum**, all explicitly the same North-American `JM-VL03MX`/`VL03MX` variant and same seller/SKU/lot where practical.

Before paying, require the seller/manufacturer to confirm in writing:

1. exact model/variant printed on the device/package;
2. LTE bands B2/B4/B5/B7/B12/B13/B17;
3. FCC ID `2AMLF-JM-VL03` on/for the supplied variant;
4. current hardware revision and firmware version if available;
5. ability to configure custom server hostname/IP and TCP port;
6. ability to use customer-provided SIM/APN;
7. protocol/integration document availability for the exact firmware;
8. no mandatory cloud lock preventing direct TCP delivery to our own gateway;
9. quantity 3 from the same hardware/firmware lot if possible.

If any answer is unclear, do not freeze the candidate. Obtain the units only as a research sample or move to the next exact manufacturer-backed candidate.

## Source references

- Jimi IoT official JM-VL03 leaflet: `https://www.jimilab.com/wp-content/uploads/2022/09/VL03-0609.pdf`
- Jimi IoT JM-VL03 user manual: `https://www.jimiiot.com/wp-content/uploads/2022/09/VL03.pdf`
- FCC family record: `https://fccid.io/2AMLF-JM-VL03`
- FCC 2023 Class II permissive change / VL03MX model inclusion: `https://fcc.report/FCC-ID/2AMLF-JM-VL03/6697437.pdf`

These sources support candidate selection only. The actual supplied label, firmware, bytes, radio behavior, vehicle performance and soak evidence control the OpsTrax certification decision.
