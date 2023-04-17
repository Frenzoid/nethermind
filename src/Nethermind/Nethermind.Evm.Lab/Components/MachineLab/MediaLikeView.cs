// SPDX-FileCopyrightText: 2023 Demerzel Solutions Limited
// SPDX-License-Identifier: LGPL-3.0-only

using MachineStateEvents;
using Nethermind.Evm.Lab.Interfaces;
using Terminal.Gui;

namespace Nethermind.Evm.Lab.Componants;
internal class MediaLikeView<T> : IComponent<T> where T : IState<T>, new()
{
    bool isCached = false;
    private FrameView? container = null;

    public void Dispose()
    {
        container?.Dispose();
    }

    public (View, Rectangle?) View(IState<T> state, Rectangle? rect = null)
    {
        var innerState = state.GetState();
        var frameBoundaries = new Rectangle(
                X: rect?.X ?? 0,
                Y: rect?.Y ?? 0,
                Width: rect?.Width ?? Dim.Fill(),
                Height: rect?.Height ?? Dim.Percent(10)
            );
        container ??= new FrameView("Program Controls")
        {
            X = frameBoundaries.X,
            Y = frameBoundaries.Y,
            Width = frameBoundaries.Width,
            Height = frameBoundaries.Height,
        };

        if (!isCached)
        {
            string[] labels = new[] { "reset", "previous", "next", "end" };
            ActionsBase[] events = new ActionsBase[] { new Goto(0), new MoveBack(), new MoveNext(), new Goto(int.MaxValue) };
            Button? previousButton = null;
            Button[] buttons = labels.Zip(events).Select(pair =>
            {
                (string label, ActionsBase msg) = pair;
                var actionButton = new Button(label)
                {
                    X = previousButton is null ? 0 : Pos.Right(previousButton),
                };
                actionButton.Clicked += () =>
                {
                    state.EventsSink.EnqueueEvent(msg);
                };
                previousButton = actionButton;
                return actionButton;
            }).ToArray();
            container.Add(buttons);
        }
        isCached = true;
        return (container, frameBoundaries);
    }
}
