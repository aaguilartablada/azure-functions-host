// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.
#nullable enable

using System;
using System.Collections.Generic;

namespace Microsoft.Azure.WebJobs.Script.Metrics
{
    /// <summary>
    /// Defines the methods that are required for a host metrics provider.
    /// The provider is can be used to collects metrics from the
    /// HostMetrics meter and makes them available as a HostMetricsPayload object.
    /// </summary>
    public interface IHostMetricsProvider
    {
        /// <summary>
        /// Gets the instance ID.
        /// </summary>
        public string InstanceId { get; }

        /// <summary>
        /// Gets the name of the function group for this instance.
        /// </summary>
        public string FunctionGroup { get; }

        /// <summary>
        /// Gets the total number of started invocations for host lifetime.
        /// </summary>
        public long TotalStartedInvocationCount { get; }

        /// <summary>
        /// Retrieves a dictionary of available metrics.
        /// </summary>
        public IReadOnlyDictionary<string, long> GetHostMetrics();

        /// <summary>
        /// Determines whether the provider has any host metrics.
        /// </summary>
        public bool HasMetrics();
    }
}
