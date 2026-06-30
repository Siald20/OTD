// SPDX-License-Identifier: GPL-3.0-or-later

using System;

namespace OTD.TrainDriving.RouteControl.Exceptions;

public sealed class RouteUpdateConflictException : Exception
{
    public RouteUpdateConflictException(string message) : base(message)
    {
    }
}

