using System;

public class Flachrelais : RelaisBase
{
    public bool sp; // Spule hat Strom

    // Ermöglicht den direkten Lese-/Schreibzugriff (z.B. FUS_ZR.Value = true;)
    public new bool Value 
    {
        get => base.Value;
        set => sp = value;
    }

    // Grundstellung ist abgefallen (false)
    public Flachrelais() : base(false) 
    { 
        sp = false; 
    }
    
    public void Init(bool anz) 
    { 
        sp = anz; 
        Set(sp); 
    }
    
    // Übernimmt den Spulenzustand in die mechanische Stellung (Anker)
    public void Commit() 
    { 
        Move(sp); 
    }
}