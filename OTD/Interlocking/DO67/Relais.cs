namespace OTD.Interlocking.DO67;

public abstract class Relais
{
    protected Relais(string name, double schaltzeitMs = 20.0)
    {
        Name = name;
        SchaltzeitMs = schaltzeitMs;
        TimerMs = schaltzeitMs;
    }

    public string Name { get; }
    public bool IstAngezogen { get; protected set; }
    public bool HatSpannung { get; protected set; }
    protected double SchaltzeitMs { get; }
    protected double TimerMs { get; set; }

    public virtual void SetzeSpannung(bool unterSpannung)
    {
        HatSpannung = unterSpannung;
        UpdateSimulation(SchaltzeitMs);
    }

    public abstract void UpdateSimulation(double deltaMs);

    public virtual void Grundstellung()
    {
        HatSpannung = false;
        IstAngezogen = false;
        TimerMs = SchaltzeitMs;
    }
}

public sealed class MonostabilesRelais : Relais
{
    public MonostabilesRelais(string name, double schaltzeitMs = 20.0)
        : base(name, schaltzeitMs)
    {
    }

    public override void UpdateSimulation(double deltaMs)
    {
        if (HatSpannung)
        {
            if (!IstAngezogen)
            {
                TimerMs -= deltaMs;
                if (TimerMs <= 0)
                {
                    IstAngezogen = true;
                }
            }

            return;
        }

        IstAngezogen = false;
        TimerMs = SchaltzeitMs;
    }
}

public sealed class BistabilesRelais : Relais
{
    private bool _vorherSpannung;
    private bool _setImpulsAktiv;
    private bool _resetImpulsAktiv;

    public BistabilesRelais(string name, double schaltzeitMs = 20.0)
        : base(name, schaltzeitMs)
    {
    }

    public void SetImpuls()
    {
        _setImpulsAktiv = true;
        _resetImpulsAktiv = false;
        TimerMs = SchaltzeitMs;
        UpdateSimulation(SchaltzeitMs);
    }

    public void ResetImpuls()
    {
        _resetImpulsAktiv = true;
        _setImpulsAktiv = false;
        TimerMs = SchaltzeitMs;
        UpdateSimulation(SchaltzeitMs);
    }

    public override void UpdateSimulation(double deltaMs)
    {
        if (_setImpulsAktiv || _resetImpulsAktiv)
        {
            TimerMs -= deltaMs;
            if (TimerMs <= 0)
            {
                if (_setImpulsAktiv)
                {
                    IstAngezogen = true;
                }

                if (_resetImpulsAktiv)
                {
                    IstAngezogen = false;
                }

                _setImpulsAktiv = false;
                _resetImpulsAktiv = false;
                TimerMs = SchaltzeitMs;
            }

            _vorherSpannung = HatSpannung;
            return;
        }

        if (HatSpannung && !_vorherSpannung)
        {
            TimerMs -= deltaMs;
            if (TimerMs <= 0)
            {
                IstAngezogen = !IstAngezogen;
                TimerMs = SchaltzeitMs;
            }
        }
        else
        {
            TimerMs = SchaltzeitMs;
        }

        _vorherSpannung = HatSpannung;
    }

    public override void Grundstellung()
    {
        base.Grundstellung();
        _vorherSpannung = false;
        _setImpulsAktiv = false;
        _resetImpulsAktiv = false;
    }
}
