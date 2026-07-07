// SPDX-License-Identifier: GPL-3.0-or-later

namespace OTD.TrainDriving.RouteControl.Domain;

/// <summary>
/// Defines the type of sensor for route tracking and feedback.
/// </summary>
public enum SensorType
{
    /// <summary>
    /// Track contact (e.g., reed switch, contact): Detects train presence at a specific point.
    /// Offset is transformed symmetrically on reverse direction: new_offset = distance - old_offset
    /// </summary>
    TrackContact,

    /// <summary>
    /// Occupancy detection (e.g., track vacancy detector/Gleisbesetztmelder): 
    /// Detects when train enters a section.
    /// Offset is transformed asymmetrically on reverse direction to account for entry from opposite end:
    /// new_offset = distance - old_offset (same as TrackContact, but semantically represents section entry)
    /// </summary>
    OccupancyDetection
}
