### Release notes

<!-- Please add your release notes in the following format:
- My change description (#PR)
-->
- Update Java Worker Version to [2.12.2](https://github.com/Azure/azure-functions-java-worker/releases/tag/2.12.2)
- Update Python Worker Version to [4.17.0](https://github.com/Azure/azure-functions-python-worker/releases/tag/4.17.0)
- Increased maximum HTTP request content size to 210000000 Bytes (~200MB)
- Update Node.js Worker Version to [3.8.1](https://github.com/Azure/azure-functions-nodejs-worker/releases/tag/v3.8.1)
- Update WebJobsScriptHostService to remove hardcoded sleep during application shut down (#9520)
- Update PowerShell 7.4 Worker Version to [4.0.2975](https://github.com/Azure/azure-functions-powershell-worker/releases/tag/v4.0.2975)
- Update PowerShell 7.2 Worker Version to [4.0.2974](https://github.com/Azure/azure-functions-powershell-worker/releases/tag/v4.0.2974)
- Update PowerShell 7.0 Worker Version to [4.0.2973](https://github.com/Azure/azure-functions-powershell-worker/releases/tag/v4.0.2973)
- Bug fix: Do not restart a worker channel when an API request is made to update or get the function metadata
  - This fixes an issue where when testing a function app in the portal, and worker indexing is enabled, the host creates a new worker channel
    that does not get properly initialized; the host will now just return the function metadata
- Bug fix: Fix an issue where host creates a new worker channel that does not get properly initialized
  - The host will now restart if it's running and there are no channels available
