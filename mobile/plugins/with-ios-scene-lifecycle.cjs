const { withAppDelegate, withInfoPlist } = require("@expo/config-plugins");

const sceneConfigurationMethod = `
  public func application(
    _ application: UIApplication,
    configurationForConnecting connectingSceneSession: UISceneSession,
    options: UIScene.ConnectionOptions
  ) -> UISceneConfiguration {
    let configuration = UISceneConfiguration(
      name: "Default Configuration",
      sessionRole: connectingSceneSession.role
    )
    configuration.delegateClass = SceneDelegate.self
    return configuration
  }
`;

const sceneDelegateClass = `
class SceneDelegate: UIResponder, UIWindowSceneDelegate {
  var window: UIWindow?

  func scene(
    _ scene: UIScene,
    willConnectTo session: UISceneSession,
    options connectionOptions: UIScene.ConnectionOptions
  ) {
    guard let windowScene = scene as? UIWindowScene else { return }
    guard
      let appDelegate = UIApplication.shared.delegate as? AppDelegate,
      let factory = appDelegate.reactNativeFactory
    else { return }

    let nextWindow = UIWindow(windowScene: windowScene)
    window = nextWindow
    appDelegate.window = nextWindow

    factory.startReactNative(
      withModuleName: "main",
      in: nextWindow,
      launchOptions: nil
    )

    if !connectionOptions.urlContexts.isEmpty {
      self.scene(scene, openURLContexts: connectionOptions.urlContexts)
    }
  }

  func scene(_ scene: UIScene, openURLContexts URLContexts: Set<UIOpenURLContext>) {
    guard
      let urlContext = URLContexts.first,
      let appDelegate = UIApplication.shared.delegate as? AppDelegate
    else { return }

    var options: [UIApplication.OpenURLOptionsKey: Any] = [
      .openInPlace: urlContext.options.openInPlace,
    ]

    if let sourceApplication = urlContext.options.sourceApplication {
      options[.sourceApplication] = sourceApplication
    }
    if let annotation = urlContext.options.annotation {
      options[.annotation] = annotation
    }

    _ = appDelegate.application(
      UIApplication.shared,
      open: urlContext.url,
      options: options
    )
  }
}
`;

function addInfoPlistSceneManifest(config) {
  return withInfoPlist(config, (nextConfig) => {
    nextConfig.modResults.UIApplicationSceneManifest = {
      UIApplicationSupportsMultipleScenes: false,
      UISceneConfigurations: {
        UIWindowSceneSessionRoleApplication: [
          {
            UISceneConfigurationName: "Default Configuration",
            UISceneDelegateClassName: "$(PRODUCT_MODULE_NAME).SceneDelegate",
          },
        ],
      },
    };
    return nextConfig;
  });
}

function patchAppDelegate(contents) {
  if (contents.includes("class SceneDelegate: UIResponder, UIWindowSceneDelegate")) {
    return contents;
  }

  const startupBlockPattern =
    /#if os\(iOS\) \|\| os\(tvOS\)\n\s*window = UIWindow\(frame: UIScreen\.main\.bounds\)\n\s*factory\.startReactNative\(\n\s*withModuleName: "main",\n\s*in: window,\n\s*launchOptions: launchOptions\)\n#endif/;

  if (!startupBlockPattern.test(contents)) {
    throw new Error(
      "Could not find the Expo AppDelegate startup block for UIScene migration."
    );
  }

  let next = contents.replace(
    startupBlockPattern,
    `#if os(iOS) || os(tvOS)
    if #unavailable(iOS 13.0) {
      window = UIWindow(frame: UIScreen.main.bounds)
      factory.startReactNative(
        withModuleName: "main",
        in: window,
        launchOptions: launchOptions)
    }
#endif`
  );

  if (!next.includes("configurationForConnecting connectingSceneSession")) {
    const linkingMarker = "\n  // Linking API";
    if (!next.includes(linkingMarker)) {
      throw new Error("Could not find the AppDelegate linking marker.");
    }
    next = next.replace(
      linkingMarker,
      `\n${sceneConfigurationMethod}\n  // Linking API`
    );
  }

  const delegateMarker =
    "\nclass ReactNativeDelegate: ExpoReactNativeFactoryDelegate";
  if (!next.includes(delegateMarker)) {
    throw new Error("Could not find ReactNativeDelegate insertion point.");
  }

  return next.replace(
    delegateMarker,
    `\n${sceneDelegateClass}${delegateMarker}`
  );
}

function addAppDelegateSceneLifecycle(config) {
  return withAppDelegate(config, (nextConfig) => {
    if (nextConfig.modResults.language !== "swift") {
      throw new Error("iOS UIScene migration requires a Swift AppDelegate.");
    }
    nextConfig.modResults.contents = patchAppDelegate(
      nextConfig.modResults.contents
    );
    return nextConfig;
  });
}

module.exports = function withIosSceneLifecycle(config) {
  return addAppDelegateSceneLifecycle(
    addInfoPlistSceneManifest(config)
  );
};
