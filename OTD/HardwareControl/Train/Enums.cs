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
public enum TrainOperatingMode
{
    /// <summary>
    /// train shut down (locomotives unregistered at the command station)
    /// </summary>
    ShutDown,

    /// <summary>
    /// Train in parking mode (locomotives registred at the command station, driving commands ignored, parking lights on, if available).
    /// </summary>
    Parking,

    /// <summary>
    /// Train is shunting. Drive commands are accepted with reduced maximum speed.
    /// </summary>
    Shunting,

    /// <summary>
    /// Train is travelling. Drive commands are accepted in this mode.
    /// </summary>
    Travelling
}

/// <summary>
/// Defines the selected travelling direction of the train.
/// </summary>
public enum TrainDirection
{
    /// <summary>
    /// Train travels in logical direction A (forward).
    /// </summary>
    A,

    /// <summary>
    /// Train travels in logical direction B (backward).
    /// </summary>
    B
}

/// <summary>
/// Defines the orientation of a vehicle within the train composition.
/// </summary>
public enum VehicleOrientation
{
    /// <summary>
    /// The vehicle's decoder direction matches the train's selected travel direction.
    /// </summary>
    Normal,

    /// <summary>
    /// The vehicle's decoder direction is opposite to the train's selected travel direction.
    /// </summary>
    Reverse
}

public enum LocoDecoderProtocol
{
    /// <summary>
    /// DCC protocol with 14 speed steps.
    /// </summary>
    Dcc14,

    /// <summary>
    /// DCC protocol with 28 speed steps.
    /// </summary>
    Dcc28,

    /// <summary>
    /// DCC protocol with 128 speed steps (126 effective speed steps).
    /// </summary>
    Dcc128,

    /// <summary>
    /// MM2 (Maerklin Motorola) protocol with 27 speed steps.
    /// </summary>
    Motorola,

    /// <summary>
    /// Tams M3 protocol for mfx decoders (without feedback).
    /// </summary>
    M3,

    /// <summary>
    /// Maerklin mfx protocol.
    /// </summary>
    Mfx
}

// Todo: Prüfen, ob "Undefined" Zustände benötigt werden, ggf. IsInitalized-Property einführen,
// Todo: um Fahrbefehle entgegengenommen werden dürfen.
/// <summary>
/// Defines the decoder-level logical travel direction of a vehicle.
/// </summary>
public enum VehicleDirection
{
    /// <summary>
    /// Direction is undefined (before initialization).
    /// </summary>
    Undefined,

    /// <summary>
    /// Travel direction forward
    /// </summary>
    Forward,

    /// <summary>
    /// Travel direction backward
    /// </summary>
    Backward
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

public enum HeadlightMode
{
    /// <summary>
    /// Headlights are turned on.
    /// </summary>
    Off,

    /// <summary>
    /// Headlights are turned off.
    /// </summary>
    On,

    /// <summary>
    /// Headlights are switched automatically, based on operating mode.
    /// </summary>
    Auto
}
