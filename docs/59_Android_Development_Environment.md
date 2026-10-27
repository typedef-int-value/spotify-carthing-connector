# Android development environment

## Verified toolchain

| Component | Installed version |
|---|---|
| .NET SDK | 10.0.401 |
| MAUI Android workload | 10.0.20 |
| .NET Android SDK pack | 36.1.69 |
| Android platform | API 36 |
| Android Build Tools | 36.0.0 |
| Android Platform Tools | 36.0.0-13206524 |
| Microsoft OpenJDK, installed by the dependency target | 17.0.14 |

## Local paths

| User environment variable | Value |
|---|---|
| `AndroidSdkDirectory` | `%LOCALAPPDATA%\Android\Sdk` |
| `ANDROID_HOME` | `%LOCALAPPDATA%\Android\Sdk` |
| `JavaSdkDirectory` | `%LOCALAPPDATA%\Android\jdk` |

The variables contain expanded absolute paths. Restart existing terminals or IDEs to inherit them. Existing JDK 25 and `JAVA_HOME` were preserved; Android builds use `JavaSdkDirectory` instead. The existing `JAVA_HOME` path failed JDK validation during initial setup.

## Installation

```powershell
dotnet workload install maui-android --skip-manifest-update --disable-parallel

# Run against an Android-targeted project. This also accepts the Android SDK licenses.
dotnet build <project.csproj> -t:InstallAndroidDependencies -f net10.0-android `
  "-p:AndroidSdkDirectory=$env:LOCALAPPDATA\Android\Sdk" `
  "-p:JavaSdkDirectory=$env:LOCALAPPDATA\Android\jdk" `
  -p:AcceptAndroidSDKLicenses=True
```

The dependency target selects the SDK and JDK components required by the project. Installation reference: [Microsoft Android dependency setup](https://learn.microsoft.com/en-us/dotnet/android/getting-started/installation/dependencies).

## Build validation

- Generated MAUI sample under `analysis/android-environment-check/`, targeting only `net10.0-android`.
- Debug build: 0 warnings, 0 errors.
- Generated debug-signed APK: `analysis/android-environment-check/bin/Debug/net10.0-android/com.companyname.androidenvironmentcheck-Signed.apk`.
- Signed APK size: 16,265,645 bytes.
- Logs: `analysis/android-workload-install.log`, `analysis/android-dependencies-install.log`, `analysis/android-environment-build.log`.

```powershell
dotnet build analysis/android-environment-check/AndroidEnvironmentCheck.csproj `
  -f net10.0-android -c Debug `
  "-p:AndroidSdkDirectory=$env:LOCALAPPDATA\Android\Sdk" `
  "-p:JavaSdkDirectory=$env:LOCALAPPDATA\Android\jdk"
```

For device debugging, enable USB debugging on the Android phone and accept its computer authorization prompt. Use `%LOCALAPPDATA%\Android\Sdk\platform-tools\adb.exe` for this SDK's ADB installation.
