// SPDX-FileCopyrightText: 2023 Demerzel Solutions Limited
// SPDX-License-Identifier: LGPL-3.0-only

using System.Data;
using Nethermind.Evm.Lab.Interfaces;
using Terminal.Gui;

namespace Nethermind.Evm.Lab.Componants;
internal class StorageView : IComponent<MachineState>
{
    public (View, Rectangle) View(IState<MachineState> state, Rectangle? rect = null)
    {
        var innerState = state.GetState();

        var frameBoundaries = new Rectangle(
                X: rect?.X ?? 0,
                Y: rect?.Y ?? 0,
                Width: rect?.Width ?? 50,
                Height: rect?.Height ?? 10
            );
        var frameView = new FrameView("StorageState")
        {
            X = frameBoundaries.X,
            Y = frameBoundaries.Y,
            Width = frameBoundaries.Width,
            Height = frameBoundaries.Height,
        };

        if (innerState.StoragesByDepth.TryPeek(out var storage))
        {
            var dataTable = new DataTable();
            foreach (var (k, v) in storage)
            {
                dataTable.Rows.Add(k);
                dataTable.Columns.Add(v);
            }

            frameView.Add(new TableView()
            {
                X = 0,
                Y = 0,
                Width = Dim.Fill(2),
                Height = Dim.Fill(2),
                Table = dataTable
            });
        }
        return (frameView, frameBoundaries);
    }
}
