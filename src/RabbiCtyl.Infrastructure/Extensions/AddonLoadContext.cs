using System.Reflection;
using System.Runtime.Loader;
using RabbiCtyl.Addons.Abstractions;
using RabbiCtyl.Core.Entities;

namespace RabbiCtyl.Infrastructure.Extensions;

internal sealed class AddonLoadContext : AssemblyLoadContext
{
    private static readonly HashSet<string> SharedAssemblies = new(StringComparer.OrdinalIgnoreCase)
    {
        typeof(IAddon).Assembly.GetName().Name!,
        typeof(Entity).Assembly.GetName().Name!,
    };

    private readonly AssemblyDependencyResolver _resolver;

    public AddonLoadContext(string addonAssemblyPath)
        : base($"addon:{Path.GetFileNameWithoutExtension(addonAssemblyPath)}", isCollectible: true)
    {
        _resolver = new AssemblyDependencyResolver(addonAssemblyPath);
    }

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        if (assemblyName.Name is not null && SharedAssemblies.Contains(assemblyName.Name))
        {
            return Default.Assemblies.FirstOrDefault(
                assembly => string.Equals(assembly.GetName().Name, assemblyName.Name, StringComparison.OrdinalIgnoreCase));
        }

        var path = _resolver.ResolveAssemblyToPath(assemblyName);

        return path is null ? null : LoadFromAssemblyPath(path);
    }

    protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
    {
        var path = _resolver.ResolveUnmanagedDllToPath(unmanagedDllName);

        return path is null ? IntPtr.Zero : LoadUnmanagedDllFromPath(path);
    }
}
