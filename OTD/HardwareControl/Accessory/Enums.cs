// // SPDX-License-Identifier: GPL-3.0-or-later
// //
// // OpenTrainDrive - DecoderControl
// // Copyright (C) 2026
// //
// // Authors:
// // - Hansueli Alder <info@batec.net>
// //
// // Dieses Programm ist freie Software: Sie können es unter den Bedingungen
// // der GNU General Public License, wie von der Free Software Foundation,
// // entweder Version 3 der Lizenz oder (nach Ihrer Wahl) jeder späteren
// // veröffentlichten Version, weiterverbreiten und/oder modifizieren.
// //
// // Dieses Programm wird in der Hoffnung bereitgestellt, dass es nützlich sein wird,
// // jedoch OHNE JEDE GEWÄHRLEISTUNG; sogar ohne die implizite Gewährleistung der
// // MARKTFÄHIGKEIT oder EIGNUNG FÜR EINEN BESTIMMTEN ZWECK.
// // Siehe die GNU General Public License für weitere Details.
// //
// // Sie sollten eine Kopie der GNU General Public License zusammen mit diesem
// // Programm erhalten haben. Falls nicht, siehe <https://www.gnu.org/licenses/>.

namespace OTD.HardwareControl;

/// <summary>
/// Defines operating mode of the train.
/// </summary>
public enum AccessoryType
{
    /// <summary>
    /// Turnout
    /// </summary>
    Turnout,

    /// <summary>
    /// Signal
    /// </summary>
    Signal,

    /// <summary>
    /// Other accessory (e.g. lighting, function model)
    /// </summary>
    Other
}

/// <summary>
/// Defines supported decoder communication protocols.
/// </summary>
public enum DecoderProtocol
{
    /// <summary>
    /// DCC accessory protocol
    /// </summary>
    Dcc,

    /// <summary>
    /// extended DCC accessory protocol
    /// </summary>
    DccExtended,

    /// <summary>
    /// MM2 (Maerklin Motorola) accessory protocol
    /// </summary>
    Motorola,

    /// <summary>
    /// Tams M3 accessory protocol for mfx decoders (without feedback).
    /// </summary>
    M3,

    /// <summary>
    /// Maerklin mfx accessory protocol.
    /// </summary>
    Mfx
}

/// <summary>
/// Defines on/off states for decoders.
/// </summary>
public enum FunctionState
{
    /// <summary>
    /// Function state is undefined (before initialization).
    /// </summary>
    Undefined,

    /// <summary>
    /// Function is switched off.
    /// </summary>
    Off,

    /// <summary>
    /// Function is switched on.
    /// </summary>
    On
}

