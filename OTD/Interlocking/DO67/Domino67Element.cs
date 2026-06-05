namespace OTD.Interlocking.DO67;

using System;
using System.Collections.Generic;

public abstract class Domino67Element
{
    public string ID { get; set; } = string.Empty;

    // Elektrischer Zustand des 24-poligen Spurkabels am Element.
    public SpurkabelDomino67 Spurkabel;

    private readonly Dictionary<string, DominoStecker> _stecker =
        new(StringComparer.OrdinalIgnoreCase);

    protected Domino67Element()
    {
        RegisterStecker("S");
        RegisterStecker("R");
        RegisterStecker("L");
        RegisterStecker("E");
        RegisterStecker("A");
        RegisterStecker("EH");
        RegisterStecker("AH");
    }

    public DominoStecker SteckerS => GetStecker("S");
    public DominoStecker SteckerR => GetStecker("R");
    public DominoStecker SteckerL => GetStecker("L");
    public DominoStecker SteckerE => GetStecker("E");
    public DominoStecker SteckerA => GetStecker("A");
    public DominoStecker SteckerEH => GetStecker("EH");
    public DominoStecker SteckerAH => GetStecker("AH");

    protected DominoStecker RegisterStecker(string name)
    {
        if (_stecker.ContainsKey(name))
        {
            throw new InvalidOperationException($"Stecker '{name}' existiert bereits.");
        }

        var stecker = new DominoStecker(this, name);
        _stecker.Add(name, stecker);
        return stecker;
    }

    public DominoStecker GetStecker(string name)
    {
        if (_stecker.TryGetValue(name, out var stecker))
        {
            return stecker;
        }

        throw new KeyNotFoundException($"Stecker '{name}' nicht gefunden.");
    }

    public VirtuellesKabel VerbindeMit(Domino67Element zielElement, string eigenerStecker, string zielStecker)
    {
        return new VirtuellesKabel(GetStecker(eigenerStecker), zielElement.GetStecker(zielStecker));
    }

    public abstract void VerarbeiteSpurstroeme();
}
