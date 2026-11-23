using Llano.App;
using Llano.Core;

if (args is ["--probe"] or ["--listen"])
{
    if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
    var devices = HidCooler.Find();
    Console.WriteLine($"Found {devices.Count} V12 Ultra HID interface(s).");
    foreach (var device in devices)
    {
        Console.WriteLine(device.Identity);
        if (device.ReadStatus() is { } status)
            Console.WriteLine($"Power: {(status.Powered ? "on" : "off")}. Reported speed setting: {status.SettingRpm} RPM. RGB effect: {status.LightingEffectIndex}, " +
                $"color: {status.LightingColorIndex}, motion level: {status.LightingSpeed + 1}, brightness: {status.BrightnessPercent}%.");
    }
    if (args[0] == "--listen" && devices.Count == 1)
    {
        Console.WriteLine("Listening for 15 seconds. Move the cooler dial or press the RGB mode button.");
        Console.WriteLine(await devices[0].CaptureAsync(CancellationToken.None));
    }
    return devices.Count == 1 ? 0 : 1;
}

var limits = new SpeedLimits(300, 2800, 100);
var curve = new[] { new CurvePoint(40, 800), new CurvePoint(80, 2400) };
var profile = new FanProfile { SharedSensors = ["cpu", "gpu"], SharedCurve = curve, MaximumRpm = 2330 };
var controller = new FanController(limits);
Equal<V12UltraStatus?>(new(800, 1, 1), V12UltraStatus.Decode(Convert.FromHexString("00881400010102144B")), "800 RPM status fixture");
Equal<V12UltraStatus?>(new(1200, 1, 1), V12UltraStatus.Decode(Convert.FromHexString("00882400010102143B")), "1200 RPM status fixture");
Equal<V12UltraStatus?>(null, V12UltraStatus.Decode(Convert.FromHexString("00882400010102143A")), "reject wrong status checksum");
Equal<V12UltraStatus?>(null, V12UltraStatus.Decode(Convert.FromHexString("008824")), "reject a truncated status report");
var softwareStatus = V12UltraStatus.Decode(Convert.FromHexString("00802800010102282B"));
Equal<V12UltraStatus?>(new(1300, 1, 1, SoftwareControl: true, Brightness: 40), softwareStatus, "software control status fixture");
Equal<V12UltraStatus?>(new(1200, 1, 1, LightingEnabled: false),
    V12UltraStatus.Decode(Convert.FromHexString("0088240081010214BB")), "lights-off status fixture");
Equal("0001280001010214BE", Convert.ToHexString(new V12UltraStatus(1200, 1, 1).EncodeCommand(1300, null)), "switch from dial to software");
Equal("0000240001010214C3", Convert.ToHexString(softwareStatus!.EncodeCommand(1200, new(true, LightingEffect.Breathing, "#4080FF", 8))), "software speed and lighting command");
var lightingCommand = new V12UltraStatus(1200, 0, 0).EncodeCommand(null,
    new(true, LightingEffect.ColorChase, V12UltraStatus.Palette[0], 70, 3));
Equal("00002400030003B322", Convert.ToHexString(lightingCommand), "encode motion and brightness without taking fan ownership");
Equal(70, new V12UltraStatus(1200, 0, 0, Brightness: lightingCommand[7]).BrightnessPercent, "normalize hardware brightness");
Equal(0, V12UltraStatus.PaletteIndex("#FF3030"), "preserve old red palette slot");
Equal(4, V12UltraStatus.PaletteIndex("#FFD83D"), "preserve old amber palette slot");
Equal("0000280101010228AA", Convert.ToHexString(softwareStatus!.EncodePowerCommand(false)),
    "turn off without changing the fan or lighting settings");
Equal("0000280001010228AB", Convert.ToHexString((softwareStatus! with { Powered = false }).EncodePowerCommand(true)),
    "turn on without asking for software fan ownership");

Equal(800d, FanController.Interpolate(curve, 20), "interpolate below the first point");
Equal(1600d, FanController.Interpolate(curve, 60), "interpolate between points");
Equal(2400d, FanController.Interpolate(curve, 95), "interpolate above the last point");
Equal(2300, limits.Quantize(2330, 350, 2330), "quantize within a non-step maximum");
Equal(400, limits.Quantize(350, 350, 2330), "quantize within a non-step minimum");
Equal(false, limits.ContainsStep(351, 399), "reject a range with no device step");

controller.Start(profile);
Equal(FanState.WaitingForCooler, controller.Update(At(0), Readings(0, 55, 50), false).State, "wait for the cooler");
Equal(FanState.WaitingForSensors, controller.Update(At(1), Readings(1, null, 50), true).State, "require all sensors before arming");
Equal(1600, controller.Update(At(2), Readings(2, 55, 60), true).NewCommand, "use the hotter temperature");
Equal(null, controller.Update(At(3), Readings(3, 55, 60), true).NewCommand, "suppress repeated RPM writes");
Equal(null, controller.Update(At(4), Readings(4, 57, 50), true).NewCommand, "hold speed within hysteresis");
Equal(null, controller.Update(At(5), Readings(5, 45, 50), true).NewCommand, "start cooldown");
Equal(null, controller.Update(At(14), Readings(14, 45, 50), true).NewCommand, "hold speed for ten seconds");
Equal(1300, controller.Update(At(15), Readings(15, 45, 50), true).NewCommand, "decrease after cooldown and hysteresis");
Equal(2000, controller.Update(At(16), Readings(16, 70, 50), true).NewCommand, "increase on the next tick");
Equal(new FanDecision(FanState.SensorFallback, 2300, 2300),
    controller.Update(At(22), Readings(16, 70, 50), true), "fall back to the profile maximum for stale readings");
Equal(null, controller.Update(At(23), Readings(23, 52, 50), true).NewCommand, "hold fallback RPM during recovery cooldown");
Equal(1400, controller.Update(At(33), Readings(33, 52, 50), true).NewCommand, "recover after fresh samples and cooldown");
Equal(FanState.WaitingForCooler, controller.Update(At(34), Readings(34, 70, 50), false).State, "invalidate on disconnect");
Equal(FanState.WaitingForSensors, controller.Update(At(35), Readings(29, 70, 50), true).State, "require new readings after reconnect");
Equal(2000, controller.Update(At(36), Readings(36, 70, 50), true).NewCommand, "resume after reconnect");
Equal(FanState.DialOverride, controller.Update(At(37), Readings(37, 70, 50), true, true).State, "yield to the physical dial");
Equal(FanState.DialOverride, controller.Update(At(38), Readings(38, 70, 50), true).State, "stay paused after dial override");

var separate = profile with
{
    Mode = FanMode.SeparateCurves,
    CpuSensor = "cpu",
    GpuSensor = "gpu",
    CpuCurve = curve,
    GpuCurve = [new(40, 1300), new(60, 2300)]
};
controller.Start(separate);
Equal(1800, controller.Update(At(1), Readings(1, 55, 50), true).NewCommand, "use the higher CPU/GPU RPM request");
Equal(FanState.SensorFallback, controller.Update(At(2), Readings(2, 60, null), true).State, "flag either separate sensor missing");
controller.Start(profile with { Mode = FanMode.Fixed, FixedRpm = 1270 });
Equal(1300, controller.Update(At(0), Readings(0, null, null), true).NewCommand, "fixed mode works without temperature sensors");
controller.Pause();
Equal(FanState.Paused, controller.Update(At(1), Readings(1, 65, 65), true).State, "pause suppresses commands");

var balanced = CoolingPresets.Create(CoolingMode.Balanced, 0);
controller.Start(balanced);
Equal(FanState.WaitingForSensors, controller.Update(At(0), Readings(0, null, null), true).State, "automatic waits for a CPU input");
Equal(600, controller.Update(At(1), Readings(1, 50, null, automatic: true), true).NewCommand, "automatic starts from CPU alone");
Equal(null, controller.Update(At(2), Readings(2, 98, null, automatic: true), true).NewCommand, "single CPU burst does not send an RPM change");
Equal(null, controller.Update(At(3), Readings(3, 50, null, automatic: true), true).NewCommand, "return to normal without a speed jump");
for (int second = 4; second <= 11; second++)
    Equal(null, controller.Update(At(second), Readings(second, second % 2 == 0 ? 61 : 49, null, automatic: true), true).NewCommand,
        "oscillating temperatures stay inside the speed band");
int previous = 600;
int lastIncrease = 0;
for (int second = 12; second <= 29; second++)
{
    var decision = controller.Update(At(second), Readings(second, 52, 78, automatic: true), true);
    int rpm = decision.TargetRpm!.Value;
    if (rpm != previous)
    {
        Equal(true, rpm > previous && rpm - previous <= 200, "normal ramp increases by at most 200 RPM");
        Equal(true, second - lastIncrease >= 2, "normal ramp waits at least two seconds between increases");
        lastIncrease = second;
    }
    previous = rpm;
}
Equal(true, previous >= 1800, "sustained GPU heat raises RPM with a cool CPU");
for (int second = 30; second < 45; second++)
{
    var decision = controller.Update(At(second), Readings(second, 50, null, automatic: true), true);
    Equal(FanState.Smart, decision.State, "sleeping GPU does not trigger fallback");
    Equal(previous, decision.TargetRpm, "cooldown holds existing speed for 15 seconds");
}
Equal(previous - 100, controller.Update(At(45), Readings(45, 50, null, automatic: true), true).NewCommand, "cooldown steps down by 100 RPM");
Equal(null, controller.Update(At(47), Readings(47, 50, null, automatic: true), true).NewCommand, "cooldown waits at least three seconds between drops");
Equal(previous - 200, controller.Update(At(48), Readings(48, 50, null, automatic: true), true).NewCommand, "cooldown continues without restarting the hold");
Equal(2800, controller.Update(At(54), Readings(54, null, 50, automatic: true), true).NewCommand, "CPU loss triggers immediate fallback even when the GPU is readable");
Equal(2800, controller.Update(At(55), Readings(55, 50, null, automatic: true), true).TargetRpm, "fallback recovery starts with a hold");

controller.Start(balanced);
Equal(1000, controller.Update(At(0), Readings(0, 50, null, automatic: true), true, currentRpm: 1000).NewCommand, "applying a new preset begins at the reported speed");
Equal(null, controller.Update(At(1), Readings(1, 98, null, automatic: true), true).NewCommand, "high-temperature confirmation waits for a second sample");
Equal(null, controller.Update(At(2), Readings(1, 98, null, automatic: true), true).NewCommand, "repeated sample does not confirm critical heat");
Equal(FanMotion.HighTemperature, controller.Update(At(3), Readings(3, 98, null, automatic: true), true).Motion, "sustained critical heat bypasses rate limits");
controller.Start(balanced);
Equal(2800, controller.Update(At(0), Readings(0, 50, 93, automatic: true), true, currentRpm: 600).NewCommand, "extreme GPU heat applies maximum immediately");
Equal(FanState.DialOverride, controller.Update(At(1), Readings(1, 50, 93, automatic: true), true, physicalOverride: true).State, "automatic mode yields to the physical dial");
controller.Start(CoolingPresets.Create(CoolingMode.Quiet, 0));
Equal(new FanDecision(FanState.WaitingForSensors, null, null),
    controller.Update(At(0), Readings(0, null, 45, automatic: true), true), "GPU alone cannot start an automatic preset");
Equal(300, controller.Update(At(1), Readings(1, 50, 45, automatic: true), true).TargetRpm, "quiet holds 300 RPM at light load");
controller.Start(CoolingPresets.Create(CoolingMode.Quiet, 0));
Equal(400, controller.Update(At(0), Readings(0, 49, 45, automatic: true), true, currentRpm: 400).TargetRpm, "quiet starts from the current speed");
Equal(300, controller.Update(At(15), Readings(15, 49, 45, automatic: true), true).TargetRpm, "quiet reaches 300 after cooldown");
controller.Start(CoolingPresets.Create(CoolingMode.Boost, 0));
Equal(1000, controller.Update(At(0), Readings(0, 50, 45, automatic: true), true).NewCommand, "boost runs faster under the same temperatures");

controller.Start(balanced, waitForStableTemperatures: true);
Equal(FanState.StabilizingSensors, controller.Update(At(0), Readings(0, 103, 93, automatic: true), true).State,
    "sign-in ignores an initial temperature spike");
Equal(null, controller.Update(At(4), Readings(0, 50, null, automatic: true), true).NewCommand,
    "repeated startup snapshot cannot arm cooling");
Equal(FanState.WaitingForSensors, controller.Update(At(5), Readings(5, null, 50, automatic: true), true).State,
    "a missing CPU resets startup settling without a maximum RPM fallback");
for (int second = 6; second <= 10; second++)
    Equal(null, controller.Update(At(second), Readings(second, 50, null, automatic: true), true, currentRpm: 600).NewCommand,
        "sign-in leaves the fan alone during settling");
Equal(600, controller.Update(At(11), Readings(11, 103, 93, automatic: true), true, currentRpm: 600).NewCommand,
    "a single critical reading when sign-in cooling starts keeps the observed RPM");
Equal(null, controller.Update(At(12), Readings(12, null, 50, automatic: true), true).NewCommand,
    "a brief CPU dropout as sign-in cooling starts does not command maximum RPM");
Equal(null, controller.Update(At(13), Readings(13, 51, null, automatic: true), true).NewCommand,
    "sign-in cooling continues from the same speed when CPU readings return");
Equal(2800, controller.Update(At(17), Readings(17, null, 50, automatic: true), true).NewCommand,
    "the normal loss safeguard applies once sign-in has settled");

controller.Start(balanced, waitForStableTemperatures: true);
for (int second = 0; second < 5; second++)
    controller.Update(At(second), Readings(second, 50, null, automatic: true), true, currentRpm: 600);
Equal(600, controller.Update(At(5), Readings(5, 103, null, automatic: true), true, currentRpm: 600).NewCommand,
    "critical heat at the end of sign-in settling needs confirmation");
controller.Update(At(6), Readings(6, 103, null, automatic: true), true);
Equal(2800, controller.Update(At(7), Readings(7, 103, null, automatic: true), true).NewCommand,
    "continuous critical heat still commands maximum during sign-in");

string testSettings = Path.Combine(Path.GetTempPath(), $"Llano-check-{Guid.NewGuid():N}.json");
try
{
    var store = new SettingsStore(testSettings, limits);
    Equal("Balanced", store.Load().SelectedProfile, "default settings");
    Equal(false, store.Load().PowerOffAtShutdown, "cooler shutdown behavior is opt-in");
    File.WriteAllText(testSettings, """{"Version":1,"Profiles":[{"Name":"Legacy"}],"SelectedProfile":"Legacy","ManualRpm":1400}""");
    var legacy = store.Load();
    Equal("Legacy", legacy.SelectedProfile, "preserve the saved legacy profile");
    Equal(1400, legacy.ManualRpm, "preserve legacy cooling preferences");
    Equal(AppTheme.EmberRed, legacy.Theme, "legacy settings use the default theme");
    Equal(true, legacy.CloseToTray, "legacy settings preserve close-to-tray");
    Equal(false, legacy.RequestAdministratorOnLaunch, "existing settings do not request administrator access automatically");
    foreach (var theme in Enum.GetValues<AppTheme>())
    {
        store.Save(legacy with { Theme = theme, CloseToTray = false });
        var restored = store.Load();
        Equal(theme, restored.Theme, "persist each app theme");
        Equal(false, restored.CloseToTray, "persist close-to-tray preference");
    }
    var lighting = new Lighting(true, LightingEffect.Breathing, "#123456", 60);
    store.Save(new AppSettings { Profiles = [separate with { Name = "Gaming", Lighting = lighting }], SelectedProfile = "Gaming", ResumeAtSignIn = true });
    var loaded = store.Load();
    Equal("Gaming", loaded.SelectedProfile, "persist selection");
    Equal("cpu", loaded.Profiles[0].CpuSensor, "persist sensor identifier");
    Equal(lighting, loaded.Profiles[0].Lighting, "persist lighting");
    Equal(true, loaded.ResumeAtSignIn, "persist sign-in selection");
    var newLight = new Lighting(true, LightingEffect.ColorChase, V12UltraStatus.Palette[0], 40, 3);
    store.Save(loaded with { LastCoolingMode = CoolingMode.Manual, ManualRpm = 1400, LastLighting = newLight, PowerOffAtShutdown = true,
        RequestAdministratorOnLaunch = true, LightingPresets = [new("Evening", newLight)] });
    var updated = store.Load();
    Equal(CoolingMode.Manual, updated.LastCoolingMode, "persist last cooling mode");
    Equal(1400, updated.ManualRpm, "persist selected manual speed");
    Equal(true, updated.PowerOffAtShutdown, "persist cooler shutdown preference");
    Equal(true, updated.RequestAdministratorOnLaunch, "persist administrator launch preference");
    Equal(newLight, updated.LastLighting, "persist last lighting look");
    Equal(new LightingPreset("Evening", newLight), updated.LightingPresets.Single(), "persist a custom lighting preset");
    store.Save(new());
    Equal("Balanced", store.Load().SelectedProfile, "atomically replace settings");
}
finally { File.Delete(testSettings); }

string settingsDirectory = Path.Combine(Path.GetTempPath(), $"Llano-uninstall-check-{Guid.NewGuid():N}");
Directory.CreateDirectory(settingsDirectory);
string config = Path.Combine(settingsDirectory, "settings.json");
string temporaryConfig = Path.Combine(settingsDirectory, $"{Guid.NewGuid():N}.tmp");
string retainedFile = Path.Combine(settingsDirectory, "notes.tmp");
try
{
    File.WriteAllText(config, "{}");
    File.WriteAllText(temporaryConfig, "{}");
    File.WriteAllText(retainedFile, "keep this");
    AppDataFiles.RemoveSettings(settingsDirectory);
    Equal(false, File.Exists(config), "uninstall removes the saved settings");
    Equal(false, File.Exists(temporaryConfig), "uninstall removes interrupted settings writes");
    Equal(true, File.Exists(retainedFile), "uninstall preserves files it did not create");
    File.Delete(retainedFile);
    AppDataFiles.RemoveSettings(settingsDirectory);
    Equal(false, Directory.Exists(settingsDirectory), "uninstall removes the empty app data folder");
    AppDataFiles.RemoveSettings(settingsDirectory);
}
finally
{
    if (Directory.Exists(settingsDirectory))
    {
        File.Delete(config);
        File.Delete(temporaryConfig);
        File.Delete(retainedFile);
        Directory.Delete(settingsDirectory);
    }
}

Console.WriteLine("All fan-control and settings checks passed.");
return 0;

static void Equal<T>(T expected, T actual, string name)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new InvalidOperationException($"{name}: expected {expected}, got {actual}.");
}

static TimeSpan At(int seconds) => TimeSpan.FromSeconds(seconds);

static Dictionary<string, Temperature> Readings(int seconds, double? cpu, double? gpu, bool automatic = false)
{
    var readings = new Dictionary<string, Temperature>();
    if (cpu is { } c) readings.Add(automatic ? CoolingPresets.CpuInput : "cpu", new(c, At(seconds)));
    if (gpu is { } g) readings.Add(automatic ? CoolingPresets.GpuInput : "gpu", new(g, At(seconds)));
    return readings;
}
