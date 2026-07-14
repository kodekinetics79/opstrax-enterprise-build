namespace Opstrax.Telematics.Contracts.Signals;

/// <summary>
/// Well-known dotted-namespace signal keys for the extensible signal bag, aligned
/// with the COVESA Vehicle Signal Specification (VSS) branch naming. These are
/// <em>conventions</em>, not a closed set: adapters may emit any dotted key, but
/// SHOULD reuse these constants so that cross-adapter consumers can aggregate the
/// same physical quantity without per-vendor mapping tables.
/// </summary>
public static class VssSignals
{
    /// <summary>Cumulative distance travelled, kilometres. Unit: <c>km</c>.</summary>
    public const string Odometer = "Vehicle.Powertrain.Odometer";

    /// <summary>Engine/ignition running state. Unit: unitless boolean.</summary>
    public const string EngineIsRunning = "Vehicle.Powertrain.CombustionEngine.IsRunning";

    /// <summary>Engine coolant temperature. Unit: <c>degC</c>.</summary>
    public const string CoolantTemperature = "Vehicle.Powertrain.CombustionEngine.ECT";

    /// <summary>Fuel tank level as a percentage of capacity. Unit: <c>percent</c>.</summary>
    public const string FuelLevel = "Vehicle.Powertrain.FuelSystem.RelativeLevel";

    /// <summary>Low-voltage system (starter) battery voltage. Unit: <c>V</c>.</summary>
    public const string BatteryVoltage = "Vehicle.LowVoltageBattery.CurrentVoltage";

    /// <summary>Ignition switch position (accessory/on). Unit: unitless boolean.</summary>
    public const string Ignition = "Vehicle.Body.IgnitionOn";

    /// <summary>Vehicle ground speed. Unit: <c>kph</c>.</summary>
    public const string Speed = "Vehicle.Speed";

    /// <summary>Course over ground. Unit: <c>degrees</c>.</summary>
    public const string Heading = "Vehicle.CurrentLocation.Heading";
}
