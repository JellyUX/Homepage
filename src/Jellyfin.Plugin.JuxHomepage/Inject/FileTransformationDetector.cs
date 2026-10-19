using System.Reflection;
using System.Runtime.Loader;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;

namespace Jellyfin.Plugin.JuxHomepage.Inject;

/// <summary>
/// Detects and bridges to the FileTransformation plugin at runtime via reflection.
/// Caches the resolved MethodInfo after the first successful lookup.
/// </summary>
public class FileTransformationDetector
{
    private const string AssemblyNameFragment = ".FileTransformation";
    private const string PluginInterfaceTypeName = "Jellyfin.Plugin.FileTransformation.PluginInterface";

    private readonly ILogger<FileTransformationDetector> _logger;
    private MethodInfo? _registerMethod;
    private bool _resolved;

    /// <summary>
    /// Initializes a new instance of the <see cref="FileTransformationDetector"/> class.
    /// </summary>
    /// <param name="logger">Logger.</param>
    public FileTransformationDetector(ILogger<FileTransformationDetector> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Returns the version of the loaded FileTransformation assembly: the fallback of the admin warning when
    /// the plugin manager does not list FileTransformation under its id (DEC-P05-6).
    /// </summary>
    /// <returns>The assembly version, or null when FileTransformation is not loaded.</returns>
    public Version? GetAssemblyVersion() => FindAssembly()?.GetName().Version;

    /// <summary>
    /// Returns true if the FileTransformation plugin assembly is loaded and
    /// the RegisterTransformation entry point is reachable.
    /// Result is cached after the first successful resolution.
    /// </summary>
    public bool IsAvailable()
    {
        if (_resolved && _registerMethod is not null)
        {
            return true;
        }

        var assembly = FindAssembly();
        if (assembly is null)
        {
            return false;
        }

        var type = assembly.GetType(PluginInterfaceTypeName);
        if (type is null)
        {
            _logger.LogError(
                "FileTransformation assembly found but type {Type} is missing.",
                PluginInterfaceTypeName);
            return false;
        }

        _registerMethod = type.GetMethod("RegisterTransformation");
        if (_registerMethod is null)
        {
            _logger.LogError(
                "FileTransformation type found but RegisterTransformation method is missing.");
            return false;
        }

        _resolved = true;
        return true;
    }

    /// <summary>
    /// Registers a file transformation via the FileTransformation plugin.
    /// Must be called only after <see cref="IsAvailable"/> returns true.
    /// </summary>
    /// <param name="payload">
    /// JObject matching TransformationRegistrationPayload (id, fileNamePattern,
    /// callbackAssembly, callbackClass, callbackMethod).
    /// </param>
    public void RegisterTransformation(JObject payload)
    {
        if (_registerMethod is null)
        {
            _logger.LogError(
                "RegisterTransformation called but FileTransformation is not available.");
            return;
        }

        try
        {
            _registerMethod.Invoke(null, new object?[] { payload });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "FileTransformation RegisterTransformation invocation failed.");
        }
    }

    private static Assembly? FindAssembly() => AssemblyLoadContext.All
        .SelectMany(ctx => ctx.Assemblies)
        .FirstOrDefault(a => a.FullName?.Contains(AssemblyNameFragment, StringComparison.Ordinal) ?? false);
}
