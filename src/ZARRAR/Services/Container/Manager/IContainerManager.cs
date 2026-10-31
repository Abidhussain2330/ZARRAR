// SPDX-License-Identifier: LicenseRef-THE-ARZENS-Custom
// Copyright (C) 2022-2025 THE ARZENS

using System.Net;
using ZARRAR.Models.Internal;

namespace ZARRAR.Services.Container.Manager;

public interface IContainerManager
{
    /// <summary>
    /// Create a container
    /// </summary>
    /// <param name="config">container configuration</param>
    /// <param name="token"></param>
    /// <returns></returns>
    public Task<Models.Data.Container?> CreateContainerAsync(ContainerConfig config, CancellationToken token = default);

    /// <summary>
    /// Destroy a container
    /// </summary>
    /// <param name="container">container</param>
    /// <param name="token"></param>
    /// <returns></returns>
    public Task DestroyContainerAsync(Models.Data.Container container, CancellationToken token = default);

    /// <summary>Return a secret-free health and capacity snapshot for the active runtime provider.</summary>
    public Task<ContainerProviderStatus> GetStatusAsync(CancellationToken token = default);
}

public sealed record ContainerProviderStatus(
    string Provider,
    string Node,
    string Status,
    int? Capacity,
    int Running,
    int Pending,
    int Unavailable,
    string Isolation,
    string? Version,
    DateTimeOffset SampledAtUtc);

internal static class ContainerManagerLogHelper
{
    private static void LogWithHttpContext<T>(
        ILogger<T> logger,
        string container,
        HttpStatusCode status,
        string body,
        string statusLogFormatKey,
        string responseLogFormatKey
    )
    {
        logger.SystemLog(StaticLocalizer[statusLogFormatKey, container, status],
            TaskStatus.Failed, LogLevel.Warning);
        logger.SystemLog(StaticLocalizer[responseLogFormatKey, container, body],
            TaskStatus.Failed, LogLevel.Error);
    }

    extension<T>(ILogger<T> logger)
    {
        internal void LogCreationFailedWithHttpContext(string container,
            HttpStatusCode status,
            string body
        ) => LogWithHttpContext(logger, container, status, body,
            nameof(Resources.Program.ContainerManager_ContainerCreationFailedStatus),
            nameof(Resources.Program.ContainerManager_ContainerCreationFailedResponse));

        internal void LogDeletionFailedWithHttpContext(string container,
            HttpStatusCode status,
            string body
        ) => LogWithHttpContext(logger, container, status, body,
            nameof(Resources.Program.ContainerManager_ContainerDeletionFailedStatus),
            nameof(Resources.Program.ContainerManager_ContainerDeletionFailedResponse));

        internal void LogServiceCreationFailedWithHttpContext(string container,
            HttpStatusCode status,
            string body
        ) => LogWithHttpContext(logger, container, status, body,
            nameof(Resources.Program.ContainerManager_ServiceCreationFailedStatus),
            nameof(Resources.Program.ContainerManager_ServiceCreationFailedResponse));
    }
}
