// SPDX-License-Identifier: GPL-3.0-or-later
//
// OpenTrainDrive - AccessoryControl
// Copyright (C) 2026
//
// Authors:
// - Hansueli Alder <info@batec.net>
//
// Dieses Programm ist freie Software: Sie können es unter den Bedingungen
// der GNU General Public License, wie von der Free Software Foundation,
// entweder Version 3 der Lizenz oder (nach Ihrer Wahl) jeder späteren
// veröffentlichten Version, weiterverbreiten und/oder modifizieren.
//
// Dieses Programm wird in der Hoffnung bereitgestellt, dass es nützlich sein wird,
// jedoch OHNE JEDE GEWÄHRLEISTUNG; sogar ohne die implizite Gewährleistung der
// MARKTFÄHIGKEIT oder EIGNUNG FÜR EINEN BESTIMMTEN ZWECK.
// Siehe die GNU General Public License für weitere Details.
//
// Sie sollten eine Kopie der GNU General Public License zusammen mit diesem
// Programm erhalten haben. Falls nicht, siehe <https://www.gnu.org/licenses/>.

using System;

namespace OTD.HardwareControl.Accessory;

/// <summary>
/// Event arguments for accessory decoder state changes reported by a command station.
/// </summary>
public class AccessoryStateChangedEventArgs : EventArgs, IDecoderStateChangedEventArgs
{
    /// <summary>
    /// Address of the accessory decoder.
    /// </summary>
    public int Address { get; }

    /// <summary>
    /// Protocol-specific accessory output value that was reported.
    /// </summary>
    public int OutputValue { get; }

    /// <summary>
    /// New channel function state.
    /// </summary>
    public FunctionState FunctionState { get; }

    /// <summary>
    /// Backward-compatible alias of <see cref="FunctionState"/>.
    /// </summary>
    public FunctionState State => FunctionState;

    /// <summary>
    /// Backward-compatible view of <see cref="FunctionState"/>.
    /// </summary>
    public bool IsActive => FunctionState == FunctionState.On;

    /// <summary>
    /// Creates a new AccessoryStateChangedEventArgs instance.
    /// </summary>
    public AccessoryStateChangedEventArgs(int address, int outputValue, FunctionState functionState)
    {
        Address = address;
        OutputValue = outputValue;
        FunctionState = functionState;
    }

    public override string ToString()
        => $"AccessoryDecoder {Address} OutputValue {OutputValue} = {FunctionState}";
}
