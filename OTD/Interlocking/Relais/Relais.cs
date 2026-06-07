using System;

// Anmerkung: Die "LogikManager" Funktionen (lm_add) wurden hier weggelassen, 
// da sie in C# meistens über Event-Handler oder zentrale Listen abgewickelt werden.

public class RelaisBase
{
    protected bool m_gs; // Relais in Grundstellung
    protected bool m_as; // Relais in Arbeitsstellung
    protected readonly bool m_gs_anz; // Grundstellung ist angezogen
    protected bool m_gekeilt; // Umstellen verhindert

    public RelaisBase(bool gs_anz)
    {
        m_gs_anz = gs_anz;
        m_gs = true;
        m_as = false;
        m_gekeilt = false;
    }

    // Ersetzt den operator bool() (Schliesser)
    public virtual bool Value => a(); 
    
    // Ersetzt den operator!() (Öffner)
    public bool NotValue => g();

    public bool g() => m_gs; // Relais in Grundstellung
    public bool a() => m_as; // Relais in Arbeitsstellung
    public bool vg() => !m_as; // Relais in Grundstellung, verzögerter Kontakt
    public bool va() => !m_gs; // Relais in Arbeitsstellung, verzögerter Kontakt

    public void Set(bool as_val) 
    { 
        m_gs = !as_val; 
        m_as = as_val; 
    }

    public void Toggle() 
    { 
        m_gs = !m_gs; 
        m_as = !m_as; 
    }

    public void Move(bool spule)
    {
        if (m_gekeilt) return;
        bool n = spule != m_gs_anz;
        if (n) {
            if (m_gs) m_gs = false;
            else m_as = true;
        }
        else {
            if (m_as) m_as = false;
            else m_gs = true;
        }
    }

    public bool IstAngezogen() => m_gs_anz ? m_gs : m_as;
    
    public void Keilen(bool gekeilt) => m_gekeilt = gekeilt;
    
    public bool Gekeilt() => m_gekeilt;
}

public class Relais : RelaisBase
{
    public bool sp; // Spule hat Strom

    // Ermöglicht den direkten Lese-/Schreibzugriff wie in C++ (L1 = true;)
    public new bool Value 
    {
        get => base.Value;
        set => sp = value;
    }

    public Relais() : base(false) { sp = false; }
    
    public void Init(bool anz) 
    { 
        sp = anz; 
        Set(sp); 
    }
    
    public void Commit() { Move(sp); }
}

public class RelaisInv : RelaisBase
{
    public bool sp;

    public new bool Value 
    {
        get => base.Value;
        set => sp = value;
    }

    public RelaisInv() : base(true) { sp = false; }
    
    public void Init(bool anz) 
    { 
        sp = anz; 
        Set(!sp); 
    }
    
    public void Commit() { Move(sp); }
}

public class Haftrelais : RelaisBase
{
    public new bool a; // Anzug
    public bool b; // Abwurf
    private bool sp; // Zustand Spule

    public Haftrelais(bool gs_anz = true) : base(gs_anz) 
    { 
        a = false; 
        b = false; 
        sp = gs_anz; 
    }

    public void Init(bool anz)
    {
        sp = anz;
        Set(sp);
    }

    public void Commit()
    {
        if (b) sp = false;
        else if (a) sp = true;
        Move(sp);
        a = false;
        b = false;
    }
}

public class RelaisGestuetzt : RelaisBase
{
    public bool sp;
    private RelaisBase? m_stuetzer;

    public new bool Value
    {
        get => base.Value;
        set => sp = value;
    }

    public RelaisGestuetzt() : base(false) { sp = false; }
    
    public void SetStuetzer(RelaisBase r) { m_stuetzer = r; }
    
    public void Init(bool anz) { sp = anz; Set(sp); }
    
    public void Commit()
    {
        if (sp) Move(true);
        else if (m_stuetzer != null && m_stuetzer.IstAngezogen()) Move(false);
        else if (!m_gs && !m_as) Move(false);
        sp = false;
    }
}

public class Stuetzrelais
{
    public bool sa; // Spule für Arbeitsstellung
    public bool sg; // Spule für Grundstellung
    public RelaisBase gu;
    public RelaisBase go;

    public Stuetzrelais()
    {
        gu = new RelaisBase(false);
        go = new RelaisBase(true);
        sa = false;
        sg = false;
    }

    public void Commit()
    {
        if (!sg && gu.a()) go.Move(false);
        if (!sa && go.g()) gu.Move(false);
        if (sg) go.Move(true);
        if (sa) gu.Move(true);
        sg = false;
        sa = false;
    }
}
