# VolumeController5 — finální verze (5 sliderů, auto-port, pinned apps okno)

Co umí:
- 5 potenciometrů/sliderů z Arduina → absolutní hlasitost (0..1023 → 0..1)
- 5 slotů v aplikaci (dropdown): **MASTER** nebo konkrétní `xxx.exe`
- Dropdown primárně bere **Windows Mixer (audio sessions)**, ale:
  - máš tlačítko **„Připnuté aplikace…“** → otevře okno, kde si vybereš a uložíš EXE,
  - připnuté EXE jsou **v dropdownu vždy**, i když teď nehrají (Spotify apod.)
- Auto detekce portu (USB priorita, fallback BT), bez nutnosti ručního výběru
- Opravené hledání portu: při scanu **nevypíná/neresetuje Arduino pořád dokola** (DTR/RTS off)
- UI nezamrzá: audio nastavování běží na background threadu + deadband + ~20 FPS update

Poznámka:
- Windows umí změnit hlasitost EXE jen když má **audio session** (je ve Volume Mixeru).
  Když session ještě neexistuje, změna se projeví až ve chvíli, kdy aplikace začne hrát.
  (Aplikace si hlasitost „pamatuje“ a znovu ji aplikuje při refreshi seznamu.)

---

## 1) Arduino
Nahraj `arduino/VolumeController5.ino` (Arduino IDE).

- USB: 115200
- HC-05 BT: 9600
- Handshake: PC posílá `PING` → Arduino odpoví `PONG,VC5`

---

## 2) PC aplikace

### Spuštění (debug)
Dvojklik:
- `scripts/Run_Debug.bat`

nebo ručně ve složce `pc-app/VolumeController5`:
```powershell
dotnet restore
dotnet run
```

### EXE (single-file)
Dvojklik:
- `scripts/Publish_SingleFile.bat`

Výsledek:
`pc-app/VolumeController5/bin/Release/net8.0-windows/win-x64/publish/VolumeController5.exe`

---

## 3) Použití
1) Spusť aplikaci → sama hledá port (uvidíš to nahoře u „Port“).
2) Klikni **„Připnuté aplikace…“** a přidej třeba `spotify.exe` (nebo vyber ze seznamu běžících).
3) V každém slotu vyber MASTER nebo EXE.


## USB<->BT failover (nově)
- Arduino posílá přes USB `PINGPC` a čeká na odpověď `PONGPC`.
- Když 3× po sobě nepřijde odpověď, Arduino začne posílat data přes BT.
- Jakmile se zase podaří ping, přepne zpět na USB.
- V aplikaci je tlačítko **Nastavení portů…**, kde můžeš ručně nastavit BT COM port.
- Když aplikace běží na BT, každých pár sekund zkouší, jestli je dostupné USB a přepne zpět.


## Kompaktní publish
Původní publish byl `self-contained`, takže do výstupu přibalil celý .NET runtime a měl klidně přes 100 MB.
Teď je skript nastavený na **framework-dependent single-file publish**, takže výstup je výrazně menší.

Spuštění:
- `scripts/Run_Debug.bat` = debug běh
- `scripts/Publish_SingleFile.bat` = kompaktní EXE

Poznámka:
- kompaktní EXE vyžaduje, aby cílový počítač měl nainstalovaný **.NET Desktop Runtime 8**
- ve zdrojovém ZIPu už nejsou přibalené složky `bin/` a `obj/`


## Novinky v2.3
- opravený kontrast textu v ComboBox dropdownu
- přidané logo aplikace + ikona EXE/tray
- kliknutí na křížek okno nezavře proces, ale schová aplikaci do systémové lišty (tray)
- obnovení okna: dvojklik na tray ikonu nebo pravé tlačítko → Otevřít


## v2.3.2 opravy
- opravený Publish_SingleFile.bat (už nepadá na NETSDK1176)
- tmavý ComboBox/dropdown s čitelným textem v hlavním okně i v nastavení portů


## v2.3.3 opravy
- logo a ikona jsou teď zabalené jako WPF Resource
- MainWindow používá pack URI pro logo i ikonu
- tray ikona se načítá z embedded resource streamu, takže publish EXE už po startu nespadne kvůli Assets cestě


## v2.3.4 úpravy UI
- z hlavního UI odstraněno logo, název a podtitul z horní lišty
- tlačítka v horní liště jsou zarovnaná doprava
- odstraněn blok „Mapování“
- v okně připnutých aplikací přidáno tlačítko „Odebrat vše“


## v2.3.4.1 oprava buildu
- opraven konflikt `MessageBox` v `PinnedAppsWindow.xaml.cs` (WPF vs WinForms)


## v2.3.4.2 drobné UI úpravy
- z tlačítek v horní liště odstraněny tři tečky
- v nastavení portů lze nově nastavit USB i BT port zvlášť, oba i v režimu Auto
- upravený popis v nastavení portů bez zmínky o konkrétním modulu


## v2.3.4.3 drobné UI opravy
- opraven hover vzhled ComboBoxů (už se nesvětlají tak, že nejde přečíst text)
- okno Nastavení portů je vyšší a tip se už neořezává
