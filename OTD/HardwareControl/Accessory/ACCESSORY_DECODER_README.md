## Accessory Decoder - Architektur und Nutzung (Stand 2026-06)

### Kernregel

Der Accessory-Decoder ist für **stationäre** Decoder (Weichen, Signale, Entkuppler) und ist fest an **genau eine** Kommandozentrale gebunden.

- Fahrzeugdecoder: mobil, können zwischen Zentralen wechseln -> Subscribe-Modell in `OTD/HardwareControl/Train/LocoDecoder.cs`
- Zubehördecoder: stationär, eine feste Zentrale -> `OTD/HardwareControl/Accessory/AccessoryAccessoryDecoder.cs`

### API-Überblick (value-basiert)

- Konstruktor: `new AccessoryAccessoryDecoder(XElement decoderConfiguration)`
- Feste Zentrale über Subscribe: `SubscribeCommandStationAsync(...)`
- Senden: `SendValueAsync(int value, CancellationToken)`
- Decoder-Infos: `Address`, `ChannelCount`, `Protocol`
- Event: `StateChanged`

### Konfigurationsbeispiel

```xml
<accessory uid="switch-001">
  <decoder>
    <protocol>DCC</protocol>
    <activationtime>500</activationtime>
  </decoder>
  <states>
    <state id="straight" description="gerade">
      <decoder address="11" value="0"/>
    </state>
    <state id="diverging" description="abzweigend">
      <decoder address="11" value="1"/>
    </state>
  </states>
</accessory>
```

### Nutzung

```csharp
var accessoryElement = XElement.Load("accessories.xml").Element("decoder")!;

var commandStation = new LoDiRektor();
await commandStation.ConnectAsync("192.168.1.100", 5550);

var accessoryDecoder = new AccessoryAccessoryDecoder(accessoryElement);
await accessoryDecoder.SubscribeCommandStationAsync(commandStation);

await accessoryDecoder.SendValueAsync(0x01); // z.B. DCC basic: Zustand/Ausgangskombination
await accessoryDecoder.SendValueAsync(0x00); // aus
```

### Mock-Testbeispiel

```csharp
[TestMethod]
public async Task AccessoryDecoder_SendValue_WorksCorrectly()
{
    var mock = new MockCommandStation();
    await mock.ConnectAsync("localhost", 5550);
    await mock.SetPowerAsync(true);

    var accessoryElement = CreateTestAccessoryConfig();
    var decoder = new AccessoryAccessoryDecoder(accessoryElement);
    await decoder.SubscribeCommandStationAsync(mock);

    await decoder.SendValueAsync(0x02);
    Assert.IsTrue(mock.AccessoryValues.ContainsKey(decoder.Address));
    Assert.AreEqual((byte)0x02, mock.AccessoryValues[decoder.Address]);
}
```

### Hinweise für die Systemintegration

- Wenn es mehrere Zentralen im Gesamtsystem gibt, sollte die Zuordnung im Layout/Loader erfolgen (z. B. über `commandStationId`).
- Der Accessory-Decoder verwendet `SendValueAsync(...)` als einzige Sende-API.
- Eine nicht verbundene `CommandStation` führt beim Senden zu einem Fehler aus der jeweiligen Zentrale.

---

**Status**: implementiert  
**Namespace**: `OTD.HardwareControl.Accessory`  
**Interface**: `IAccessoryDecoder`  
**Implementierung**: `AccessoryAccessoryDecoder`

