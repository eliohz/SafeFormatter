# SafeFormatter – Sichere SD-/USB-Aufbereitung (WPF, .NET 8)

> **Hinweis:** Dieses Projekt erfüllt die beschriebenen Anforderungen: Es listet **nur echte Wechseldatenträger** (USB/SD), filtert interne Laufwerke hart heraus, führt **immer** einen vollständigen *Clean All* gefolgt von Partitionierung + Formatierung (automatisch FAT32 ≤ 32 GB, sonst exFAT) aus, benötigt Adminrechte, zeigt eine ruhige Kachel-UI, Fortschritt, Zeitmessung und ein verständliches Log inkl. Download-Link.

---

## Projektstruktur
```
SafeFormatter/
├─ SafeFormatter.csproj
├─ App.xaml
├─ App.xaml.cs
├─ app.manifest
├─ Models/
│  └─ DiskInfo.cs
├─ Services/
│  ├─ DiskService.cs
│  └─ ProcessRunner.cs
├─ ViewModels/
│  └─ MainViewModel.cs
├─ Views/
│  ├─ MainWindow.xaml
│  └─ MainWindow.xaml.cs
└─ Themes/
   └─ Theme.xaml
```

---

## Hinweise zur Formatierungslogik (Label setzen)
Aktuell setzt der `format`-Schritt kein Label, um den Verzicht auf „Quick“ nicht zu gefährden. Um das Label **direkt** beim Formatieren zu setzen, kannst du die Format-Kommandos wie folgt anpassen:

```csharp
// In DiskService.RunDiskpartScript() beim Einfügen der Commands
// Ersetze im Aufrufer (CleanAllAndFormatAsync) die Zeile "format fs=..." durch:
var fmt = disk.RecommendedFs.Equals("FAT32", StringComparison.OrdinalIgnoreCase)
    ? $"format fs=fat32 label=\"{label?.Replace("\"", "")}\""
    : $"format fs=exfat label=\"{label?.Replace("\"", "")}\"";
```

Damit wird weiterhin **kein** `quick` verwendet (Vollformat).

---

## Barrierefreiheit & Sicherheit
- Fokusreihenfolge ist linear, UI-Elemente haben klare Beschriftungen (Screenreader-freundlich über `AutomationProperties.Name`).
- Warnfarbe wird nur in Fehlermeldungen genutzt.
- **Kein Expertenmodus** – interne Laufwerke werden nie angezeigt, da hart gefiltert.

---

## Build & Ausführen
1. Mit Visual Studio 2022/2025 oder `dotnet` CLI öffnen/bauen:
   ```bash
   dotnet build
   dotnet run --project SafeFormatter
   ```
2. Beim Start fordert Windows UAC Adminrechte an (Manifest).  
3. Medien einstecken → Kachel auswählen → Checkbox bestätigen → optional Label setzen → **Starten**.

---

## Wichtige Hinweise
- **Clean All** kann je nach Größe lange dauern (schreibt Nullblöcke auf das gesamte Medium).
- Während des Vorgangs keine Explorerfenster des Sticks geöffnet lassen.
- Für SD-Karten mit Schreibschutzschieber: ggf. entriegeln.

---

## Erweiterungen (optional)
- Live-Hotplug via WMI Eventing (`__InstanceCreationEvent` auf `Win32_DiskDrive`).
- Dark Mode Umschalter & Systemthemen-Erkennung.
- Mehrsprachigkeit (de/en) via `Resx`.
- Signierte Ausführung (Code Signing) für SmartScreen.
