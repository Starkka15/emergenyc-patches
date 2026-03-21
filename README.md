# EmergeNYC Community Patches

> ⚠️ **Very Early Alpha — Things Will Break**
>
> This is unofficial community work reverse-engineered from the base game. Not affiliated with or endorsed by the EmergeNYC development team. Install at your own risk. Multiplayer has not been tested!

---

## What's Included

### EmergeNYC.CommunityFixes (v0.2.1)
Core bug fixes targeting crashes and broken gameplay across the base game.

**Emergency Dispatch**
- Fixed off-by-one causing the last event in rotation to never fire
- Fixed infinite loop when all emergency events had triggered once
- Fixed emergencies activating 2–3× instead of once per call
- Fixed crash in `ReenableAllCalls` when any event had no mission button
- Injected EMS events into the rotation when the scene has none defined

**Fire System**
- Fixed fire particle system cache logic (was inverted — broke extinguishing)
- Fixed division by zero on fire startup multiplier
- Fixed fire spread controller double-registering itself
- Fixed debug key (Alpha9) left active in release builds

**Water Supply**
- Fixed `OnWaterDepleted`/`OnWaterRestored`/`OnWaterFull` firing every frame (major performance drain — now only fires on actual state transitions)
- Fixed `OnPlayerEnteredRoom` null check logic (was inverted)
- Fixed crash in `OnDestroy` when parent is null
- Fixed infinite recursion in `GetPhotonViewInParents`
- Fixed missing null check in `OnUseWater`

**Procedural Emergency Spawning**
- Fixed 12+ `FindObjectOfType` calls with no null checks — game crashed on any map with a missing scene connector
- Fixed `SpawnManholeFire` NRE on non-host clients
- Fixed `ClearCallRPC` crash when `currentcall` was already null
- Training Map: car fire now attempts to fall back to available parked vehicle positions when the standard connector is absent

**Networking / Multiplayer**
- Fixed `ConnectionManager` singleton never being assigned
- Fixed `OnJoinedRoom` called twice when hosting
- Fixed static event subscription leak in `ConnectionManager`
- Fixed door state not syncing to players who join mid-session
- Fixed hose attach point RPC crash when `attachPoint` was null

**Traffic**
- Fixed car despawn distance measured from the traffic controller instead of the camera (cars were popping in/out visibly close to the player)
- Fixed `CanDeactivate` always returning true
- Fixed ghost cars spawning on dead-end roads
- Fixed hardcoded Heavy traffic density ignoring actual settings
- Added emergency vehicle yielding behaviour for traffic AI

**Vehicles / Physics**
- Fixed `NoOfGears` being a static field shared by all vehicles (changing gears on one vehicle changed all vehicles simultaneously)
- Fixed handbrake torque not clearing after release
- Fixed steering helper euler angle wrap bug at the 0/360 boundary

**Characters / AI**
- Fixed guaranteed crash in `CharacterAirHandler` on trigger exit (null reference immediately after assigning null to `airZone`)
- Fixed air mask enable/disable methods being swapped
- Fixed NRE in `AIManager` when parent transform was null
- Fixed various null ref crashes in AI health monitor and population AI

**Other**
- Fixed `ControlFire` list mutation during iteration (items being skipped on removal)
- Fixed static lists not clearing between scenes (accumulated stale refs)
- Fixed charring speed being frame-rate dependent instead of time-based
- Fixed `Singleton` `OnDestroy` blocking scene transitions
- Fixed stretcher controller, outrigger manager, mount point, and selection manager null refs
- Asset bundle CRC/size validation bypass for community-built bundles

---

### EmergeNYC.EMSEnhancement (v0.2.0)
Adds patient vitals deterioration to EMS calls. In the base game patient vitals are static — this mod makes them decline based on the patient's condition until treated.

**Condition profiles:**
| Condition | Effects |
|---|---|
| Cardiac Arrest | Pulse and BP drop to zero, O2 falls, temperature drops |
| Gunshot Wound | Tachycardia leading to hemorrhagic shock, BP and O2 decline |
| Overdose | Respiratory depression, O2 crashes, bradycardia |
| Respiratory Emergency | O2 drops rapidly, compensatory fast pulse, then collapse |
| Fall / Car Accident | Mild elevation, slow gradual decline |

> ⚠️ Experimental — multiplayer sync is not fully tested.

---

### EmergeNYC.PoliceEnhancement *(not yet released)*
Police dispatch events and traffic stop mechanic. Still in development — not stable enough for general use.

---

## Known Limitations

- **Training Map**: Car Fire, Plane Fire, and Boat Fire scenarios do not spawn. These are not properly set up in the Training Map scene by the base game and cannot be fixed through patching alone.
- Police Enhancement is not included in releases yet.
- Patches were developed against a specific game version. Future game updates may break them.
- Diagnostic log files (`CommunityFixes_diag.log`, `EMSEnhancement_diag.log`) are written to `BepInEx/plugins/` on each launch — this is normal and they can be deleted.

---

## Installation

### Requirements
- EmergeNYC (Steam)
- [BepInEx 5.x](https://github.com/BepInEx/BepInEx/releases) — use 5.x, **not** 6.x

### Step 1 — Install BepInEx
Download **BepInEx 5.x** for your platform and extract it directly into your EmergeNYC game folder:

```
EmergeNYC/
  BepInEx/          ← from the zip
  winhttp.dll       ← from the zip (Windows)
  doorstop_config.ini
  EMERGENYC.exe
  EMERGENYC_Data/
```

Run the game once to let BepInEx initialize, then close it. A `BepInEx/plugins/` folder will be created.

> **Linux / Proton:** Set launch options in Steam to:
> ```
> WINEDLLOVERRIDES="winhttp=n,b" %command%
> ```

### Step 2 — Install the plugins
Copy both `.dll` files from the [latest release](../../releases/latest) into:
```
EmergeNYC/BepInEx/plugins/
```

### Step 3 — Verify
Launch the game. Check `BepInEx/LogOutput.log` for:
```
[Info] Loading [EmergeNYC Community Fixes 0.2.1]
[Info] Loading [EmergeNYC EMS Enhancement 0.2.0]
```

### Uninstalling
Delete the `.dll` files from `BepInEx/plugins/`. The base game is not modified — all patches are applied at runtime only.

---

## Building from Source

Requires .NET SDK and the game installed at the path in the `.csproj` files.

```bash
cd src/EmergeNYC.CommunityFixes
dotnet build -c Release

cd src/EmergeNYC.EMSEnhancement
dotnet build -c Release
```

Built DLLs are automatically copied to `BepInEx/plugins/` after each build.

---

## Credits

Community reverse-engineering and patch development. Not affiliated with or endorsed by the EmergeNYC development team.
