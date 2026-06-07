using System;

public class Stecker
{
    private readonly bool[] m_pins = new bool[24];
    private Stecker? m_other;

    public void Set(int pin, bool val)
    {
        if (pin < 1 || pin > 24) throw new ArgumentOutOfRangeException(nameof(pin));

        if (m_other != null)
        {
            m_other.m_pins[pin - 1] = val;
        }
    }

    public bool Get(int pin)
    {
        if (pin < 1 || pin > 24) throw new ArgumentOutOfRangeException(nameof(pin));
        return m_pins[pin - 1];
    }

    public static void Connect(Stecker s1, Stecker s2)
    {
        s1.m_other = s2;
        s2.m_other = s1;
    }
}
