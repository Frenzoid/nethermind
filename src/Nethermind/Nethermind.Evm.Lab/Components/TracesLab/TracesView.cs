// SPDX-FileCopyrightText: 2023 Demerzel Solutions Limited
// SPDX-License-Identifier: LGPL-3.0-only

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Nethermind.Evm.Lab.Interfaces;
using Org.BouncyCastle.Asn1.Crmf;
using Terminal.Gui;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace Nethermind.Evm.Lab.Componants;
internal class TracesView : IComponent<TraceState>
{
    bool isCached = false;
    private View? container = null;
    private HalfPageView targetView = null;
    private HalfPageView subjectView = null;

    public void Dispose()
    {
        container?.Dispose();
        targetView?.Dispose();
        subjectView?.Dispose();
    }

    public (View, Rectangle?) View(IState<TraceState> state, Rectangle? rect = null)
    {
        var innerState = state.GetState();

        targetView ??= new HalfPageView(innerState.Traces.target.Name);
        subjectView ??= new HalfPageView(innerState.Traces.subject.Name);

        container ??= new View()
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill()
        };


        var (target_view, _) = targetView.View(innerState.Traces.target.State, new Rectangle(0, 0, Dim.Percent(50), Dim.Fill() - 3)); // h:10 w:30
        var (subject_view, _) = subjectView.View(innerState.Traces.subject.State, new Rectangle(Pos.Right(target_view), 0, Dim.Percent(51), Dim.Fill() - 3)); // h:10 w:30

        if (!isCached)
        {
            var controls_view = new MediaLikeView<TraceState>()
                .View(innerState, new Rectangle(0, Pos.Bottom(subject_view), Dim.Fill(), 3)).Item1;
            container.Add(target_view, subject_view, controls_view);
            isCached = true;
        }

        return (container, null);
    }
}
