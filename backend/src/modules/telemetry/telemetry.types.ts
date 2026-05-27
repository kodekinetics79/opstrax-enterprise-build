export interface TelemetryEvent {
  tenantId: string;
  vehicleId: string;
  driverId?: string;
  deviceId: string;
  deviceType: string;
  providerName: string;
  timestamp: string;
  countryCode: string;

  location?: {
    latitude: number;
    longitude: number;
    speed?: number;
    heading?: number;
  };

  engine?: {
    ignitionStatus?: boolean;
    rpm?: number;
    odometer?: number;
    engineHours?: number;
    fuelLevel?: number;
    diagnosticCodes?: string[];
  };

  safety?: {
    harshBrake?: boolean;
    harshAcceleration?: boolean;
    collisionDetected?: boolean;
    driverDistraction?: boolean;
    seatbeltStatus?: boolean;
  };

  coldChain?: {
    temperature?: number;
    humidity?: number;
    doorOpen?: boolean;
    reeferStatus?: string;
  };

  tires?: {
    pressure?: number;
    temperature?: number;
    tirePosition?: string;
  };

  rawPayload?: Record<string, unknown>;
}
