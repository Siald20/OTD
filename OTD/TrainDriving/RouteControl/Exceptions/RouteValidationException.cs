// SPDX-License-Identifier: GPL-3.0-or-later

using System;

namespace OTD.TrainDriving.RouteControl.Exceptions;

public sealed class RouteValidationException : Exception
{
    public RouteValidationException(string message) : base(message)
    {
    }
}

