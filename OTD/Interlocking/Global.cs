using System;

public static class Global
{
    // Globale Zustandsvariablen
    public static bool g_Blinker = false;
    public static bool g_settled = false;
    
    // Zykluszeiten (z.B. 50 Zyklen pro Sekunde = 20ms)
    public static int g_cycle_time_ms = 20; 
    public static int g_cycles_per_sec = 50;

    // Compiler-Switches / Defines aus C++
    public const bool ENABLE_DUMMY_HSIG = true;
    public const bool ENABLE_DUMMY_ZSIG = true;
    public const bool ENABLE_DUMMY_WEICHE = true;
    public const bool ENABLE_DUMMY_VSIG = true;

    public const bool INIT_IS_FREI = true;
    public const bool CONFIG_SP_E = true;
    public const bool CONFIG_SP_A = true;
}
