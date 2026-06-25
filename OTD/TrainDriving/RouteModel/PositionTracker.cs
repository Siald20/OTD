// SPDX-License-Identifier: GPL-3.0-or-later

using System;

namespace OTD.TrainDriving.RouteModel;

/// <summary>
/// Position estimator for route-based driving with sensor recalibration.
/// </summary>
public sealed class PositionTracker
{
    private readonly RouteTable _routeTable;
    private readonly double _correctionGateCm;

    /// <summary>
    /// Creates a position tracker bound to a route table.
    /// </summary>
    /// <param name="routeTable">The route table used for sensor anchor lookups.</param>
    /// <param name="initialPositionCm">Initial estimated route position in model centimeters.</param>
    /// <param name="correctionGateCm">Maximum accepted deviation between estimated and sensed position for recalibration.</param>
    public PositionTracker(RouteTable routeTable, double initialPositionCm = 0.0, double correctionGateCm = 150.0)
    {
        _routeTable = routeTable ?? throw new ArgumentNullException(nameof(routeTable));

        if (correctionGateCm < 0)
            throw new ArgumentOutOfRangeException(nameof(correctionGateCm), "Gate must be >= 0.");

        EstimatedPositionCm = Math.Max(0.0, initialPositionCm);
        _correctionGateCm = correctionGateCm;
    }

    /// <summary>
    /// Gets the current estimated absolute route position in model centimeters.
    /// </summary>
    public double EstimatedPositionCm { get; private set; }

    /// <summary>
    /// Gets the identifier of the most recent calibration sensor, if any.
    /// </summary>
    public int? LastCalibrationSensorId { get; private set; }

    /// <summary>
    /// Gets the most recent correction error in model centimeters, if any.
    /// </summary>
    public double? LastCorrectionErrorCm { get; private set; }

    /// <summary>
    /// Integrates a traveled distance delta into the estimated route position.
    /// </summary>
    /// <param name="deltaCm">Distance traveled since the previous update in model centimeters. Negative values move the estimate backwards.</param>
    public void IntegrateDelta(double deltaCm)
    {
        EstimatedPositionCm = Math.Max(0.0, EstimatedPositionCm + deltaCm);
    }

    /// <summary>
    /// Attempts to recalibrate the estimated position from a sensor activation.
    /// </summary>
    /// <param name="sensorId">The sensor identifier to resolve against the route table.</param>
    /// <returns><c>true</c> if the sensor was found and the correction was accepted; otherwise <c>false</c>.</returns>
    public bool TryRecalibrateFromSensor(int sensorId)
    {

        if (!_routeTable.TryGetSensorAnchor(sensorId, out var anchor))
            return false;

        var error = anchor.S_Cm - EstimatedPositionCm;
        LastCorrectionErrorCm = error;

        if (Math.Abs(error) > _correctionGateCm)
            return false;

        EstimatedPositionCm = anchor.S_Cm;
        LastCalibrationSensorId = sensorId;
        return true;
    }

    /// <summary>
    /// Forces the internal estimated route position without applying recalibration gate checks.
    /// </summary>
    /// <param name="positionCm">The new estimated route position in model centimeters.</param>
    public void ForceSetPosition(double positionCm)
    {
        EstimatedPositionCm = Math.Max(0.0, positionCm);
    }
}



