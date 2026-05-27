# Fleet Backend Starter

This backend creates the first foundation for your global fleet platform.

It supports:

- Country Compliance Layer
  - USA
  - Canada
  - Saudi Arabia
  - UAE
  - Custom Country

- Device Integration Layer
  - OBD-II
  - J1939/CAN
  - GPS trackers
  - Dashcams
  - Temperature sensors
  - Fuel sensors
  - BLE/RFID driver ID
  - Tire pressure sensors

- Industry Module Layer
  - Logistics
  - Cold chain
  - School transport
  - Construction
  - Oil & gas
  - Rental fleet
  - Delivery fleet

## How to install

Place this folder inside your main project folder.

Example:

```txt
your-main-project/
  frontend/
  backend/
```

Rename this folder to:

```txt
backend
```

Then run:

```bash
cd backend
cp .env.example .env
npm install
npm run dev
```

Backend will run at:

```txt
http://localhost:5000
```

## Test health

Open:

```txt
http://localhost:5000/api/health
```

## Test Saudi configuration

Use Postman or Thunder Client:

```txt
POST http://localhost:5000/api/tenant/configure
```

Body:

```json
{
  "tenantId": "demo-saudi-client-001",
  "primaryCountry": "SA",
  "operatingCountries": ["SA"],
  "industries": ["logistics", "cold_chain"],
  "enabledDeviceTypes": [
    "gps_tracker",
    "temperature_sensor",
    "ble_rfid_driver_id",
    "dashcam"
  ]
}
```

Expected result:

The backend will return enabled compliance packs, modules, currency, language, units, and timezone.
