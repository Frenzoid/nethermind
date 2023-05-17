// SPDX-FileCopyrightText: 2023 Demerzel Solutions Limited
// SPDX-License-Identifier: LGPL-3.0-only

using CommandLine;

namespace Nethermind.Tools.Kute;

public class Config
{
    [Option('m', "messages", Required = true, HelpText = "Path to a Folder or a File containing JSON RPC messages")]
    public string MessagesSourcePath { get; }

    [Option('a', "address", Required = false, Default = "http://localhost:8551", HelpText = "Address where to send JSON RPC calls")]
    public string HostAddress { get; }

    [Option('s', "secret", Required = true, HelpText = "Path to file with hex encoded secret for jwt authentication")]
    public string JwtSecretFile { get; }

    [Option('d', "dry", Required = false, Default = false, HelpText = "Only log into console")]
    public bool DryRun { get; }

    public Config(string messagesSourcePath, string hostAddress, string jwtSecretFile, bool dryRun)
    {
        MessagesSourcePath = messagesSourcePath;
        HostAddress = hostAddress;
        JwtSecretFile = jwtSecretFile;
        DryRun = dryRun;
    }
}
