using System;

public abstract class RelaisSatz
{
    protected RelaisSatz()
    {
        // In der C++ Version wurde hier lm_add(this) aufgerufen, um den 
        // RelaisSatz automatisch beim LogikManager (für den Tick-Zyklus) anzumelden.
        // Falls du eine LogikManager-Klasse in C# hast, kannst du das hier ergänzen:
        // LogikManager.Add(this);
    }

    // Virtuelle Methoden, die von den spezifischen Baugruppen (z.B. TMN500_WSR) 
    // mit 'override' überschrieben werden.

    public virtual void Update()
    {
        // Standardmäßig leer, wird von der Baugruppe implementiert
    }

    public virtual bool UpdateWire()
    {
        // Standardmäßig keine Änderungen an der Verkabelung
        return false;
    }

    public virtual void Output()
    {
        // Standardmäßig leer, wird von der Baugruppe implementiert
    }
}