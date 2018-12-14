// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license.
using System.Text;

namespace Microsoft.Git.CredentialManager
{
    public static class Constants
    {
        public const string GcmVersion = "1.0";
        public const string PersonalAccessTokenUserName = "PersonalAccessToken";

        public const string WwwAuthenticateNegotiateScheme = "Negotiate";
        public const string WwwAuthenticateNtlmScheme      = "NTLM";

        public static class EnvironmentVariables
        {
            public const string GcmTrace           = "GCM_TRACE";
            public const string GcmTraceSecrets    = "GCM_TRACE_SECRETS";
            public const string GcmDebug           = "GCM_DEBUG";
            public const string GitTerminalPrompts = "GIT_TERMINAL_PROMPT";
        }

        /// <summary>
        /// Get standard program header title for Git Credential Manager, including the current version and OS information.
        /// </summary>
        /// <returns>Standard program header.</returns>
        public static string GetProgramHeader()
        {
            PlatformInformation info = PlatformUtils.GetPlatformInformation();

            return $"Git Credential Manager (version {GcmVersion}, {info.OperatingSystemType})";
        }

        /// <summary>
        /// Get the HTTP user-agent for Git Credential Manager.
        /// </summary>
        /// <returns>User-agent string for HTTP requests.</returns>
        public static string GetHttpUserAgent()
        {
            PlatformInformation info = PlatformUtils.GetPlatformInformation();
            string osType     = info.OperatingSystemType;
            string cpuArch    = info.CpuArchitecture;
            string clrVersion = info.ClrVersion;

            return string.Format($"Git-Credential-Manager/{GcmVersion} ({osType}; {cpuArch}) CLR/{clrVersion}");
        }
    }
}
