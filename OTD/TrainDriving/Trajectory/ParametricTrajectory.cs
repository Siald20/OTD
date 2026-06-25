// SPDX-License-Identifier: GPL-3.0-or-later

using System;

namespace OTD.TrainDriving.Trajectory;

/// <summary>
/// Generic trajectory that supports linear, control-point and ease-in-out curve shapes.
/// </summary>
public sealed class ParametricTrajectory : ISpeedTrajectory
{
    private readonly double _v0Ms;
    private readonly double _v1Ms;
    private readonly double _scale;
    private readonly TrajectoryCurveType _curveType;
    private readonly TrajectoryControlPoint? _controlPoint;
    private readonly double _shapeExponent;

    public ParametricTrajectory(DrivingTrajectoryRequest request)
    {
        request.Validate();

        TotalDistanceCmModel = request.DistanceCmModel;
        _scale = request.Scale;
        _curveType = request.CurveType;
        _controlPoint = request.ControlPoint;

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

        var cpDistanceCm = _controlPoint.XModelRatio * TotalDistanceCmModel;
        var cpSpeedMs = _controlPoint.SpeedKmhPrototype / 3.6;

        var sTotal = ModelCmToPrototypeM(TotalDistanceCmModel);
        var sCp = ModelCmToPrototypeM(cpDistanceCm);

        if (sTotal <= 0.0 || sCp <= 0.0 || sCp >= sTotal)
            return EvaluateLinear(traveledModelCm);

        var s = ModelCmToPrototypeM(traveledModelCm);

        if (s <= sCp)
        {
            var a1 = ((cpSpeedMs * cpSpeedMs) - (_v0Ms * _v0Ms)) / (2.0 * sCp);
            var vSquared1 = (_v0Ms * _v0Ms) + (2.0 * a1 * s);
            return Math.Sqrt(Math.Max(0.0, vSquared1));
        }

        var s2 = sTotal - sCp;
        var a2 = ((_v1Ms * _v1Ms) - (cpSpeedMs * cpSpeedMs)) / (2.0 * s2);
        var sFromCp = s - sCp;
        var vSquared2 = (cpSpeedMs * cpSpeedMs) + (2.0 * a2 * sFromCp);

        if (traveledModelCm >= TotalDistanceCmModel)
            return _v1Ms;

        return Math.Sqrt(Math.Max(0.0, vSquared2));
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



