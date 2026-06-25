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


public class TMN825_AS_AE
{
    public bool Gsk() => true;

    public bool IsAus() => true;
}
