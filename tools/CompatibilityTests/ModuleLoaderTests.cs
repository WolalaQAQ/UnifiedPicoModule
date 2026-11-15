#if NATIVE_HOST
using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using UnifiedPicoModule.Configuration;
using UnifiedPicoModule.Sdk;
using VRCFaceTracking;
using VRCFaceTracking.Core.Library;
using VRCFaceTracking.Core.Params.Data;
using VRCFaceTracking.Core.Params.Expressions;
using VRCFaceTracking.ModuleProcess;

namespace CompatibilityTests;

internal static partial class Program
{
    private static void ModuleLoaderTests()
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "module-loader");
        Run("official6-module-loader-v5-dll-lifecycle", () =>
        {
            Directory.CreateDirectory(directory);
            var copiedDllPath = Path.Combine(directory, "UnifiedPicoModule.dll");
            File.Copy(typeof(UnifiedPicoTrackingModule).Assembly.Location, copiedDllPath, overwrite: true);
            File.WriteAllText(Path.Combine(directory, "UnifiedPicoModule.json"),
                JsonSerializer.Serialize(new ModuleConfig { DisableEyeTracking = true, DisableFaceTracking = true }));

            Check(typeof(UnifiedTracking).Assembly.GetName().Version == new Version(6, 0, 0, 0), "Official Core 6 is loaded");
            Check(typeof(ExtTrackingModule).Assembly.GetName().Version == new Version(6, 0, 0, 0), "Official SDK 6 is loaded");
            var previousData = UnifiedTracking.Data;
            var data = new UnifiedTrackingData();
            data.Shapes[(int)UnifiedExpressions.JawOpen].Weight = 0.37f;
            data.Eye.Left.Gaze.x = -0.29f;
            UnifiedTracking.Data = data;
            ExtTrackingModule? module = null;
            AssemblyLoadContext? context = null;
            try
            {
                var loader = new ModuleAssembly(NullLogger<ModuleProcessMain>.Instance, NullLoggerFactory.Instance, copiedDllPath);
                module = loader.TrackingModule;
                Check(module is not null, "Official ModuleAssembly resolves the module implementation");
                var loaded = module!;
                var moduleType = loaded.GetType();
                context = AssemblyLoadContext.GetLoadContext(moduleType.Assembly);
                Check(context is not null && context != AssemblyLoadContext.Default && context.IsCollectible,
                    "Official loader uses a separate collectible module context");
                Check(moduleType.FullName == typeof(UnifiedPicoTrackingModule).FullName && moduleType != typeof(UnifiedPicoTrackingModule),
                    "The copied module was loaded separately, not reused from the test assembly context");
                Check(moduleType.Assembly.Location == copiedDllPath, "Official loader loads the copied DLL path");
                Check(moduleType.BaseType == typeof(ExtTrackingModule), "Loaded module shares the host SDK base type");
                Check(ReferenceEquals(loader.TrackingModule, loaded), "ModuleAssembly caches its tracking module instance");
                Check(loaded.Logger is not null, "Official loader assigns the module logger");

                var references = moduleType.Assembly.GetReferencedAssemblies();
                foreach (var hostAssembly in new[] { typeof(ExtTrackingModule).Assembly, typeof(UnifiedTracking).Assembly })
                {
                    var reference = references.Single(a => a.Name == hostAssembly.GetName().Name);
                    Check(reference.Version == new Version(5, 4, 5, 0), $"The tested DLL references {reference.Name} 5.4.5");
                    Check(ReferenceEquals(context!.LoadFromAssemblyName(reference), hostAssembly),
                        $"The module's {reference.Name} 5.4.5 reference binds to the shared official 6 assembly");
                }
                Check(!context!.Assemblies.Any(a => a.GetName().Name is "VRCFaceTracking.SDK" or "VRCFaceTracking.Core"),
                    "The module context does not privately load another SDK or Core");
                Console.WriteLine($"LOADER: {moduleType.Assembly.GetName().Name} in {context.Name}; SDK/Core 5.4.5 references share host 6.");

                Check(loaded.Supported == (true, true), "Official loader exposes both supported tracking groups");
                Check(loaded.Initialize(false, false) == (false, false), "No host allocations enable no tracking groups");
                Check(loaded.Status == ModuleState.Idle && loaded.ModuleInformation.Active, "Disabled module initializes idle");
                Check(loaded.ModuleInformation.Name == "Unified PICO Face Tracking", "Official-loaded Initialize populates metadata");
                Check(moduleType.GetField("coordinator", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(loaded) is null,
                    "Disabled official-loaded module starts no tracking sources");
                Check(moduleType.GetField("timerPolicyAttempted", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(loaded) is false,
                    "Disabled official-loaded module does not attempt timer policy changes");
                loaded.Update();
                Near(data.Shapes[(int)UnifiedExpressions.JawOpen].Weight, 0.37f, "Disabled loader update preserves host mouth data");
                Near(data.Eye.Left.Gaze.x, -0.29f, "Disabled loader update preserves host eye data");
                loaded.Teardown();
                loaded.Teardown();
                loaded.Update();
                Check(loaded.Status == ModuleState.Uninitialized && !loaded.ModuleInformation.Active,
                    "Official-loaded teardown is idempotent and update after stop stays stopped");
                Check(ReferenceEquals(UnifiedTracking.Data, data), "Module lifecycle uses the shared host tracking container");
                Near(data.Shapes[(int)UnifiedExpressions.JawOpen].Weight, 0.37f, "Disabled loader teardown preserves foreign mouth data");
                Near(data.Eye.Left.Gaze.x, -0.29f, "Disabled loader teardown preserves foreign eye data");
            }
            finally
            {
                try
                {
                    module?.Teardown();
                }
                finally
                {
                    if (module?.ModuleInformation.StaticImages is { } images)
                        foreach (var image in images) image.Dispose();
                    UnifiedTracking.Data = previousData;
                    context?.Unload();
                }
            }
        });

        Run("official6-module-loader-invalid-path-and-extension", () =>
        {
            Directory.CreateDirectory(directory);
            var missingPath = Path.Combine(directory, "missing", "UnifiedPicoModule.dll");
            var wrongExtensionPath = Path.Combine(directory, "not-a-module.txt");
            File.WriteAllText(wrongExtensionPath, "Not a DLL.");
            foreach (var path in new[] { missingPath, wrongExtensionPath })
            {
                var rejected = false;
                try
                {
                    _ = new ModuleAssembly(NullLogger<ModuleProcessMain>.Instance, NullLoggerFactory.Instance, path);
                }
                catch (ArgumentException)
                {
                    rejected = true;
                }
                Check(rejected, $"Official loader rejects invalid module path: {path}");
            }
        });
    }
}
#endif
