using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using DotNet.Testcontainers.Images;

namespace GTiff2Tiles.Benchmarks;

internal static class ContainerRunner
{
    public static async Task PullAsync(string imageName)
    {
        await using IContainer container = new ContainerBuilder(imageName)
            .WithImagePullPolicy(PullPolicy.Always)
            .WithCommand("true")
            .Build();

        await container.StartAsync().ConfigureAwait(false);
    }

    public static async Task<IContainer> StartPersistentAsync(
        string imageName,
        string hostDataDirectory,
        string containerDataDirectory)
    {
        IContainer container = new ContainerBuilder(imageName)
            .WithImagePullPolicy(PullPolicy.Missing)
            .WithBindMount(hostDataDirectory, containerDataDirectory)
            .WithEntrypoint("sleep")
            .WithCommand("infinity")
            .Build();

        await container.StartAsync().ConfigureAwait(false);

        return container;
    }

    public static async Task RunInExistingAsync(IContainer container, params string[] command)
    {
        ExecResult result = await container.ExecAsync(command).ConfigureAwait(false);

        if (result.ExitCode is 0)
            return;

        throw new InvalidOperationException(
            $"Container exec failed with code {result.ExitCode}.{Environment.NewLine}stdout:{Environment.NewLine}{result.Stdout}{Environment.NewLine}stderr:{Environment.NewLine}{result.Stderr}"
        );
    }

    public static async ValueTask StopAndDisposeAsync(IContainer? container)
    {
        if (container is null)
            return;

        try
        {
            await container.StopAsync().ConfigureAwait(false);
        }
        finally
        {
            await container.DisposeAsync().ConfigureAwait(false);
        }
    }
}
