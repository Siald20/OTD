// SPDX-License-Identifier: GPL-3.0-or-later

using System;

namespace OTD.TrainDriving.Trajectory;

/// <summary>
/// Generic speed trajectory that supports linear, control-point and ease-in-out curve shapes.
/// </summary>
public sealed class Trajectory : ISpeedTrajectory
{
    private readonly double _v0Ms;
    private readonly double _v1Ms;
    private readonly double _scale;
    private readonly TrajectoryCurveType _curveType;
    private readonly TrajectoryControlPoint? _controlPoint;
    private readonly TrajectoryControlPoint? _secondaryControlPoint;
    private readonly double _shapeExponent;

    public Trajectory(DrivingTrajectoryRequest request)
    {
        request.Validate();

        TotalDistanceCmModel = request.DistanceCmModel;
        _scale = request.Scale;
        _curveType = request.CurveType;
        _controlPoint = request.ControlPoint;
        _secondaryControlPoint = request.SecondaryControlPoint;

        _v0Ms = request.CurrentSpeedKmhPrototype / 3.6;
        _v1Ms = request.TargetSpeedKmhPrototype / 3.6;

        // 50% -> linear blend; >50 stronger S-curve, <50 softer transition.
        _shapeExponent = 0.5 + (request.CurveShapePercent / 100.0) * 2.5;
    }

    public double TotalDistanceCmModel { get; }

    public double GetSpeedKmhAtModelDistanceCm(double traveledModelCm)
    {
        if (TotalDistanceCmModel <= 0)
            return _v1Ms * 3.6;

        var xCm = Math.Clamp(traveledModelCm, 0.0, TotalDistanceCmModel);

        return _curveType switch
        {
            TrajectoryCurveType.ControlPoint => EvaluateControlPoint(xCm) * 3.6,
            TrajectoryCurveType.EaseInOut => EvaluateEaseInOut(xCm) * 3.6,
            _ => EvaluateLinear(xCm) * 3.6
        };
    }

    private double EvaluateLinear(double traveledModelCm)
    {
        var sTotal = ModelCmToPrototypeM(TotalDistanceCmModel);
        if (sTotal <= 0.0)
            return _v1Ms;

        var s = ModelCmToPrototypeM(traveledModelCm);
        var a = ((_v1Ms * _v1Ms) - (_v0Ms * _v0Ms)) / (2.0 * sTotal);
        var vSquared = (_v0Ms * _v0Ms) + (2.0 * a * s);
        var v = Math.Sqrt(Math.Max(0.0, vSquared));

        if (traveledModelCm >= TotalDistanceCmModel)
            return _v1Ms;

        return v;
    }

    private double EvaluateControlPoint(double traveledModelCm)
    {
        if (_controlPoint is null)
            return EvaluateLinear(traveledModelCm);

        var sTotal = ModelCmToPrototypeM(TotalDistanceCmModel);
        if (sTotal <= 0.0)
            return EvaluateLinear(traveledModelCm);

        var cp1DistanceCm = _controlPoint.XModelRatio * TotalDistanceCmModel;
        var cp1SpeedMs = _controlPoint.SpeedKmhPrototype / 3.6;
        var sCp1 = ModelCmToPrototypeM(cp1DistanceCm);

        if (sCp1 <= 0.0 || sCp1 >= sTotal)
            return EvaluateLinear(traveledModelCm);

        var s = ModelCmToPrototypeM(traveledModelCm);

        if (_secondaryControlPoint is null)
        {
            if (s <= sCp1)
            {
                return EvaluateSegmentSpeed(s, 0.0, sCp1, _v0Ms, cp1SpeedMs);
            }

            if (traveledModelCm >= TotalDistanceCmModel)
                return _v1Ms;

            return EvaluateSegmentSpeed(s, sCp1, sTotal, cp1SpeedMs, _v1Ms);
        }

        var cp2DistanceCm = _secondaryControlPoint.XModelRatio * TotalDistanceCmModel;
        var cp2SpeedMs = _secondaryControlPoint.SpeedKmhPrototype / 3.6;
        var sCp2 = ModelCmToPrototypeM(cp2DistanceCm);

        if (sCp2 <= sCp1 || sCp2 >= sTotal)
            return EvaluateLinear(traveledModelCm);

        if (s <= sCp1)
        {
            return EvaluateSegmentSpeed(s, 0.0, sCp1, _v0Ms, cp1SpeedMs);
        }

        if (s <= sCp2)
        {
            return EvaluateSegmentSpeed(s, sCp1, sCp2, cp1SpeedMs, cp2SpeedMs);
        }

        if (traveledModelCm >= TotalDistanceCmModel)
            return _v1Ms;

        return EvaluateSegmentSpeed(s, sCp2, sTotal, cp2SpeedMs, _v1Ms);
    }

    private static double EvaluateSegmentSpeed(double s, double sStart, double sEnd, double vStartMs, double vEndMs)
    {
        var segmentLength = sEnd - sStart;
        if (segmentLength <= 0.0)
            return vEndMs;

        var segmentTravel = Math.Clamp(s - sStart, 0.0, segmentLength);
        var a = ((vEndMs * vEndMs) - (vStartMs * vStartMs)) / (2.0 * segmentLength);
        var vSquared = (vStartMs * vStartMs) + (2.0 * a * segmentTravel);
        return Math.Sqrt(Math.Max(0.0, vSquared));
    }

    private double EvaluateEaseInOut(double traveledModelCm)
    {
        var ratio = Math.Clamp(traveledModelCm / TotalDistanceCmModel, 0.0, 1.0);

        // Monotonic S-curve blend in [0..1].
        var left = Math.Pow(ratio, _shapeExponent);
        var right = Math.Pow(1.0 - ratio, _shapeExponent);
        var blend = left / (left + right);

        var speed = _v0Ms + (_v1Ms - _v0Ms) * blend;
        if (traveledModelCm >= TotalDistanceCmModel)
            return _v1Ms;

        return Math.Max(0.0, speed);
    }

    private double ModelCmToPrototypeM(double modelCm)
    {
        return modelCm * _scale / 100.0;
    }
}




