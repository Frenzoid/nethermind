// SPDX-FileCopyrightText: 2023 Demerzel Solutions Limited
// SPDX-License-Identifier: LGPL-3.0-only

namespace Nethermind.Tools.Kute.JsonRpcMethodFilter;

class ComposedJsonRpcMethodFilter : IJsonRpcMethodFilter
{
    private readonly IEnumerable<IJsonRpcMethodFilter> _filters;

    public ComposedJsonRpcMethodFilter(IEnumerable<IJsonRpcMethodFilter> filters)
    {
        _filters = filters;
    }

    public bool ShouldSubmit(string methodName) =>
        _filters.All(f => f.ShouldSubmit(methodName));
}
