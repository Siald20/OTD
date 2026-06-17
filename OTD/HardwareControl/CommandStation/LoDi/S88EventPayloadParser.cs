// SPDX-License-Identifier: GPL-3.0-or-later
//
// OpenTrainDrive - DecoderControl
// Copyright (C) 2026
//
// Authors:
// - Hansueli Alder <info@batec.net>
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.
// See the GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License
// along with this program. If not, see <https://www.gnu.org/licenses/>.
using System.Collections.Generic;
using System.Linq;

namespace OTD.HardwareControl.Drivers;

internal static class S88EventPayloadParser
{
    public static bool TryParse(byte[] payload, out S88EventPayloadParseResult? result, out string? error)
    {
        result = null;
        error = null;

        if (payload.Length == 0)
        {
            error = "Payload leer.";
            return false;
        }

        var count = payload[0];

        // Heartbeat: [0x00]
        if (count == 0 && payload.Length == 1)
        {
            result = new S88EventPayloadParseResult(
                format: S88EventPayloadFormat.Heartbeat,
                count: 0,
                changes: [],
                trailingBytes: 0,
                moduleAddress1: null,
                moduleType: null,
                moduleAddress2: null,
                moduleAddress3: null);
            return true;
        }

        var expectedLength = 1 + count * 3;
        if (payload.Length >= expectedLength && count > 0)
        {
            var changes = new List<S88EventPayloadChange>(count);
            for (var i = 0; i < count; i++)
            {
                var idx = 1 + i * 3;
                var moduleAddress = payload[idx];
                var contactNumber = payload[idx + 1];
                var rawState = payload[idx + 2];
                var isOccupied = rawState != 0;

                changes.Add(new S88EventPayloadChange(
                    moduleAddress,
                    contactNumber,
                    rawState,
                    isOccupied ? S88ChangeType.Occupied : S88ChangeType.Free));
            }

            result = new S88EventPayloadParseResult(
                format: S88EventPayloadFormat.ContactChanges,
                count: count,
                changes: changes,
                trailingBytes: payload.Length - expectedLength,
                moduleAddress1: null,
                moduleType: null,
                moduleAddress2: null,
                moduleAddress3: null);
            return true;
        }

        // Alternative LoDi-Interpretation fuer Diagnose:
        // [Anzahl][Moduladresse1][Modultyp][Moduladresse2][Moduladresse3]
        if (payload.Length >= 5)
        {
            result = new S88EventPayloadParseResult(
                format: S88EventPayloadFormat.ModuleOverview,
                count: count,
                changes: [],
                trailingBytes: payload.Length - 5,
                moduleAddress1: payload[1],
                moduleType: payload[2],
                moduleAddress2: payload[3],
                moduleAddress3: payload[4]);
            return true;
        }

        if (payload.Length < expectedLength)
        {
            error = $"Ungueltige Laenge: erwartet mindestens {expectedLength} Byte(s), erhalten {payload.Length}.";
            return false;
        }

        error = $"Payload konnte keinem bekannten EVT-Format zugeordnet werden. Laenge={payload.Length}.";
        return false;
    }
}

internal enum S88EventPayloadFormat
{
    Heartbeat,
    ContactChanges,
    ModuleOverview
}

internal enum S88ChangeType
{
    Free,
    Occupied
}

internal sealed class S88EventPayloadChange(
    byte moduleAddress,
    byte contactNumber,
    byte rawState,
    S88ChangeType changeType)
{
    public byte ModuleAddress { get; } = moduleAddress;
    public byte ContactNumber { get; } = contactNumber;
    public byte RawState { get; } = rawState;
    public S88ChangeType ChangeType { get; } = changeType;

    public bool IsOccupied => ChangeType == S88ChangeType.Occupied;
}

internal sealed class S88EventPayloadParseResult
{
    public S88EventPayloadFormat Format { get; }
    public int Count { get; }
    public IReadOnlyList<S88EventPayloadChange> Changes { get; }
    public int TrailingBytes { get; }
    public byte? ModuleAddress1 { get; }
    public byte? ModuleType { get; }
    public byte? ModuleAddress2 { get; }
    public byte? ModuleAddress3 { get; }

    public S88EventPayloadParseResult(
        S88EventPayloadFormat format,
        int count,
        IReadOnlyList<S88EventPayloadChange> changes,
        int trailingBytes,
        byte? moduleAddress1,
        byte? moduleType,
        byte? moduleAddress2,
        byte? moduleAddress3)
    {
        Format = format;
        Count = count;
        Changes = changes;
        TrailingBytes = trailingBytes;
        ModuleAddress1 = moduleAddress1;
        ModuleType = moduleType;
        ModuleAddress2 = moduleAddress2;
        ModuleAddress3 = moduleAddress3;
    }

    public bool IsHeartbeat => Format == S88EventPayloadFormat.Heartbeat;

    public IReadOnlyList<int> ModuleAddresses => Changes
        .Select(change => (int)change.ModuleAddress)
        .Distinct()
        .OrderBy(moduleAddress => moduleAddress)
        .ToArray();
}

