public sealed class SpurTestQuelle : RelaisSatz
{
    private readonly sbyte[] _values = new sbyte[24];

    public SpurStecker sk_Test = new();

    public sbyte GetValue(int pin) => _values[pin - 1];

    public void SetValue(int pin, sbyte value) => _values[pin - 1] = value;

    public override void Update()
    {
        sk_Test.Reset();
        for (var pin = 1; pin <= 24; pin++) sk_Test.Set(pin, _values[pin - 1]);
    }

    public override bool UpdateWire()
    {
        sk_Test.Commit();
        return sk_Test.HasChanged();
    }
}
