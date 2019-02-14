// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license.
using System;
using System.ComponentModel;
using System.Threading.Tasks;
using Microsoft.Git.CredentialManager.Commands;
using Microsoft.Git.CredentialManager.SecureStorage;

namespace Microsoft.Git.CredentialManager
{
    public class Application : ApplicationBase
    {
        public IHostProviderRegistry ProviderRegistry { get; } = new HostProviderRegistry();

        public Application(ICommandContext context)
            : base(context) { }

        protected override async Task<int> RunInternalAsync(string[] args)
        {
            // Construct all supported commands
            var commands = new CommandBase[]
            {
                new EraseCommand(ProviderRegistry),
                new GetCommand(ProviderRegistry),
                new StoreCommand(ProviderRegistry),
                new VersionCommand(),
                new HelpCommand(),
            };

            // Trace the current version and program arguments
            Context.Trace.WriteLine($"{Constants.GetProgramHeader()} '{string.Join(" ", args)}'");

            if (args.Length == 0)
            {
                Context.StdError.WriteLine("Missing command.");
                HelpCommand.PrintUsage(Context.StdError);
                return -1;
            }

            foreach (var cmd in commands)
            {
                if (cmd.CanExecute(args))
                {
                    try
                    {
                        await cmd.ExecuteAsync(Context, args);
                        return 0;
                    }
                    catch (Exception e)
                    {
                        if (e is AggregateException ae)
                        {
                            ae.Handle(WriteException);
                        }
                        else
                        {
                            WriteException(e);
                        }

                        return -1;
                    }
                }
            }

            Context.StdError.WriteLine("Unrecognized command '{0}'.", args[0]);
            HelpCommand.PrintUsage(Context.StdError);
            return -1;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                ProviderRegistry.Dispose();
            }

            base.Dispose(disposing);
        }

        protected bool WriteException(Exception ex)
        {
            // Try and use a nicer format for some well-known exception types
            switch (ex)
            {
                case Win32Exception w32Ex:
                    Context.StdError.WriteLine("fatal: {0} [0x{1:x}]", w32Ex.Message, w32Ex.NativeErrorCode);
                    break;
                default:
                    Context.StdError.WriteLine("fatal: {0}", ex.Message);
                    break;
            }

            // Recurse to print all inner exceptions
            if (!(ex.InnerException is null))
            {
                WriteException(ex.InnerException);
            }

            return true;
        }
    }
}
