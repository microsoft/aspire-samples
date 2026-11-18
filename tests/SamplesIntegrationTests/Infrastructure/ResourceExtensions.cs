// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace SamplesIntegrationTests.Infrastructure;

internal static class ResourceExtensions
{
    /// <summary>
    /// Gets the name of the <see cref="DotnetProjectResource"/> based on the project file path.
    /// </summary>
    public static string GetName(this DotnetProjectResource project)
    {
        var metadata = project.GetProjectMetadata();
        return Path.GetFileNameWithoutExtension(metadata.ProjectPath);
    }
}
