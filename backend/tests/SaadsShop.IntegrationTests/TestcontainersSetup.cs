using System.Runtime.CompilerServices;

namespace SaadsShop.IntegrationTests;

internal static class TestcontainersSetup
{
    /// <summary>
    /// Turns off Testcontainers' resource reaper unless the environment asks
    /// for it.
    /// </summary>
    /// <remarks>
    /// Ryuk is a sidecar container that deletes anything left behind if the
    /// test process is killed. It is a good idea, and it is also a second image
    /// to pull — which some networks (this project's CI sandbox among them)
    /// will not serve. Without this the whole suite fails at startup on a
    /// missing <c>testcontainers/ryuk</c>, which tells a reader nothing about
    /// the shop.
    ///
    /// Nothing is leaked by turning it off in the ordinary case:
    /// <see cref="ShopDatabase.DisposeAsync"/> stops the container xUnit
    /// created. The gap Ryuk covers is the process being killed outright, and
    /// then it is one <c>docker rm</c>. Set
    /// <c>TESTCONTAINERS_RYUK_DISABLED=false</c> to have it back.
    ///
    /// This runs at assembly load, before any Testcontainers type is touched —
    /// its settings are read once, statically, so setting the variable later
    /// would have no effect.
    /// </remarks>
    [ModuleInitializer]
    internal static void Initialize()
    {
        const string ryuk = "TESTCONTAINERS_RYUK_DISABLED";

        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable(ryuk)))
            Environment.SetEnvironmentVariable(ryuk, "true");
    }
}
