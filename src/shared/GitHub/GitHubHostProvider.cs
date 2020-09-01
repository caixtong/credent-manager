﻿// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license.
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Git.CredentialManager;
using Microsoft.Git.CredentialManager.Authentication.OAuth;

namespace GitHub
{
    public class GitHubHostProvider : HostProvider
    {
        private static readonly string[] GitHubOAuthScopes =
        {
            GitHubConstants.OAuthScopes.Repo,
            GitHubConstants.OAuthScopes.Gist,
            GitHubConstants.OAuthScopes.Workflow,
        };

        private static readonly string[] GitHubCredentialScopes =
        {
            GitHubConstants.TokenScopes.Gist,
            GitHubConstants.TokenScopes.Repo
        };

        private readonly IGitHubRestApi _gitHubApi;
        private readonly IGitHubAuthentication _gitHubAuth;

        public GitHubHostProvider(ICommandContext context)
            : this(context, new GitHubRestApi(context), new GitHubAuthentication(context)) { }

        public GitHubHostProvider(ICommandContext context, IGitHubRestApi gitHubApi, IGitHubAuthentication gitHubAuth)
            : base(context)
        {
            EnsureArgument.NotNull(gitHubApi, nameof(gitHubApi));
            EnsureArgument.NotNull(gitHubAuth, nameof(gitHubAuth));

            _gitHubApi = gitHubApi;
            _gitHubAuth = gitHubAuth;
        }

        public override string Id => "github";

        public override string Name => "GitHub";

        public override IEnumerable<string> SupportedAuthorityIds => GitHubAuthentication.AuthorityIds;

        public override bool IsSupported(InputArguments input)
        {
            if (input is null)
            {
                return false;
            }

            // Split port number and hostname from host input argument
            input.TryGetHostAndPort(out string hostName, out _);

            // We do not support unencrypted HTTP communications to GitHub,
            // but we report `true` here for HTTP so that we can show a helpful
            // error message for the user in `CreateCredentialAsync`.
            return (StringComparer.OrdinalIgnoreCase.Equals(input.Protocol, "http") ||
                    StringComparer.OrdinalIgnoreCase.Equals(input.Protocol, "https")) &&
                   (StringComparer.OrdinalIgnoreCase.Equals(hostName, GitHubConstants.GitHubBaseUrlHost) ||
                    StringComparer.OrdinalIgnoreCase.Equals(hostName, GitHubConstants.GistBaseUrlHost));
        }

        public override string GetServiceName(InputArguments input)
        {
            var baseUri = new Uri(base.GetServiceName(input));

            // Normalise the URI
            string url = NormalizeUri(baseUri).AbsoluteUri;

            // Trim trailing slash
            return url.TrimEnd('/');
        }

        public override async Task<ICredential> GenerateCredentialAsync(InputArguments input)
        {
            ThrowIfDisposed();

            // We should not allow unencrypted communication and should inform the user
            if (StringComparer.OrdinalIgnoreCase.Equals(input.Protocol, "http"))
            {
                throw new Exception("Unencrypted HTTP is not supported for GitHub. Ensure the repository remote URL is using HTTPS.");
            }

            Uri remoteUri = input.GetRemoteUri();

            string service = GetServiceName(input);

            AuthenticationModes authModes = await GetSupportedAuthenticationModesAsync(remoteUri);

            AuthenticationPromptResult promptResult = await _gitHubAuth.GetAuthenticationAsync(remoteUri, input.UserName, authModes);

            switch (promptResult.AuthenticationMode)
            {
                case AuthenticationModes.Basic:
                    GitCredential patCredential = await GeneratePersonalAccessTokenAsync(remoteUri, promptResult.BasicCredential);

                    // HACK: Store the PAT immediately in case this PAT is not valid for SSO.
                    // We don't know if this PAT is valid for SAML SSO and if it's not Git will fail
                    // with a 403 and call neither 'store' or 'erase'. The user is expected to fiddle with
                    // the PAT permissions manually on the web and then retry the Git operation.
                    // We must store the PAT now so they can resume/repeat the operation with the same,
                    // now SSO authorized, PAT.
                    // See: https://github.com/microsoft/Git-Credential-Manager-Core/issues/133
                    Context.CredentialStore.AddOrUpdate(service, patCredential.Account, patCredential.Password);
                    return patCredential;

                case AuthenticationModes.OAuth:
                    return await GenerateOAuthCredentialAsync(remoteUri);

                default:
                    throw new ArgumentOutOfRangeException(nameof(promptResult));
            }
        }

        private async Task<GitCredential> GenerateOAuthCredentialAsync(Uri targetUri)
        {
            OAuth2TokenResult result = await _gitHubAuth.GetOAuthTokenAsync(targetUri, GitHubOAuthScopes);

            // Resolve the GitHub user handle
            GitHubUserInfo userInfo = await _gitHubApi.GetUserInfoAsync(targetUri, result.AccessToken);

            return new GitCredential(userInfo.Login, result.AccessToken);
        }

        private async Task<GitCredential> GeneratePersonalAccessTokenAsync(Uri targetUri, ICredential credentials)
        {
            AuthenticationResult result = await _gitHubApi.CreatePersonalTokenAsync(
                targetUri, credentials, null, GitHubCredentialScopes);

            string token = null;

            if (result.Type == GitHubAuthenticationResultType.Success)
            {
                Context.Trace.WriteLine($"Token acquisition for '{targetUri}' succeeded");

                token = result.Token;
            }
            else if (result.Type == GitHubAuthenticationResultType.TwoFactorApp ||
                     result.Type == GitHubAuthenticationResultType.TwoFactorSms)
            {
                bool isSms = result.Type == GitHubAuthenticationResultType.TwoFactorSms;

                string authCode = await _gitHubAuth.GetTwoFactorCodeAsync(targetUri, isSms);

                result = await _gitHubApi.CreatePersonalTokenAsync(targetUri, credentials, authCode, GitHubCredentialScopes);

                if (result.Type == GitHubAuthenticationResultType.Success)
                {
                    Context.Trace.WriteLine($"Token acquisition for '{targetUri}' succeeded.");

                    token = result.Token;
                }
            }

            if (token != null)
            {
                // Resolve the GitHub user handle
                GitHubUserInfo userInfo = await _gitHubApi.GetUserInfoAsync(targetUri, token);

                return new GitCredential(userInfo.Login, token);
            }

            throw new Exception($"Interactive logon for '{targetUri}' failed.");
        }

        internal async Task<AuthenticationModes> GetSupportedAuthenticationModesAsync(Uri targetUri)
        {
            // Check for an explicit override for supported authentication modes
            if (Context.Settings.TryGetSetting(
                GitHubConstants.EnvironmentVariables.AuthenticationModes,
                Constants.GitConfiguration.Credential.SectionName, GitHubConstants.GitConfiguration.Credential.AuthenticationModes,
                out string authModesStr))
            {
                if (Enum.TryParse(authModesStr, true, out AuthenticationModes authModes) && authModes != AuthenticationModes.None)
                {
                    Context.Trace.WriteLine($"Supported authentication modes override present: {authModes}");
                    return authModes;
                }
                else
                {
                    Context.Trace.WriteLine($"Invalid value for supported authentication modes override setting: '{authModesStr}'");
                }
            }

            // GitHub.com should use OAuth authentication only
            if (IsGitHubDotCom(targetUri))
            {
                Context.Trace.WriteLine($"{targetUri} is github.com - authentication schemes: '{GitHubConstants.DotDomAuthenticationModes}'");
                return GitHubConstants.DotDomAuthenticationModes;
            }

            // For GitHub Enterprise we must do some detection of supported modes
            Context.Trace.WriteLine($"{targetUri} is GitHub Enterprise - checking for supporting authentication schemes...");

            try
            {
                GitHubMetaInfo metaInfo = await _gitHubApi.GetMetaInfoAsync(targetUri);

                var modes = AuthenticationModes.None;
                if (metaInfo.VerifiablePasswordAuthentication)
                {
                    modes |= AuthenticationModes.Basic;
                }
                if (Version.TryParse(metaInfo.InstalledVersion, out var version) && version >= GitHubConstants.MinimumEnterpriseOAuthVersion)
                {
                    modes |= AuthenticationModes.OAuth;
                }

                Context.Trace.WriteLine($"GitHub Enterprise instance has version '{metaInfo.InstalledVersion}' and supports authentication schemes: {modes}");
                return modes;
            }
            catch (Exception ex)
            {
                Context.Trace.WriteLine($"Failed to query '{targetUri}' for supported authentication schemes.");
                Context.Trace.WriteException(ex);

                Context.Terminal.WriteLine($"warning: failed to query '{targetUri}' for supported authentication schemes.");
                return AuthenticationModes.Basic | AuthenticationModes.OAuth;
            }
        }

        protected override void ReleaseManagedResources()
        {
            _gitHubApi.Dispose();
            _gitHubAuth.Dispose();
            base.ReleaseManagedResources();
        }

        #region Private Methods

        internal static bool IsGitHubDotCom(Uri targetUri)
        {
            return StringComparer.OrdinalIgnoreCase.Equals(targetUri.Host, GitHubConstants.GitHubBaseUrlHost);
        }

        private static Uri NormalizeUri(Uri uri)
        {
            if (uri is null)
            {
                throw new ArgumentNullException(nameof(uri));
            }

            // Special case for gist.github.com which are git backed repositories under the hood.
            // Credentials for these repositories are the same as the one stored with "github.com"
            if (uri.DnsSafeHost.Equals(GitHubConstants.GistBaseUrlHost, StringComparison.OrdinalIgnoreCase))
            {
                return new Uri("https://" + GitHubConstants.GitHubBaseUrlHost);
            }

            return uri;
        }

        #endregion
    }
}
