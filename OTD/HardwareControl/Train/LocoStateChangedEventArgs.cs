// SPDX-License-Identifier: GPL-3.0-or-later
//
// OpenTrainDrive - DecoderControl
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
using OTD.HardwareControl.CommandStation;

namespace OTD.HardwareControl.Train;

/// <summary>
/// Argumente für Lokzustands-Updates aus der Kommandozentrale.
/// Enthält entweder ein Fahrstufen-/Richtungsupdate oder ein Funktionsupdate.
/// Protocol-unabhängige Abstraktion für alle Command Stations.
/// </summary>
public sealed class LocoStateChangedEventArgs : EventArgs
{
    public int Address { get; }
    public int? SpeedStep { get; }
    public VehicleDirection Direction { get; }
    public int? FunctionNumber { get; }
    public FunctionState? FunctionStateValue { get; }
    public bool IsEventPacket { get; }

    public bool HasSpeedUpdate => SpeedStep.HasValue;
    public bool HasFunctionUpdate => FunctionNumber.HasValue && FunctionStateValue.HasValue;

    public LocoStateChangedEventArgs(
        int address,
        int? speedStep,
        VehicleDirection direction,
        int? functionNumber,
        FunctionState? functionStateValue,
        bool isEventPacket)
    {
        Address = address;
        SpeedStep = speedStep;
        Direction = direction;
        FunctionNumber = functionNumber;
        FunctionStateValue = functionStateValue;
        IsEventPacket = isEventPacket;
    }
}

