// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license.
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Microsoft.Git.CredentialManager.Commands
{
    /// <summary>
    /// Represents a Git Credential Manager command.
    /// </summary>
    public abstract class CommandBase
    {
        /// <summary>
        /// Check if this command should be executed for the given arguments.
        /// </summary>
        /// <param name="args">Application command-line arguments.</param>
        /// <returns>True if the command should be executed, false otherwise.</returns>
        public abstract bool CanExecute(string[] args);

        /// <summary>
        /// Execute the command.
        /// </summary>
        /// <param name="context">The current command execution context.</param>
        /// <param name="args">Application command-line arguments.</param>
        /// <returns>Awaitable task for the command execution.</returns>
        public abstract Task ExecuteAsync(ICommandContext context, string[] args);
    }

    /// <summary>
    /// Represents a simple Git Credential Manager command that takes a single named verb.
    /// </summary>
    public abstract class VerbCommandBase : CommandBase
    {
        /// <summary>
        /// Name of the command verb.
        /// </summary>
        protected abstract string Name { get; }

        public override bool CanExecute(string[] args)
        {
            return args.Length != 0 && StringComparer.OrdinalIgnoreCase.Equals(args[0], Name);
        }
    }

    /// <summary>
    /// Represents a command which selects a <see cref="IHostProvider"/> from a <see cref="IHostProviderRegistry"/>
    /// based on the <see cref="InputArguments"/> from standard input, and interacts with a <see cref="GitCredential"/>.
    /// </summary>
    public abstract class HostProviderCommandBase : VerbCommandBase
    {
        private readonly IHostProviderRegistry _hostProviderRegistry;

        protected HostProviderCommandBase(IHostProviderRegistry hostProviderRegistry)
        {
            _hostProviderRegistry = hostProviderRegistry;
        }

        public override async Task ExecuteAsync(ICommandContext context, string[] args)
        {
            // Parse standard input arguments
            // git-credential treats the keys as case-sensitive; so should we.
            IDictionary<string, string> inputDict = await context.StdIn.ReadDictionaryAsync(StringComparer.Ordinal);
            var input = new InputArguments(inputDict);

            // Determine the host provider
            context.Trace.WriteLine("Detecting host provider for input:");
            context.Trace.WriteDictionary(inputDict);
            IHostProvider provider = _hostProviderRegistry.GetProvider(input);
            context.Trace.WriteLine($"Host provider '{provider.Name}' was selected.");

            // Build the credential identifier
            string hostProviderKey = provider.GetCredentialKey(input);
            string credentialKey = $"git:{hostProviderKey}";
            context.Trace.WriteLine($"Credential key is '{credentialKey}'.");

            await ExecuteInternalAsync(context, input, provider, credentialKey);
        }

        /// <summary>
        /// Execute the command using the given <see cref="InputArguments"/>, <see cref="IHostProvider"/>, and <see cref="GitCredential"/> key.
        /// </summary>
        /// <param name="context">The current command execution context.</param>
        /// <param name="input">Input arguments of the current Git credential query.</param>
        /// <param name="provider">Host provider for the current <see cref="InputArguments"/>.</param>
        /// <param name="credentialKey">Unique identifier in the OS secure storage system for a <see cref="GitCredential"/>.</param>
        /// <returns>Awaitable task for the command execution.</returns>
        protected abstract Task ExecuteInternalAsync(ICommandContext context, InputArguments input, IHostProvider provider, string credentialKey);
    }
}
