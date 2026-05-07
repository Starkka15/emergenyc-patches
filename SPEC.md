# EmergeNYC Traffic System Overhaul

## §G — Goal

Overhaul EmergeNYC traffic patches (camera-based spawn/despawn, fewer stuck/parked cars, better flow, GTA V-style emergency yield) AND ship a full-game public modder SDK covering every major game system: emergencies, fire, EMS, traffic, characters, vehicles, dispatch, and sirens.

Also ship `EmergeNYC.TrafficAI.dll`: a custom traffic vehicle AI (`CustomTrafficVehicle`) that replaces `TSTrafficAI`'s movement layer with a proper state-machine-driven controller. Reads `TSNavigation` lane graph for routing, drives via `RCC_CarControllerV3` inputs, does spatial obstacle/edge/siren detection via Unity physics. EV avoidance only triggers on active siren.

---

## §C — Constraints

- BepInEx 5.4.23 + Harmony 2.x patches only — no game DLL modification
- Proton/Linux: `HideManagerGameObject=true` + `HarmonyBackend=cecil` mandatory
- Two distinct traffic systems; both must be handled:
  - **Baked** (`TrafficCarBaked`): keyframe-on-rails, managed by `TrafficMatrixUpdater` + `TrafficSpawner`
  - **TS** (`TSTrafficAI` + `TSNavigation`): real-time AI/physics, managed by `TSTrafficSpawner`
- `TSTrafficSpawner` pool is fixed-size array allocated at Awake — cannot resize at runtime
- `TrafficMatrixUpdater.instance` and `TrafficSpawner.instance` are null in test/sandbox scenes
- Unity 2022.3.1f1 Mono runtime
- Build: `dotnet build src/EmergeNYC.CommunityFixes/EmergeNYC.CommunityFixes.csproj -c Release`
- Build TrafficAI: `dotnet build src/EmergeNYC.TrafficAI/EmergeNYC.TrafficAI.csproj -c Release`
- Deploy: `bin/Release/netstandard2.1/*.dll` → `BepInEx/plugins/`
- `TSNavigation` is its own `MonoBehaviour` — continues updating when `TSTrafficAI` is disabled
- `RCC_CarControllerV3` reads `gasInput/brakeInput/steerInput` public floats in its own `FixedUpdate`

---

## §I — Interfaces

| surface | type | notes |
|---------|------|-------|
| `TSTrafficAI.trafficAIList` | `static List<TSTrafficAI>` | all active TS cars |
| `TSTrafficSpawner.mainInstance` | singleton | nullable |
| `TSTrafficSpawner.myPosition` | private `Vector3` | currently = spawner transform pos (bug) |
| `TSTrafficSpawner.maxDistance` | float | active radius from myPosition |
| `TrafficMatrixUpdater.instance` | singleton | null in some scenes (baked only) |
| `TrafficSpawner.instance` | singleton | null in some scenes (baked only) |
| `TSNavigation.currentLane` | int | current lane index |
| `TSNavigation.changingLane` | bool | lane change in progress |
| `TSNavigation.travelingOnConector` | bool | on junction connector |
| `TSNavigation.lanes[]` | `TSLaneInfo[]` | lane graph |
| `TSNavigation.GetOutOfLane()` | method | triggers lane change right |
| `TSNavigation.LaneChange()` | method | raw lane change (rate-limited) |
| `TSNavigation.lastRequest` | private float | rate limit timestamp for GetOutOfLane |
| `TSTrafficAI.MAXSPEED` | private float | max speed, m/s |
| `TSTrafficAI.myLaneOffset` | private float | lateral steering offset |
| `TSTrafficAI.lastSet` | private float | canMove timer (stop if lastSet+2f>Time.time) |
| `TSTrafficAI.fullStop` | private bool | emergency full-stop flag |
| `TSTrafficAI.players` | private `List<Transform>` | triggers fullStop when non-empty |
| `TSTrafficAI.segDistance` | public float | distance along current segment |
| `TSTrafficAI.canMove` | property | getter: `lastSet+2f < Time.time`; setter (orig): sets lastSet + Destroy(20f) |
| `TSLaneInfo.laneLinkRight` | int | right lane index (-1 = rightmost) |
| `FFD_SirenControl.SirenState` | enum | Off = no siren |
| `NYPDSirenController` | bool fields | `wailing`, `yelping`, `prtying` |
| `FFD_Airhorn` | events | `OnPlay`, `OnStop` |
| `TSSimpleCar.OnCollisionEnter` | method | default: despawn on any collision |
| `IPoolable.Deactivate()` | method | returns car to pool |
| `EmergencyVehicleRegistry` | our static | siren registry, shared across patches |
| `YieldStateManager` | our static | per-car yield state machine |
| **— SDK: Game Systems —** | | |
| `EmergencyManagerV2.instance` | singleton | active emergency state, `activeEvent`, `energencyEvents[]` |
| `EmergencyManagerV2.activeEvent` | `EmergencyEvent` | currently running emergency |
| `ProceduralEmergencySystem` | component on EM2 | call spawner; `currentcall`, `CurrentEmergencyPos`, `ActiveEmergency` |
| `ProceduralEmergencySystem.SpawnEMSCall()` | method | spawn EMS patient call |
| `ProceduralEmergencySystem.SendOutTicket()` | method | dispatch audio + RPC |
| `FireController` | per-fire component | `fireIntensity`, `OnExtinguished`, `OnExinctFire (Action)` |
| `FireController.IgniteFire(float)` | method | ignite/intensify (0–1) |
| `FireController.ExtinguishFire()` | method | extinguish immediately |
| `FireController.ApplySubstanceOnSystem(string,float)` | method | apply water/retardant |
| `EMSPatient.patients` | `static List<EMSPatient>` | all active patients |
| `EMSPatient.RaiseLocalEvent(string)` | method | trigger treatment event on patient |
| `EMSPatient.vascularSystem` | `VascularSystem` | patient vitals/physiology |
| `CharacterManager.instance` | singleton | `SpawnAI()`, `Spawn()`, `firehouses` list |
| `AIManager` | per-character component | `TakeControl()`, `AIControl/PlayerControl`, equipment refs |
| `FFD_SirenControl` | per-vehicle component | `SirenState_Current`, `OnUpdateSiren (Action)`, `SetSiren()` |
| `FFD_SirenControl.SirenState` | enum | `Off, Horn, Wail, Yelp, Prty, Man` |
| `NYPDSirenController` | per-vehicle component | `wailing`, `yelping`, `prtying` bools |
| `VehicleSpawner` | component | `Spawn(string prefab)` |
| **— TrafficAI: RCC + Navigation —** | | |
| `RCC_CarControllerV3.gasInput` | public float | throttle 0–1; write directly |
| `RCC_CarControllerV3.brakeInput` | public float | brake 0–1; write directly |
| `RCC_CarControllerV3.steerInput` | public float | steer -1(left)→1(right); write directly |
| `RCC_CarControllerV3.canControl` | public bool | set false to stop RCC reading its own AI inputs |
| `RCC_CarControllerV3.speed` | public float | current speed km/h read-only |
| `TSNavigation.waypoints` | `TSPoints[]` | current lane waypoints |
| `TSNavigation.currentWaypoint` | int | index into waypoints[] |
| `TSNavigation.RelativeWaypointPosition` | `Vector3` | pre-computed vector to next waypoint (local space) |
| `TSNavigation.currentMaxSpeed` | float | lane speed limit km/h |
| `TSNavigation.lanes` | `TSLaneInfo[]` | full lane graph |
| `TSNavigation.currentLane` | int (property) | current lane index |
| `TSLaneInfo.points` | `TSPoints[]` | world-space waypoints in lane |
| `TSLaneInfo.laneWidth` | float | lane width m (default 2.5) |
| `TSLaneInfo.GetClosest(ref float, Vector3)` | method | closest lane point to world pos |
| `TSLaneInfo.laneLinkRight` | int | right adjacent lane (-1 = none) |
| `TSPoints.point` | `Vector3` | world-space waypoint position |
| `Physics.SphereCast` | Unity API | forward obstacle detection |
| `NavMesh.FindClosestEdge` | Unity API | road edge distance from position |
| `Physics.OverlapBox` | Unity API | shoulder / parked car detection |
| `EmergencyVehicleRegistry` | our static | registered EVs; cross-reference with siren state |
| `FFD_SirenControl.SirenState_Current` | enum field | `!= Off` = siren active |
| **— SDK: Public API (our code) —** | | |
| `EmergeNYCSDK` | our static | top-level SDK entry point, all subsystem refs |
| `EmergencyAPI` | our static | emergency lifecycle hooks |
| `FireAPI` | our static | fire system hooks + helpers |
| `EMSAPI` | our static | patient/treatment hooks |
| `TrafficAPI` | our static | traffic hooks + custom EV registration |
| `CharacterAPI` | our static | AI/player character hooks |
| `VehicleAPI` | our static | vehicle/siren hooks |
| `DispatchAPI` | our static | call generation + unit assignment hooks |
| `EventBus` | our static | cross-system typed event routing |

---

## §V — Invariants

| id | rule |
|----|------|
| V1 | No `FindObjectsOfType`/`FindObjectOfType` in per-car `Update`/`FixedUpdate`. Cache at patch class init or use existing static lists. |
| V2 | Never write `TSTrafficAI.canMove`. Write `lastSet` directly: stop=`lastSet=Time.time`, resume=`lastSet=Time.time-3f`. |
| V3 | Obstacle `SphereCast` origins offset by ≥`(carWidth/2 + 0.1m)` in cast direction — avoids self-collision. |
| V4 | All cull/despawn distance checks use `Camera.main.transform.position`, not any spawner/controller transform. |
| V5 | `YieldStateManager` owns `MAXSPEED` and `myLaneOffset` for yielding cars. No other patch modifies these while `IsYielding(id)==true`. |
| V6 | Stuck/parked detection skips: `IsYielding(id)==true`, `fullStop==true`, `canMove==false` (lastSet within 2s = light/block stop). |
| V7 | Stuck detection is last-resort safety net. Root-cause fixes (segDistance clamp, fullStop cleanup) take priority. |
| V8 | `SirenRegistryPatch` broadcasts to traffic at most once per `BroadcastInterval` (0.3s) regardless of siren count. |
| V9 | Emergency yield handles TS cars (`TSTrafficAI`) only. Baked cars use `TrafficPulloverPatches` coroutine. |
| V10 | Cars within `MinSafeSpawnDist` (40m) of `Camera.main` are never despawned by over-count cull. |
| V11 | SDK public surface is additive only — no breaking changes within a major version. Internal patch classes are not SDK surface. |
| V12 | SDK ships as a separate assembly (`EmergeNYC.ModdingSDK.dll`). CommunityFixes/EMSEnhancement/PoliceEnhancement reference it; external mods reference only SDK dll. |
| V13 | All SDK events are `static event Action<T>` delegates — null-safe invoke via `?.Invoke()`. No event ever throws to the game loop. Exceptions in handlers caught and logged. |
| V14 | SDK never calls game methods on the main thread from a postfix that already modified state — use `BeginYield` / `HarmonyLib.AccessTools` read-only in postfixes, write via prefix or separate method call. |
| V15 | SDK helper queries (e.g. `EmergencyAPI.ActiveEmergency`) are read-only wrappers. SDK does not cache mutable game objects — always dereferences from the live singleton. |
| V16 | CTV `PullRight`/`HardStop` states only activate when `EmergencyVehicleRegistry` contains ≥1 EV whose `FFD_SirenControl.SirenState_Current != Off`. Proximity alone (no siren) never triggers EV avoidance. |
| V17 | CTV lateral displacement clamped by `min(TSLaneInfo.laneWidth / 2, NavMesh.FindClosestEdge distance)` — never push a car off the drivable NavMesh surface. |
| V18 | `TSTrafficAI` disabled (not destroyed) on CTV injection; re-enabled on CTV `OnDisable` — preserves original AI if mod unloads. |
| V19 | `RCC_CarControllerV3.gasInput/brakeInput/steerInput` written exclusively from CTV `FixedUpdate` while CTV active. `TSTrafficAI` disabled so no concurrent write occurs. |
| V20 | Forward `SphereCast` origin = `transform.position + transform.forward * (halfCarLength + 0.5f)` — prevents self-intersection with own collider. |
| V21 | CTV reads `TSNavigation.RelativeWaypointPosition` and `waypoints[]` for steering — does not call TSNavigation methods. TSNavigation updates itself as its own MonoBehaviour. |

---

## §T — Tasks

| id | status | desc | cites |
|----|--------|------|-------|
| T1 | x | `TSTrafficSpawner`: patch `CheckFarCarsSingleThread` + `AddCar` to use `Camera.main.transform.position` instead of `myPosition` | V4 |
| T2 | x | Spawn guard: reject `AddCar` spawn point within 30m of camera (prevents pop-in next to player) | V4 |
| T3 | x | Stuck detection: reduce timeout 12s→7s; add parked-car path (`canMove==true`, speed<0.3m/s, not yielding, not fullStop) → respawn at 5s | V6,V7 |
| T4 | x | Siren broadcast: apply direction-aware radii (`Behind=80m`, `Beside=60m`, `Ahead=40m`, `Opposite=50m`) instead of flat `DefaultBroadcastRadius` | V8,V9 |
| T5 | x | Yield merge: check right-neighbor gap before `GetOutOfLane`; if gap too small, briefly set `SpeedMergeSpeedUp` on trailing car to create gap | V5,V9 |
| T6 | x | Baked pullover tune: `PulloverOffset→4.0m`, `PulloverSpeed→3.5`, `HoldTimeAfterClear→2s`, `SeekTimeout→5s` | V3,V9 |
| T7 | x | Verify `TrafficControllerPatches` camera despawn ranges correct after T1 (spawn=80m, despawn=180m, safe=40m); adjust if needed | V4,V10 |
| T8 | x | SDK assembly: new project `src/EmergeNYC.ModdingSDK/`; `EmergeNYCSDK` entry point with `Init()` (called from CommunityFixes Plugin.Awake); `EventBus` typed static event router | V12,V13 |
| T9 | x | `EmergencyAPI`: `OnEmergencyStart(EmergencyEvent)`, `OnEmergencyEnd(EmergencyEvent)`, `OnCallDispatched(string address, string assignment)`, `OnAutoRespond()`; read-only: `ActiveEmergency`, `AllEvents[]` | V13,V15 |
| T10 | x | `FireAPI`: `OnFireIgnited(FireController)`, `OnFireExtinguished(FireController)`, `OnFireIntensityChanged(FireController,float)`, `OnWaterApplied(FireController,float)`; helpers: `IgniteFireAt(Vector3,float)`, `GetAllFires()` | V13,V14 |
| T11 | x | `EMSAPI`: `OnPatientSpawned(EMSPatient)`, `OnPatientBackboarded(EMSPatient)`, `OnTreatmentApplied(EMSPatient,string)`, `OnVitalsChanged(EMSPatient)`; read-only: `AllPatients`, `GetPatientCondition(EMSPatient,HumanBodyBones)` | V13,V15 |
| T12 | x | `TrafficAPI`: `RegisterEmergencyVehicle(Transform)`, `UnregisterEmergencyVehicle(Transform)`, `OnCarYieldStart(TSTrafficAI)`, `OnCarYieldEnd(TSTrafficAI)`, `OnCarSpawned(TSTrafficAI)`, `OnCarDespawned(TSTrafficAI)`, `GetYieldingCars()`, `SetSpawnDensity(float)` | V11,V13 |
| T13 | x | `CharacterAPI`: `OnCharacterSpawned(AIManager)`, `OnAITakeControl(AIManager)`, `OnPlayerTakeControl(AIManager)`, `OnEquipmentChanged(AIManager,string)`; helpers: `SpawnAI(firehouse,engine,role,pos,rot)`, `GetAllCharacters()` | V13,V14 |
| T14 | x | `VehicleAPI`: `OnVehicleSpawned(GameObject)`, `OnSirenActivated(FFD_SirenControl,SirenState)`, `OnSirenDeactivated(FFD_SirenControl)`, `OnAirhornUsed(FFD_Airhorn)`, `OnPoliceSirenChanged(NYPDSirenController)`; helper: `GetAllSirenVehicles()` | V13,V14 |
| T15 | x | `DispatchAPI`: `OnCallGenerated(string type,Vector3 pos)`, `OnUnitAssigned(string unit,Emergency)`, `OnTicketSent(string address,string box)`; helpers: `ForceEmergency(string type)`, `GetNearestEngine(Vector3)`, `GetNearestTruck(Vector3)` | V13,V15 |
| T16 | x | New project `src/EmergeNYC.TrafficAI/` — csproj refs game DLLs + ModdingSDK + BepInEx; `TrafficAIPlugin` BepInEx entry point; `CopyToPlugins` MSBuild target | V12 |
| T17 | x | `CustomTrafficVehicle` (CTV) MonoBehaviour skeleton — `State` enum (`Cruise/SlowDown/PullRight/HardStop/Yield/Resume`); acquire `RCC_CarControllerV3`, `TSNavigation`, `TSTrafficAI` refs on `Start`; disable `TSTrafficAI`; re-enable on `OnDisable` | V18,V19 |
| T18 | x | Cruise state — read `TSNavigation.RelativeWaypointPosition` → compute steer angle → `RCC.steerInput`; proportional throttle to `currentMaxSpeed` → `RCC.gasInput`; no game method calls (V21) | V19,V21 |
| T19 | x | Forward obstacle detection — `Physics.SphereCast` from offset origin (V20); `SlowDown` ramp 30m→8m (`brakeInput` proportional); `HardStop` ≤8m (`brakeInput=1, gasInput=0`); return to `Cruise` when path clear | V20 |
| T20 | x | Road edge clamping — `NavMesh.FindClosestEdge` + `TSLaneInfo.laneWidth`; compute `maxLateralDisplace`; expose as property used by `PullRight` and `Yield` states (V17) | V17 |
| T21 | x | EV siren detection — coroutine scans `EmergencyVehicleRegistry` every 0.1s; skip EVs with siren off (V16); for siren-active EVs in range: dot product → `PullRight` if beside/behind, `HardStop` if oncoming in lane ≤30m | V16 |
| T22 | x | Shoulder obstruction — `Physics.OverlapBox` in target pull-right space before entering `PullRight`; if blocked → `HardStop` instead; recheck on resume | V17 |
| T23 | x | Injection — `TSTrafficAI.OnEnable` Harmony postfix adds CTV component if absent, disables `TSTrafficAI`; `TSTrafficAI.OnDisable` postfix re-enables `TSTrafficAI`, destroys CTV | V18 |

---

## §B — Bug Log

| id | date | cause | fix |
|----|------|-------|-----|
| B1 | 2026-03-14 | `TSTrafficAI.canMove` setter calls `Object.Destroy(gameObject,20f)` on every set (even true) | `TSTrafficCanMoveFix` patches setter; V2 |
| B2 | 2026-03-14 | `fullStop` permanent braking: `num3=0` hardcoded → max brake always | `TSTrafficFullStopFix` clears stale players; V6 |
| B3 | 2026-03-14 | Negative `segDistance` after lane change freezes car | `TSTrafficGetBrakeFix` clamps to 0.5; V7 |
| B4 | 2026-03-14 | `TrafficController.updateHandler` uses spawner transform pos for despawn, not camera | `TrafficControllerPatches.UpdateHandler_Fixed`; V4 |
| B5 | 2026-03-14 | `TrafficController` forces `TrafficCongestion=Heavy` every tick ignoring user setting | `TrafficControllerPatches` guards on `level==0` |
| B6 | 2026-03-14 | `TrafficCarBaked.OnPathComplete` leaves ghost car when no junction found | `TrafficCarBakedOnPathCompletePatch` pools car |
| B7 | 2026-03-14 | `CastEmergencySounds` calls `BlockTraffic()+GetOutOfLane()` → canMove=false + hard swerve | `CastEmergencySoundsPatch` disables entirely |
| B8 | open | `TSTrafficSpawner.CheckFarCarsSingleThread` uses `myPosition` (spawner origin) not camera → despawns visible cars | fix: T1 |
| B9 | open | Siren broadcast uses flat 60m radius regardless of direction → cars behind miss signal, cars ahead over-react | fix: T4 |
