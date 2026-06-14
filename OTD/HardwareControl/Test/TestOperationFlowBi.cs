// // SPDX-License-Identifier: GPL-3.0-or-later
// //
// // OpenTrainDrive - DecoderControl
// // Copyright (C) 2026
// //
// // Authors:
// // - Hansueli Alder <name@example.com>
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

using System;
using System.Threading.Tasks;

namespace OTD.HardwareControl;

internal static class TestOperationFlowBi
{
    public static void Run(CommandStation commandStation, Feedback feedbackModule)
    {
        ArgumentNullException.ThrowIfNull(commandStation);
        ArgumentNullException.ThrowIfNull(feedbackModule);

        RunAsync(commandStation).GetAwaiter().GetResult();
    }

    private static async Task RunAsync(CommandStation commandStation)
    {
        var dkwW2 = new Accessory(Guid.Parse("4a9b2c3d-5e6f-4a7b-9c0d-1e2f3a4b5c6d"));
        var turnoutW1 = new Accessory(Guid.Parse("3f8a1b2c-4d5e-4f7a-8b9c-0d1e2f3a4b5c"));
        var turnoutW3 = new Accessory(Guid.Parse("b2f3eaa9-43f9-414e-997c-92d44406a63b"));
        var turnoutW4 = new Accessory(Guid.Parse("bbcc3ec1-25ee-44d5-b086-6a13e33c548f"));
        var threeWayW5W6 = new Accessory(Guid.Parse("5b0c3d4e-6f7a-4b8c-9d0e-2f3a4b5c6d7e"));

        await dkwW2.SubscribeCommandStationAsync(commandStation);
        await turnoutW1.SubscribeCommandStationAsync(commandStation);
        await turnoutW3.SubscribeCommandStationAsync(commandStation);
        await threeWayW5W6.SubscribeCommandStationAsync(commandStation);

        // Ausfahrt Bi2 -> Bi13
        Console.WriteLine("[Flow] 1) W2 -> crossing-straight");
        await dkwW2.SetStateAsync("crossing-straight");
        Console.WriteLine("[Flow] 2) W1 -> diverging");
        await turnoutW1.SetStateAsync("diverging");
        
        await Task.Delay(TimeSpan.FromSeconds(5));
        
        // Ausfahrt Bi1 -> Bi13  
        Console.WriteLine("[Flow] 3) W3 -> diverging");
        await turnoutW3.SetStateAsync("diverging");
        Console.WriteLine("[Flow] 4) W2 -> crossing");
        await dkwW2.SetStateAsync("crossing");

        await Task.Delay(TimeSpan.FromSeconds(5));
        
        // Ausfahrt Bi1 -> Bi91
        Console.WriteLine("[Flow] 5) W1 -> straight");
        await turnoutW1.SetStateAsync("straight");
        
        await Task.Delay(TimeSpan.FromSeconds(5));
        
        // Einfahrt Bi91 -> Bi2
        Console.WriteLine("[Flow] 6) W5/6 -> straight");
        await threeWayW5W6.SetStateAsync("straight");

        await Task.Delay(TimeSpan.FromSeconds(5));
        
        // Ausfahrt Bi3 -> Bi91
        Console.WriteLine("[Flow] 7) W5/6 -> left");
        await threeWayW5W6.SetStateAsync("left");

        await Task.Delay(TimeSpan.FromSeconds(5));
        
        // Einfahrt Bi91 -> Bi1
        Console.WriteLine("[Flow] 8) W5/6 -> right");
        await threeWayW5W6.SetStateAsync("right");
    }
}