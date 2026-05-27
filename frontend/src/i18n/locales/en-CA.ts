import enUS from "./en-US";

const enCA: typeof enUS = {
  ...enUS,
  hos_ruleset: "HOS Ruleset (NSC)",
  max_driving_hours: "Max Driving Hours (13hr)",
  default_country: "Default Country (CA)",
  currency: "Currency (CAD)",
  distance_unit: "Distance Unit (km)",
  volume_unit: "Volume Unit (Litres)",
};

export default enCA;
