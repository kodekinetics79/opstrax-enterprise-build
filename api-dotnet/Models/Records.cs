namespace Opstrax.Api.Models;

public sealed record DashboardSummary(
    int ActiveVehicles,
    int JobsInProgress,
    decimal AverageSafetyScore,
    int MaintenanceDue,
    int ComplianceRisks,
    decimal WeeklyFuelCost,
    IReadOnlyList<AiInsightDto> Insights
);

public sealed record VehicleDto(
    long Id,
    string VehicleCode,
    string Type,
    string? Make,
    string? Model,
    int? Year,
    string? Vin,
    string? PlateNumber,
    decimal OdometerMiles,
    string Status,
    string? AssignedDriver
);

public sealed record CreateVehicleRequest(
    string VehicleCode,
    string Type,
    string? Make,
    string? Model,
    int? Year,
    string? Vin,
    string? PlateNumber,
    string Status
);

public sealed record DriverDto(
    long Id,
    string DriverCode,
    string FullName,
    string? Phone,
    string? Email,
    string? LicenseNumber,
    DateTime? LicenseExpiry,
    decimal SafetyScore,
    string Status
);

public sealed record CreateDriverRequest(
    string DriverCode,
    string FullName,
    string? Phone,
    string? Email,
    string? LicenseNumber,
    DateTime? LicenseExpiry,
    string Status
);

public sealed record JobDto(
    long Id,
    string JobCode,
    string CustomerName,
    string JobType,
    string? PickupAddress,
    string? DropoffAddress,
    DateTime? ScheduledStart,
    DateTime? ScheduledEnd,
    string Status,
    string Priority,
    string? VehicleCode,
    string? DriverName
);

public sealed record CreateJobRequest(
    string JobCode,
    string CustomerName,
    string JobType,
    string? PickupAddress,
    string? DropoffAddress,
    DateTime? ScheduledStart,
    DateTime? ScheduledEnd,
    string Priority,
    long? AssignedVehicleId,
    long? AssignedDriverId
);

public sealed record WorkOrderDto(
    long Id,
    string WorkOrderCode,
    string VehicleCode,
    string Title,
    string Priority,
    string Status,
    DateTime? DueDate,
    decimal? EstimatedCost
);

public sealed record AssetDto(
    long Id,
    string AssetCode,
    string AssetType,
    string Name,
    string Status,
    string? CurrentLocation,
    string? AssignedVehicle
);

public sealed record FuelTransactionDto(
    long Id,
    string VehicleCode,
    decimal Gallons,
    decimal TotalCost,
    string? FuelStation,
    DateTime TransactionTime
);

public sealed record CreateWorkOrderRequest(
    long VehicleId,
    string WorkOrderCode,
    string Title,
    string Priority,
    DateTime? DueDate,
    decimal? EstimatedCost
);

public sealed record AiInsightDto(
    long Id,
    string InsightType,
    string Title,
    string Body,
    string Severity,
    string Status,
    DateTime CreatedAt
);

public sealed record LocationEventDto(
    long Id,
    string? VehicleCode,
    string? DriverCode,
    decimal Lat,
    decimal Lng,
    decimal SpeedMph,
    decimal? Heading,
    string EventType,
    DateTime EventTime
);
