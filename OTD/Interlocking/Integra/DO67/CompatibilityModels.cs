public class Hauptsignal
{
}

public class Zwergsignal
{
    public bool L1 { get; set; }
    public bool L2 { get; set; }
    public bool L3 { get; set; }
}

public class VorsignalSZRS
{
    private bool _rm;

    public void Update(bool halt, bool freieFahrt, bool warnung, bool fahrt13)
    {
        _rm = halt || freieFahrt || warnung || fahrt13;
    }

    public bool GetRM() => _rm;

    public void Commit()
    {
    }
}

public class Kontakt
{
    private readonly Kondensator _timer = new();

    public void Init(int kapazitaet, int an, int aus) => _timer.Init(kapazitaet, an, aus);

    public void Update(bool laden, bool entladen)
    {
        _timer.Set(laden);
        if (entladen) _timer.Entladen(1, true);
        _timer.Update();
    }

    public bool O() => _timer.Value;
}

public class Schleife
{
    public enum Spannung
    {
        Minus_Niederohmig = -2,
        Minus_Hochohmig = -1,
        Aus = 0,
        Plus_Hochohmig = 1,
        Plus_Niederohmig = 2
    }

    public enum Leitwert
    {
        Unterbruch,
        Hochohmig,
        Niederohmig
    }

    private Schleife? _other;
    private Spannung _spannung;
    private Leitwert _minus;
    private Leitwert _plus;

    public static void Connect(Schleife first, Schleife second)
    {
        first._other = second;
        second._other = first;
    }

    public void SetSpannung(Spannung value) => _spannung = value;

    public Spannung GetSpannung() => _other?._spannung ?? _spannung;

    public void SetLeitwert(Leitwert minus, Leitwert plus)
    {
        _minus = minus;
        _plus = plus;
    }

    public Leitwert GetLeitwertMinus() => _other?._minus ?? _minus;

    public Leitwert GetLeitwertPlus() => _other?._plus ?? _plus;
}

public class TMN825_AS_AE
{
    public bool Gsk() => true;

    public bool IsAus() => true;
}
