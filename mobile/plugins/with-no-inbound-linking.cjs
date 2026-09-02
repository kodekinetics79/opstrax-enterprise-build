const { withAndroidManifest, withInfoPlist } = require("expo/config-plugins");

function withoutAndroidInboundFilters(config) {
  return withAndroidManifest(config, (next) => {
    const applications = next.modResults.manifest.application || [];
    for (const application of applications) {
      for (const componentType of ["activity", "activity-alias"]) {
        for (const component of application[componentType] || []) {
          component["intent-filter"] = (component["intent-filter"] || []).filter((filter) => {
            const actions = (filter.action || []).map((entry) => entry?.$?.["android:name"]);
            const categories = (filter.category || []).map((entry) => entry?.$?.["android:name"]);
            return !actions.includes("android.intent.action.VIEW") && !categories.includes("android.intent.category.BROWSABLE");
          });
        }
      }
    }
    return next;
  });
}

function withoutIosInboundSchemes(config) {
  return withInfoPlist(config, (next) => {
    delete next.modResults.CFBundleURLTypes;
    return next;
  });
}

module.exports = function withNoInboundLinking(config) {
  return withoutIosInboundSchemes(withoutAndroidInboundFilters(config));
};
