// SPDX-FileCopyrightText: 2023 Demerzel Solutions Limited
// SPDX-License-Identifier: LGPL-3.0-only

using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Nethermind.Runner.EnvironmentVariables;

public static class CommandRunner
{
    public static (int exitCode, string output, string error) RunCommand(string command)
    {
        static string GetFileName() =>
            RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "cmd.exe" : "/bin/bash";

        static string GetArguments(string command) =>
            RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? $"/C {command}" : $"-c \"{command}\"";

        ProcessStartInfo info = new(GetFileName(), GetArguments(command))
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        using Process? process = Process.Start(info);
        if (process is not null)
        {
            process.WaitForExit();
            return (process.ExitCode, process.StandardOutput.ReadToEnd(), process.StandardError.ReadToEnd());
        }

        return (-1, $"Couldn't start process {info.FileName} {info.Arguments}", string.Empty);
    }
}
