// SPDX-FileCopyrightText: 2023 Demerzel Solutions Limited
// SPDX-License-Identifier: LGPL-3.0-only

namespace Nethermind.Tools.Kute;

class Metrics
{
    public int Total { get; private set; }
    public int Failed { get; private set; }
    public int Responses { get; private set; }
    public IDictionary<string, int> Requests { get; } = new Dictionary<string, int>();

    public TimeSpan TotalRunningTime { get; set; }

    public void TickTotal() => Total++;
    public void TickFailed() => Failed++;
    public void TickResponses() => Responses++;

    public void TickMethod(string methodName)
    {
        if (Requests.ContainsKey(methodName))
        {
            Requests[methodName]++;
        }
        else
        {
            Requests[methodName] = 0;
        }
    }
}
